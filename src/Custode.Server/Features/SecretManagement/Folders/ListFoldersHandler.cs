// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Custode.Infrastructure.Access;
using Custode.Infrastructure.Persistence;
using Custode.Infrastructure.Vault;
using Custode.Server.Features.SecretManagement.Common;
using Microsoft.EntityFrameworkCore;

namespace Custode.Server.Features.SecretManagement.Folders;

internal sealed class ListFoldersHandler(AppDbContext dbContext, FolderGuard folderGuard, IFolderAccess folderAccess)
{
    public async Task<IReadOnlyList<FolderResponse>> HandleAsync(string? path, CancellationToken cancellationToken)
    {
        var folder = await folderGuard.GetFolderAsync(FolderPath.Normalize(path), cancellationToken);

        await folderGuard.AuthorizeAsync(folder.Id, FolderPermissions.Read, cancellationToken: cancellationToken);

        var children = await dbContext.Folders
            .AsNoTracking()
            .Where(child => child.ParentId == folder.Id)
            .OrderBy(child => child.Path)
            .ToListAsync(cancellationToken);

        var visible = new List<FolderResponse>();

        foreach (var child in children)
        {
            var permissions = await folderAccess.GetEffectivePermissionsAsync(child.Id, folderGuard.UserId, cancellationToken);

            if (permissions != FolderPermissions.None)
            {
                visible.Add(new FolderResponse(child.Path, child.InheritsPermissions));
            }
        }

        return visible;
    }
}
