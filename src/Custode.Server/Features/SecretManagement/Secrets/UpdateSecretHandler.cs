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
    public async Task<SecretMetadataResponse> HandleAsync(Guid secretId, SecretWriteRequest request, CancellationToken cancellationToken)
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
            request.ContentBase64));

        if (await dbContext.Secrets.AnyAsync(
                other => other.FolderId == secret.FolderId && other.Name == name && other.Id != secret.Id,
                cancellationToken))
        {
            throw new VaultConflictException("A secret with this name already exists in the folder.");
        }

        var folderPath = await dbContext.Folders
            .Where(folder => folder.Id == secret.FolderId)
            .Select(folder => folder.Path)
            .SingleAsync(cancellationToken);

        await AuditedWork.CommitAsync(dbContext, async () =>
        {
            secret.Name = name;
            secret.FileName = value.FileName;
            secret.ProtectedPayload = value.ProtectedPayload;
            secret.UpdatedAtUtc = DateTimeOffset.UtcNow;

            await auditTrail.RecordAsync(
                new AuditRecord(folderGuard.UserId, AuditActions.SecretUpdated, "secret", secret.Id.ToString("D"), "folder", secret.FolderId.ToString("D")),
                cancellationToken);
            return true;
        }, cancellationToken);

        return SecretMetadataMapper.ToResponse(secret, folderPath);
    }
}
