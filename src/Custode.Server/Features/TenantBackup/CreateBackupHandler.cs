// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using System.Text.Json;
using Custode.Infrastructure.Access;
using Custode.Infrastructure.Administration;
using Custode.Infrastructure.Audit;
using Custode.Infrastructure.Backup;
using Custode.Infrastructure.Identity;
using Custode.Infrastructure.Persistence;
using Custode.Infrastructure.Tenancy;
using Custode.Infrastructure.Vault;
using Custode.Server.Features.SecretManagement.Common;
using Custode.Server.Features.SecretManagement.Secrets;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Custode.Server.Features.TenantBackup;

public sealed record BackupRequest(string? Passphrase);

public sealed record BackupFile(byte[] Content, string FileName);

internal sealed class CreateBackupHandler(
    AppDbContext dbContext,
    ITenantContext tenantContext,
    ICurrentUser currentUser,
    IFolderAccess folderAccess,
    SecretValueCodec codec,
    IAuditTrail auditTrail,
    IOptions<BackupOptions> options)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public async Task<BackupFile> HandleAsync(BackupRequest request, CancellationToken cancellationToken)
    {
        var tenantId = tenantContext.RequireTenantId();
        var userId = currentUser.Id;

        // A passphrase that is too short is refused before any content is read.
        var passphrase = request.Passphrase is null ? null : BackupContainer.Normalize(request.Passphrase);

        if (passphrase is null || !BackupContainer.IsLongEnough(passphrase))
        {
            throw new InvalidVaultRequestException($"The passphrase must be at least {BackupContainer.MinimumPassphraseLength} characters long.");
        }

        var permissions = await folderAccess.GetEffectivePermissionsForAllFoldersAsync(userId, cancellationToken);
        var folderPaths = await dbContext.Folders.AsNoTracking().Select(folder => new { folder.Id, folder.Path }).ToListAsync(cancellationToken);

        if (folderPaths.Any(folder => !permissions.GetValueOrDefault(folder.Id).HasFlag(FolderPermissions.Read)))
        {
            await auditTrail.RecordDeniedAsync(
                new AuditRecord(userId, AuditActions.TenantBackupExported, "tenant", tenantId.ToString("D")),
                cancellationToken);

            throw new VaultForbiddenException("A backup requires Read permission on every folder of the tenant.");
        }

        var content = await BuildContentAsync(tenantId, cancellationToken);
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(content, JsonOptions);

        if (plaintext.Length > options.Value.MaxBackupBytes)
        {
            throw new VaultPayloadTooLargeException("The tenant content exceeds the maximum backup size.");
        }

        var backupId = Guid.NewGuid();
        var container = BackupContainer.Seal(plaintext, passphrase);
        Array.Clear(plaintext);

        await AuditedWork.CommitAsync(dbContext, async () =>
        {
            await auditTrail.RecordAsync(
                new AuditRecord(userId, AuditActions.TenantBackupExported, "tenant", tenantId.ToString("D"), "backup", backupId.ToString("D")),
                cancellationToken);
            return true;
        }, cancellationToken);

        return new BackupFile(container, $"custode-backup-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssZ}.custode-backup");
    }

    private async Task<BackupContent> BuildContentAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var folders = await dbContext.Folders.AsNoTracking().OrderBy(folder => folder.Path).ToListAsync(cancellationToken);
        var paths = folders.ToDictionary(folder => folder.Id, folder => folder.Path);

        var secrets = await (
            from secret in dbContext.Secrets.AsNoTracking()
            join folder in dbContext.Folders.AsNoTracking() on secret.FolderId equals folder.Id
            select new { secret, folder.Path }).ToListAsync(cancellationToken);

        var versions = await dbContext.SecretVersions.AsNoTracking().OrderBy(version => version.Number).ToListAsync(cancellationToken);

        var secretEntries = secrets.Select(entry => new BackupSecret(
            entry.secret.Id,
            entry.Path,
            entry.secret.Name,
            SecretValueCodec.FormatType(entry.secret.Type),
            entry.secret.FileName,
            entry.secret.CurrentVersion,
            entry.secret.CreatedAtUtc,
            entry.secret.UpdatedAtUtc,
            versions.Where(version => version.SecretId == entry.secret.Id).Select(version => new BackupVersion(
                version.Number,
                version.Name,
                version.FileName,
                SecretVersioning.ChangeKindName(version.ChangeKind),
                version.SourceVersionNumber,
                version.CreatedAtUtc,
                version.CreatedByUserId,
                codec.Unprotect(tenantId, version.ProtectedPayload))).ToList())).ToList();

        var groups = await dbContext.Groups.AsNoTracking().Select(group => new BackupGroup(group.Id, group.Name)).ToListAsync(cancellationToken);
        var memberships = await dbContext.GroupMemberships.AsNoTracking().Select(membership => new BackupMembership(membership.GroupId, membership.UserId)).ToListAsync(cancellationToken);
        var grants = await dbContext.FolderGrants.AsNoTracking().Select(grant => new BackupGrant(paths[grant.FolderId], grant.PrincipalKind.ToString(), grant.PrincipalId, (int)grant.Permissions)).ToListAsync(cancellationToken);

        var policy = await dbContext.TenantPolicies.AsNoTracking().FirstOrDefaultAsync(entity => entity.TenantId == tenantId, cancellationToken);

        // Members are exported without password hashes, tokens, or sessions.
        var users = await dbContext.Users.AsNoTracking().Where(user => user.TenantId == tenantId).ToListAsync(cancellationToken);
        var roleNames = await (from link in dbContext.UserRoles.AsNoTracking() join role in dbContext.Roles.AsNoTracking() on link.RoleId equals role.Id select new { link.UserId, role.Name }).ToListAsync(cancellationToken);
        var members = users.Select(user => new BackupMember(
            user.Id,
            user.UserName!,
            roleNames.Where(entry => entry.UserId == user.Id).Select(entry => entry.Name!).OrderBy(name => name).ToList(),
            user.IsEnabled,
            user.MustChangePassword)).ToList();

        var audit = await dbContext.AuditEntries.AsNoTracking().OrderBy(entry => entry.Sequence).ToListAsync(cancellationToken);
        var head = await dbContext.AuditChainHeads.AsNoTracking().FirstOrDefaultAsync(entry => entry.TenantId == tenantId, cancellationToken);

        return new BackupContent(
            new BackupManifest(BackupContainer.FormatVersion, tenantId, DateTimeOffset.UtcNow, folders.Count, secretEntries.Count, groups.Count, members.Count, audit.Count),
            folders.Select(folder => new BackupFolder(folder.Path, folder.ParentId is { } parent ? paths.GetValueOrDefault(parent) : null, folder.InheritsPermissions)).ToList(),
            secretEntries,
            groups,
            memberships,
            grants,
            policy is null ? null : new BackupPolicy(policy.IdleTimeoutMinutes, policy.AccessTokenLifetimeMinutes, policy.RefreshTokenLifetimeDays, policy.ClipboardClearSeconds),
            members,
            audit.Select(entry => new BackupAuditEntry(entry.Sequence, entry.OccurredAtUtc, entry.ActorUserId, entry.Action, entry.Outcome, entry.TargetType, entry.TargetId, entry.SecondaryTargetType, entry.SecondaryTargetId, entry.Hash, entry.PreviousHash)).ToList(),
            head is null ? null : new BackupChainHead(head.LastSequence, head.LastHash));
    }

    private sealed record BackupContent(
        BackupManifest Manifest,
        IReadOnlyList<BackupFolder> Folders,
        IReadOnlyList<BackupSecret> Secrets,
        IReadOnlyList<BackupGroup> Groups,
        IReadOnlyList<BackupMembership> Memberships,
        IReadOnlyList<BackupGrant> Grants,
        BackupPolicy? Policy,
        IReadOnlyList<BackupMember> Members,
        IReadOnlyList<BackupAuditEntry> Audit,
        BackupChainHead? ChainHead);

    private sealed record BackupManifest(int FormatVersion, Guid TenantId, DateTimeOffset CreatedAtUtc, int Folders, int Secrets, int Groups, int Members, int AuditEntries);

    private sealed record BackupFolder(string Path, string? ParentPath, bool InheritsPermissions);

    private sealed record BackupSecret(Guid Id, string FolderPath, string Name, string Type, string? FileName, int CurrentVersion, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc, IReadOnlyList<BackupVersion> Versions);

    private sealed record BackupVersion(int Number, string Name, string? FileName, string ChangeKind, int? SourceVersion, DateTimeOffset CreatedAtUtc, Guid CreatedByUserId, StoredSecretValue Value);

    private sealed record BackupGroup(Guid Id, string Name);

    private sealed record BackupMembership(Guid GroupId, Guid UserId);

    private sealed record BackupGrant(string FolderPath, string PrincipalKind, Guid PrincipalId, int Permissions);

    private sealed record BackupPolicy(int IdleTimeoutMinutes, int AccessTokenLifetimeMinutes, int RefreshTokenLifetimeDays, int ClipboardClearSeconds);

    private sealed record BackupMember(Guid Id, string Login, IReadOnlyList<string> Roles, bool IsEnabled, bool MustChangePassword);

    private sealed record BackupAuditEntry(long Sequence, DateTimeOffset OccurredAtUtc, Guid ActorUserId, string Action, string Outcome, string? TargetType, string? TargetId, string? SecondaryTargetType, string? SecondaryTargetId, string Hash, string PreviousHash);

    private sealed record BackupChainHead(long LastSequence, string LastHash);
}
