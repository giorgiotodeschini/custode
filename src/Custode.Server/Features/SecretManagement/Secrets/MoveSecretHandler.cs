// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Custode.Infrastructure.Access;
using Custode.Infrastructure.Audit;
using Custode.Infrastructure.Persistence;
using Custode.Infrastructure.Vault;
using Custode.Server.Features.SecretManagement.Common;
using Microsoft.EntityFrameworkCore;

namespace Custode.Server.Features.SecretManagement.Secrets;

internal sealed class MoveSecretHandler(AppDbContext dbContext, FolderGuard folderGuard, IAuditTrail auditTrail)
{
    public async Task<SecretMetadataResponse> HandleAsync(Guid secretId, MoveSecretRequest request, CancellationToken cancellationToken)
    {
        var secret = await SecretLookup.FindAsync(dbContext, secretId, cancellationToken);

        await folderGuard.AuthorizeAsync(
            secret.FolderId,
            FolderPermissions.Delete,
            AuditActions.SecretMoved,
            "secret",
            secretId.ToString("D"),
            "folder",
            secret.FolderId.ToString("D"),
            cancellationToken);

        var destination = await folderGuard.GetFolderAsync(FolderPath.Normalize(request.FolderPath), cancellationToken);

        if (destination.Id != secret.FolderId)
        {
            await folderGuard.AuthorizeAsync(
                destination.Id,
                FolderPermissions.Create,
                AuditActions.SecretMoved,
                "secret",
                secretId.ToString("D"),
                "folder",
                destination.Id.ToString("D"),
                cancellationToken);

            if (await dbContext.Secrets.AnyAsync(
                    other => other.FolderId == destination.Id && other.Name == secret.Name && other.Id != secret.Id,
                    cancellationToken))
            {
                throw new VaultConflictException("A secret with this name already exists in the destination folder.");
            }

            await AuditedWork.CommitAsync(dbContext, async () =>
            {
                secret.FolderId = destination.Id;
                secret.UpdatedAtUtc = DateTimeOffset.UtcNow;

                await auditTrail.RecordAsync(
                    new AuditRecord(folderGuard.UserId, AuditActions.SecretMoved, "secret", secretId.ToString("D"), "folder", destination.Id.ToString("D")),
                    cancellationToken);
                return true;
            }, cancellationToken);
        }

        return SecretMetadataMapper.ToResponse(secret, destination.Path);
    }
}
