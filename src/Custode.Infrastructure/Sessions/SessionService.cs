// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Custode.Infrastructure.Administration;
using Custode.Infrastructure.Audit;
using Custode.Infrastructure.Identity;
using Custode.Infrastructure.Persistence;
using Custode.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Custode.Infrastructure.Sessions;

public sealed record StartedSession(Guid SessionId, string RefreshToken, DateTimeOffset RefreshExpiresAtUtc);

public sealed record RefreshedSession(Guid SessionId, Guid TenantId, Guid UserId, string RefreshToken, DateTimeOffset RefreshExpiresAtUtc);

public sealed record SessionCheck(bool IsValid, bool PasswordChangeRequired)
{
    public static readonly SessionCheck Invalid = new(false, false);
}

// Owns every session operation. Each operation opens its own context under an explicit tenant scope, because the
// callers (sign-in, refresh) may run before any tenant is bound to the request.
public sealed class SessionService(
    DbContextOptions<AppDbContext> options,
    SessionCredentialResolver resolver,
    TenantPolicyReader policies,
    IOptions<SessionOptions> sessionOptions,
    TimeProvider clock) : ISessionRevoker
{
    public const string SignedOutReason = "signed-out";
    public const string PasswordChangedReason = "password-changed";
    public const string MemberDisabledReason = "member-disabled";
    public const string AdministratorWithdrawnReason = "administrator-withdrawn";
    public const string ReuseDetectedReason = "reuse-detected";

    private const int MaxCommitAttempts = 3;
    private static readonly TimeSpan ActivityWriteInterval = TimeSpan.FromMinutes(1);

    public async Task<StartedSession> StartAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var policy = await policies.GetAsync(user.TenantId, cancellationToken);
        var token = RefreshTokens.Generate();

        var session = new Session
        {
            Id = Guid.NewGuid(),
            TenantId = user.TenantId,
            UserId = user.Id,
            StartedAtUtc = now,
            LastActivityAtUtc = now,
        };

        await using var db = Scoped(user.TenantId);
        db.Sessions.Add(session);
        db.RefreshTokens.Add(NewRefreshToken(session, token, now));
        await db.SaveChangesAsync(cancellationToken);

        return new StartedSession(session.Id, token, now.AddDays(policy.RefreshTokenLifetimeDays));
    }

    // Returns null when the refresh must be refused. The reason is not exposed, so a caller cannot distinguish them.
    public async Task<RefreshedSession?> RefreshAsync(string presentedToken, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var credential = await resolver.ResolveAsync(presentedToken, cancellationToken);

        if (credential is null)
        {
            return null;
        }

        var policy = await policies.GetAsync(credential.TenantId, cancellationToken);
        await using var db = Scoped(credential.TenantId);

        var session = await db.Sessions.FirstOrDefaultAsync(entity => entity.Id == credential.SessionId, cancellationToken);

        if (session is null || session.RevokedAtUtc is not null)
        {
            return null;
        }

        if (credential.ReplacedAtUtc is { } replacedAtUtc)
        {
            if (now - replacedAtUtc <= GraceWindow)
            {
                return null;
            }

            await new AuditTrail(db, new FixedTenantContext(credential.TenantId)).RecordDeniedAsync(
                new AuditRecord(session.UserId, AuditActions.RefreshTokenReuseDetected, "session", session.Id.ToString("D"), "user", session.UserId.ToString("D")),
                cancellationToken);

            Revoke(session, ReuseDetectedReason, now);
            await db.SaveChangesAsync(cancellationToken);
            return null;
        }

        var userEnabled = await db.Users
            .AsNoTracking()
            .Where(user => user.Id == session.UserId && user.TenantId == session.TenantId)
            .Select(user => (bool?)user.IsEnabled)
            .FirstOrDefaultAsync(cancellationToken);

        if (EndReason(session, userEnabled == true, now, policy) is { } endReason)
        {
            Revoke(session, endReason, now);
            await db.SaveChangesAsync(cancellationToken);
            return null;
        }

        var stored = await db.RefreshTokens.FirstAsync(token => token.Id == credential.TokenId, cancellationToken);
        stored.ReplacedAtUtc = now;
        stored.ConcurrencyStamp = Guid.NewGuid();

        var replacement = RefreshTokens.Generate();
        db.RefreshTokens.Add(NewRefreshToken(session, replacement, now));

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // A concurrent refresh with the same token won the race. This attempt is refused without ending the session.
            return null;
        }

        return new RefreshedSession(
            session.Id,
            session.TenantId,
            session.UserId,
            replacement,
            session.StartedAtUtc.AddDays(policy.RefreshTokenLifetimeDays));
    }

    public async Task<SessionCheck> CheckAsync(Guid tenantId, Guid userId, Guid sessionId, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var policy = await policies.GetAsync(tenantId, cancellationToken);
        await using var db = Scoped(tenantId);

        var session = await db.Sessions.FirstOrDefaultAsync(entity => entity.Id == sessionId, cancellationToken);

        if (session is null || session.UserId != userId || session.RevokedAtUtc is not null)
        {
            return SessionCheck.Invalid;
        }

        var user = await db.Users
            .AsNoTracking()
            .Where(entity => entity.Id == userId && entity.TenantId == tenantId)
            .Select(entity => new { entity.IsEnabled, entity.MustChangePassword })
            .FirstOrDefaultAsync(cancellationToken);

        if (user is null || EndReason(session, user.IsEnabled, now, policy) is not null)
        {
            return SessionCheck.Invalid;
        }

        if (now - session.LastActivityAtUtc >= ActivityWriteInterval)
        {
            session.LastActivityAtUtc = now;
            await db.SaveChangesAsync(cancellationToken);
        }

        return new SessionCheck(true, user.MustChangePassword);
    }

    public async Task RevokeAsync(Guid tenantId, Guid sessionId, string reason, Guid actorUserId, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();

        await CommitAsync(tenantId, async db =>
        {
            var session = await db.Sessions.FirstOrDefaultAsync(entity => entity.Id == sessionId, cancellationToken);

            if (session is null || session.RevokedAtUtc is not null)
            {
                return 0;
            }

            Revoke(session, reason, now);
            await RecordRevocationAsync(db, tenantId, session, actorUserId, cancellationToken);
            return 1;
        }, cancellationToken);
    }

    // Revokes the session named by a refresh token, acting as the session's own user. Unknown tokens are ignored.
    public async Task SignOutAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var credential = await resolver.ResolveAsync(refreshToken, cancellationToken);

        if (credential is null)
        {
            return;
        }

        var now = clock.GetUtcNow();

        await CommitAsync(credential.TenantId, async db =>
        {
            var session = await db.Sessions.FirstOrDefaultAsync(entity => entity.Id == credential.SessionId, cancellationToken);

            if (session is null || session.RevokedAtUtc is not null)
            {
                return 0;
            }

            Revoke(session, SignedOutReason, now);
            await RecordRevocationAsync(db, credential.TenantId, session, session.UserId, cancellationToken);
            return 1;
        }, cancellationToken);
    }

    public Task<int> RevokeUserSessionsAsync(
        Guid tenantId,
        Guid userId,
        string reason,
        Guid actorUserId,
        Guid? exceptSessionId,
        CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();

        return CommitAsync(tenantId, async db =>
        {
            var sessions = await db.Sessions
                .Where(entity => entity.UserId == userId
                    && entity.TenantId == tenantId
                    && entity.RevokedAtUtc == null
                    && (exceptSessionId == null || entity.Id != exceptSessionId))
                .ToListAsync(cancellationToken);

            foreach (var session in sessions)
            {
                Revoke(session, reason, now);
                await RecordRevocationAsync(db, tenantId, session, actorUserId, cancellationToken);
            }

            return sessions.Count;
        }, cancellationToken);
    }

    private TimeSpan GraceWindow => TimeSpan.FromSeconds(sessionOptions.Value.ReuseGraceSeconds);

    private AppDbContext Scoped(Guid tenantId)
    {
        return new AppDbContext(options, new FixedTenantContext(tenantId));
    }

    // Retries only on a concurrency conflict, with a fresh context and freshly loaded entities.
    private async Task<int> CommitAsync(Guid tenantId, Func<AppDbContext, Task<int>> work, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            await using var db = Scoped(tenantId);
            var changed = await work(db);

            if (changed == 0)
            {
                return 0;
            }

            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return changed;
            }
            catch (DbUpdateConcurrencyException) when (attempt < MaxCommitAttempts)
            {
            }
        }
    }

    private static async Task RecordRevocationAsync(
        AppDbContext db,
        Guid tenantId,
        Session session,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        await new AuditTrail(db, new FixedTenantContext(tenantId)).RecordAsync(
            new AuditRecord(actorUserId, AuditActions.SessionRevoked, "session", session.Id.ToString("D"), "user", session.UserId.ToString("D")),
            cancellationToken);
    }

    private static void Revoke(Session session, string reason, DateTimeOffset now)
    {
        session.RevokedAtUtc = now;
        session.RevocationReason = reason;
    }

    private static string? EndReason(Session session, bool userEnabled, DateTimeOffset now, TenantPolicy policy)
    {
        if (!userEnabled)
        {
            return "user-disabled";
        }

        if (now - session.LastActivityAtUtc > TimeSpan.FromMinutes(policy.IdleTimeoutMinutes))
        {
            return "idle-timeout";
        }

        if (now - session.StartedAtUtc > TimeSpan.FromDays(policy.RefreshTokenLifetimeDays))
        {
            return "absolute-lifetime";
        }

        return null;
    }

    private static RefreshToken NewRefreshToken(Session session, string token, DateTimeOffset now)
    {
        return new RefreshToken
        {
            Id = Guid.NewGuid(),
            TenantId = session.TenantId,
            SessionId = session.Id,
            TokenHash = RefreshTokens.Hash(token),
            IssuedAtUtc = now,
            ConcurrencyStamp = Guid.NewGuid(),
        };
    }
}
