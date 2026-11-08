// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Custode.Infrastructure.Access;
using Custode.Infrastructure.Audit;
using Custode.Infrastructure.Persistence;
using Custode.Infrastructure.Vault;
using Custode.Server.Features.SecretManagement.Common;
using Custode.Server.Features.SecretManagement.Secrets;
using Microsoft.EntityFrameworkCore;

namespace Custode.Server.Features.SecretManagement.Versions;

internal sealed class ListVersionsHandler(AppDbContext dbContext, FolderGuard folderGuard)
{
    public async Task<IReadOnlyList<SecretVersionResponse>> HandleAsync(Guid secretId, CancellationToken cancellationToken)
    {
        var secret = await SecretLookup.FindAsync(dbContext, secretId, cancellationToken);

        await folderGuard.AuthorizeAsync(secret.FolderId, FolderPermissions.Read, cancellationToken: cancellationToken);

        var versions = await dbContext.SecretVersions
            .AsNoTracking()
            .Where(version => version.SecretId == secretId)
            .OrderByDescending(version => version.Number)
            .ToListAsync(cancellationToken);

        return versions.Select(version => new SecretVersionResponse(
            version.Number,
            version.Name,
            version.FileName,
            SecretVersioning.ChangeKindName(version.ChangeKind),
            version.CreatedAtUtc,
            version.CreatedByUserId,
            version.SourceVersionNumber)).ToList();
    }
}

internal sealed class ReadVersionValueHandler(
    AppDbContext dbContext,
    FolderGuard folderGuard,
    SecretValueCodec codec,
    IAuditTrail auditTrail)
{
    public async Task<SecretValueResponse> HandleAsync(Guid secretId, int number, CancellationToken cancellationToken)
    {
        var secret = await SecretLookup.FindAsync(dbContext, secretId, cancellationToken);

        await folderGuard.AuthorizeAsync(
            secret.FolderId,
            FolderPermissions.Read,
            AuditActions.SecretValueRead,
            "secret",
            secretId.ToString("D"),
            "folder",
            secret.FolderId.ToString("D"),
            cancellationToken);

        var version = await dbContext.SecretVersions
            .AsNoTracking()
            .FirstOrDefaultAsync(entity => entity.SecretId == secretId && entity.Number == number, cancellationToken)
            ?? throw new VaultNotFoundException("The requested version does not exist.");

        var folderPath = await dbContext.Folders
            .Where(folder => folder.Id == secret.FolderId)
            .Select(folder => folder.Path)
            .SingleAsync(cancellationToken);

        var value = codec.Unprotect(folderGuard.TenantId, version.ProtectedPayload);
        var pairs = value.Pairs?.Select(pair => new SecretPairResponse(pair.Key, pair.Value)).ToList();

        await AuditedWork.CommitAsync(dbContext, async () =>
        {
            await auditTrail.RecordAsync(
                new AuditRecord(folderGuard.UserId, AuditActions.SecretValueRead, "secret", secretId.ToString("D"), "secret version", version.Id.ToString("D")),
                cancellationToken);
            return true;
        }, cancellationToken);

        return new SecretValueResponse(
            secret.Id,
            version.Name,
            SecretValueCodec.FormatType(secret.Type),
            folderPath,
            version.FileName,
            value.Username,
            value.Password,
            value.Value,
            value.ContentBase64,
            secret.Type == SecretType.Structured ? pairs : null,
            secret.Type == SecretType.KeyValue ? pairs : null,
            version.Number);
    }
}

internal sealed class RollbackSecretHandler(
    AppDbContext dbContext,
    FolderGuard folderGuard,
    IAuditTrail auditTrail)
{
    public async Task<SecretMetadataResponse> HandleAsync(Guid secretId, int number, string? ifMatch, CancellationToken cancellationToken)
    {
        var secret = await SecretLookup.FindAsync(dbContext, secretId, cancellationToken);

        // Rollback needs Write only: it restores a stored payload without revealing it.
        await folderGuard.AuthorizeAsync(
            secret.FolderId,
            FolderPermissions.Write,
            AuditActions.SecretRolledBack,
            "secret",
            secretId.ToString("D"),
            "folder",
            secret.FolderId.ToString("D"),
            cancellationToken);

        VersionTokens.ParseIfMatch(ifMatch);

        var folderPath = await dbContext.Folders
            .Where(folder => folder.Id == secret.FolderId)
            .Select(folder => folder.Path)
            .SingleAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;

        await AuditedWork.CommitAsync(dbContext, async () =>
        {
            var current = await dbContext.Secrets.FirstOrDefaultAsync(entity => entity.Id == secretId, cancellationToken)
                ?? throw new VaultNotFoundException("The requested folder or item does not exist.");

            VersionTokens.Require(current.CurrentVersion, ifMatch);

            var source = await dbContext.SecretVersions
                .FirstOrDefaultAsync(entity => entity.SecretId == secretId && entity.Number == number, cancellationToken)
                ?? throw new VaultNotFoundException("The requested version does not exist.");

            if (number == current.CurrentVersion)
            {
                throw new VaultConflictException("The secret is already at this version.");
            }

            if (await dbContext.Secrets.AnyAsync(
                    other => other.FolderId == current.FolderId && other.Name == source.Name && other.Id != current.Id,
                    cancellationToken))
            {
                throw new VaultConflictException("Another secret in the folder now uses the name of this version.");
            }

            current.Name = source.Name;
            current.FileName = source.FileName;
            current.ProtectedPayload = source.ProtectedPayload;
            current.UpdatedAtUtc = now;
            current.CurrentVersion += 1;
            current.ConcurrencyStamp = Guid.NewGuid();

            dbContext.SecretVersions.Add(new SecretVersion
            {
                Id = Guid.NewGuid(),
                TenantId = current.TenantId,
                SecretId = current.Id,
                Number = current.CurrentVersion,
                Name = source.Name,
                FileName = source.FileName,
                ProtectedPayload = source.ProtectedPayload,
                ChangeKind = SecretVersionChangeKind.RolledBack,
                SourceVersionNumber = number,
                CreatedAtUtc = now,
                CreatedByUserId = folderGuard.UserId,
            });

            await auditTrail.RecordAsync(
                new AuditRecord(folderGuard.UserId, AuditActions.SecretRolledBack, "secret", secretId.ToString("D"), "secret version", source.Id.ToString("D")),
                cancellationToken);
            return true;
        }, cancellationToken);

        var rolledBack = await SecretLookup.FindAsync(dbContext, secretId, cancellationToken);

        return SecretMetadataMapper.ToResponse(rolledBack, folderPath);
    }
}
