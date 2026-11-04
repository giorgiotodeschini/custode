// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Custode.Infrastructure.Access;
using Custode.Infrastructure.Audit;
using Custode.Infrastructure.Persistence;
using Custode.Infrastructure.Vault;
using Custode.Server.Features.SecretManagement.Common;
using Microsoft.EntityFrameworkCore;

namespace Custode.Server.Features.SecretManagement.Secrets;

internal sealed class ReadSecretValueHandler(
    AppDbContext dbContext,
    FolderGuard folderGuard,
    SecretValueCodec codec,
    IAuditTrail auditTrail)
{
    public async Task<SecretValueResponse> HandleAsync(Guid secretId, CancellationToken cancellationToken)
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

        var folderPath = await dbContext.Folders
            .Where(folder => folder.Id == secret.FolderId)
            .Select(folder => folder.Path)
            .SingleAsync(cancellationToken);

        var value = codec.Unprotect(folderGuard.TenantId, secret.ProtectedPayload);

        await AuditedWork.CommitAsync(dbContext, async () =>
        {
            await auditTrail.RecordAsync(
                new AuditRecord(folderGuard.UserId, AuditActions.SecretValueRead, "secret", secretId.ToString("D"), "folder", secret.FolderId.ToString("D")),
                cancellationToken);
            return true;
        }, cancellationToken);

        return new SecretValueResponse(
            secret.Id,
            secret.Name,
            SecretValueCodec.FormatType(secret.Type),
            folderPath,
            secret.FileName,
            value.Username,
            value.Password,
            value.Value,
            value.ContentBase64);
    }
}
