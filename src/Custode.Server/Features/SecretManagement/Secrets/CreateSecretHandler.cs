// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Custode.Infrastructure.Access;
using Custode.Infrastructure.Audit;
using Custode.Infrastructure.Persistence;
using Custode.Infrastructure.Vault;
using Custode.Server.Features.SecretManagement.Common;
using Microsoft.EntityFrameworkCore;

namespace Custode.Server.Features.SecretManagement.Secrets;

internal sealed class CreateSecretHandler(
    AppDbContext dbContext,
    FolderGuard folderGuard,
    SecretValueCodec codec,
    IAuditTrail auditTrail)
{
    public async Task<SecretMetadataResponse> HandleAsync(CreateSecretRequest request, CancellationToken cancellationToken)
    {
        var name = SecretName.Normalize(request.Name);
        var type = SecretValueCodec.ParseType(request.Type);
        var folderPath = FolderPath.Normalize(request.FolderPath);
        var folder = await folderGuard.GetFolderAsync(folderPath, cancellationToken);

        await folderGuard.AuthorizeAsync(
            folder.Id,
            FolderPermissions.Create,
            AuditActions.SecretCreated,
            "folder",
            folder.Id.ToString("D"),
            cancellationToken: cancellationToken);

        var value = codec.Protect(folderGuard.TenantId, type, new SecretValueInput(
            request.Username,
            request.Password,
            request.Value,
            request.FileName,
            request.ContentBase64,
            request.Fields,
            request.Pairs?.Select(pair => new SecretPairInput(pair.Key, pair.Value)).ToList()));

        if (await dbContext.Secrets.AnyAsync(secret => secret.FolderId == folder.Id && secret.Name == name, cancellationToken))
        {
            throw new VaultConflictException("A secret with this name already exists in the folder.");
        }

        var now = DateTimeOffset.UtcNow;
        var secret = new Secret
        {
            Id = Guid.NewGuid(),
            TenantId = folderGuard.TenantId,
            FolderId = folder.Id,
            Name = name,
            Type = type,
            FileName = value.FileName,
            ProtectedPayload = value.ProtectedPayload,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            CurrentVersion = 1,
            ConcurrencyStamp = Guid.NewGuid(),
        };

        await AuditedWork.CommitAsync(dbContext, async () =>
        {
            dbContext.Secrets.Add(secret);
            dbContext.SecretVersions.Add(SecretVersioning.Snapshot(secret, SecretVersionChangeKind.Created, null, folderGuard.UserId, now));
            await auditTrail.RecordAsync(
                new AuditRecord(folderGuard.UserId, AuditActions.SecretCreated, "secret", secret.Id.ToString("D"), "folder", folder.Id.ToString("D")),
                cancellationToken);
            return true;
        }, cancellationToken);

        return SecretMetadataMapper.ToResponse(secret, folder.Path);
    }
}
