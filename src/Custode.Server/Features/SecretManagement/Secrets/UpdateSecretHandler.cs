// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Custode.Infrastructure.Access;
using Custode.Infrastructure.Audit;
using Custode.Infrastructure.Persistence;
using Custode.Infrastructure.Vault;
using Custode.Server.Features.SecretManagement.Common;
using Microsoft.EntityFrameworkCore;

namespace Custode.Server.Features.SecretManagement.Secrets;

internal sealed class UpdateSecretHandler(
    AppDbContext dbContext,
    FolderGuard folderGuard,
    SecretValueCodec codec,
    IAuditTrail auditTrail)
{
    public async Task<SecretMetadataResponse> HandleAsync(Guid secretId, SecretWriteRequest request, string? ifMatch, CancellationToken cancellationToken)
    {
        var secret = await SecretLookup.FindAsync(dbContext, secretId, cancellationToken);

        await folderGuard.AuthorizeAsync(
            secret.FolderId,
            FolderPermissions.Write,
            AuditActions.SecretUpdated,
            "secret",
            secretId.ToString("D"),
            "folder",
            secret.FolderId.ToString("D"),
            cancellationToken);

        VersionTokens.ParseIfMatch(ifMatch);

        var name = SecretName.Normalize(request.Name);
        var type = SecretValueCodec.ParseType(request.Type);

        if (type != secret.Type)
        {
            throw new InvalidVaultRequestException("The type of an existing secret cannot be changed.");
        }

        var value = codec.Protect(folderGuard.TenantId, type, new SecretValueInput(
            request.Username,
            request.Password,
            request.Value,
            request.FileName,
            request.ContentBase64,
            request.Fields,
            request.Pairs?.Select(pair => new SecretPairInput(pair.Key, pair.Value)).ToList()));

        var folderPath = await dbContext.Folders
            .Where(folder => folder.Id == secret.FolderId)
            .Select(folder => folder.Path)
            .SingleAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;

        // The secret is reloaded inside the unit of work, so a retry after a concurrency conflict sees the newer version
        // and fails the version check instead of overwriting it.
        await AuditedWork.CommitAsync(dbContext, async () =>
        {
            var current = await dbContext.Secrets.FirstOrDefaultAsync(entity => entity.Id == secretId, cancellationToken)
                ?? throw new VaultNotFoundException("The requested folder or item does not exist.");

            VersionTokens.Require(current.CurrentVersion, ifMatch);

            if (await dbContext.Secrets.AnyAsync(
                    other => other.FolderId == current.FolderId && other.Name == name && other.Id != current.Id,
                    cancellationToken))
            {
                throw new VaultConflictException("A secret with this name already exists in the folder.");
            }

            current.Name = name;
            current.FileName = value.FileName;
            current.ProtectedPayload = value.ProtectedPayload;
            current.UpdatedAtUtc = now;
            current.CurrentVersion += 1;
            current.ConcurrencyStamp = Guid.NewGuid();

            dbContext.SecretVersions.Add(SecretVersioning.Snapshot(current, SecretVersionChangeKind.Updated, null, folderGuard.UserId, now));

            await auditTrail.RecordAsync(
                new AuditRecord(folderGuard.UserId, AuditActions.SecretUpdated, "secret", secretId.ToString("D"), "folder", current.FolderId.ToString("D")),
                cancellationToken);
            return true;
        }, cancellationToken);

        var updated = await SecretLookup.FindAsync(dbContext, secretId, cancellationToken);

        return SecretMetadataMapper.ToResponse(updated, folderPath);
    }
}
