// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Custode.Infrastructure.Access;
using Custode.Infrastructure.Audit;
using Custode.Infrastructure.Persistence;
using Custode.Infrastructure.Vault;
using Custode.Server.Features.SecretManagement.Common;
using Microsoft.EntityFrameworkCore;

namespace Custode.Server.Features.SecretManagement.Folders;

internal sealed class CreateFolderHandler(AppDbContext dbContext, FolderGuard folderGuard, IAuditTrail auditTrail)
{
    public async Task<FolderResponse> HandleAsync(CreateFolderRequest request, CancellationToken cancellationToken)
    {
        var path = FolderPath.Normalize(request.Path);

        if (path == FolderPath.Root)
        {
            throw new InvalidVaultRequestException("The root folder is created with the tenant and cannot be created here.");
        }

        var parent = await folderGuard.GetFolderAsync(FolderPath.ParentOf(path)!, cancellationToken);

        await folderGuard.AuthorizeAsync(
            parent.Id,
            FolderPermissions.Create,
            AuditActions.FolderCreated,
            "folder",
            parent.Id.ToString("D"),
            cancellationToken: cancellationToken);

        if (await dbContext.Folders.AnyAsync(folder => folder.Path == path, cancellationToken))
        {
            throw new VaultConflictException("A folder with this path already exists.");
        }

        var folder = new Folder
        {
            Id = Guid.NewGuid(),
            TenantId = folderGuard.TenantId,
            Path = path,
            ParentId = parent.Id,
            InheritsPermissions = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };

        await AuditedWork.CommitAsync(dbContext, async () =>
        {
            dbContext.Folders.Add(folder);
            await auditTrail.RecordAsync(
                new AuditRecord(folderGuard.UserId, AuditActions.FolderCreated, "folder", folder.Id.ToString("D")),
                cancellationToken);
            return true;
        }, cancellationToken);

        return new FolderResponse(folder.Path, folder.InheritsPermissions);
    }
}
