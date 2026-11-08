// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Custode.Infrastructure.Persistence;
using Custode.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Custode.Infrastructure.Sessions;

// The only tenant-free data read available to an unauthenticated request: it resolves a refresh token hash to the
// session it belongs to, and nothing else. The caller binds its scope to the returned tenant.
public sealed record SessionCredential(Guid TokenId, Guid SessionId, Guid TenantId, DateTimeOffset? ReplacedAtUtc);

public sealed class SessionCredentialResolver(DbContextOptions<AppDbContext> options)
{
    public async Task<SessionCredential?> ResolveAsync(string refreshToken, CancellationToken cancellationToken)
    {
        await using var db = new AppDbContext(options, new UnscopedTenantContext());

        return await CredentialQuery(db, RefreshTokens.Hash(refreshToken)).SingleOrDefaultAsync(cancellationToken);
    }

    // Exposed so that the selected columns can be checked against the generated SQL.
    public static IQueryable<SessionCredential> CredentialQuery(AppDbContext db, string tokenHash)
    {
        return db.RefreshTokens
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(token => token.TokenHash == tokenHash)
            .Select(token => new SessionCredential(token.Id, token.SessionId, token.TenantId, token.ReplacedAtUtc));
    }
}
