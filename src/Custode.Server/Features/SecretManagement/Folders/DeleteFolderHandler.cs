// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Custode.Infrastructure.Access;
using Custode.Infrastructure.Audit;
using Custode.Infrastructure.Persistence;
using Custode.Infrastructure.Vault;
using Custode.Server.Features.SecretManagement.Common;
using Microsoft.EntityFrameworkCore;

namespace Custode.Server.Features.SecretManagement.Folders;

internal sealed class DeleteFolderHandler(AppDbContext dbContext, FolderGuard folderGuard, IAuditTrail auditTrail)
{
    public async Task HandleAsync(string? path, CancellationToken cancellationToken)
    {
        var normalizedPath = FolderPath.Normalize(path);

        if (normalizedPath == FolderPath.Root)
        {
            throw new InvalidVaultRequestException("The root folder cannot be deleted.");
        }

        var folder = await folderGuard.GetFolderAsync(normalizedPath, cancellationToken);

        await folderGuard.AuthorizeAsync(
            folder.Id,
            FolderPermissions.Delete,
            AuditActions.FolderDeleted,
            "folder",
            folder.Id.ToString("D"),
            cancellationToken: cancellationToken);

        var hasChildren = await dbContext.Folders.AnyAsync(child => child.ParentId == folder.Id, cancellationToken)
            || await dbContext.Secrets.AnyAsync(secret => secret.FolderId == folder.Id, cancellationToken);

        if (hasChildren)
        {
            throw new VaultConflictException("Only empty folders can be deleted.");
        }

        var grants = await dbContext.FolderGrants
            .Where(grant => grant.FolderId == folder.Id)
            .ToListAsync(cancellationToken);

        await AuditedWork.CommitAsync(dbContext, async () =>
        {
            dbContext.FolderGrants.RemoveRange(grants);
            dbContext.Folders.Remove(folder);
            await auditTrail.RecordAsync(
                new AuditRecord(folderGuard.UserId, AuditActions.FolderDeleted, "folder", folder.Id.ToString("D")),
                cancellationToken);
            return true;
        }, cancellationToken);
    }
}
