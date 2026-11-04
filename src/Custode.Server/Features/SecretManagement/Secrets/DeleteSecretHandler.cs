// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Custode.Infrastructure.Access;
using Custode.Infrastructure.Audit;
using Custode.Infrastructure.Persistence;
using Custode.Server.Features.SecretManagement.Common;

namespace Custode.Server.Features.SecretManagement.Secrets;

internal sealed class DeleteSecretHandler(AppDbContext dbContext, FolderGuard folderGuard, IAuditTrail auditTrail)
{
    public async Task HandleAsync(Guid secretId, CancellationToken cancellationToken)
    {
        var secret = await SecretLookup.FindAsync(dbContext, secretId, cancellationToken);

        await folderGuard.AuthorizeAsync(
            secret.FolderId,
            FolderPermissions.Delete,
            AuditActions.SecretDeleted,
            "secret",
            secretId.ToString("D"),
            "folder",
            secret.FolderId.ToString("D"),
            cancellationToken);

        await AuditedWork.CommitAsync(dbContext, async () =>
        {
            dbContext.Secrets.Remove(secret);
            await auditTrail.RecordAsync(
                new AuditRecord(folderGuard.UserId, AuditActions.SecretDeleted, "secret", secretId.ToString("D"), "folder", secret.FolderId.ToString("D")),
                cancellationToken);
            return true;
        }, cancellationToken);
    }
}
