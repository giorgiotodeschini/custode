// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Custode.Infrastructure.Persistence;
using Custode.Infrastructure.Vault;
using Microsoft.EntityFrameworkCore;

namespace Custode.Infrastructure.Access;

public sealed class FolderAccess(AppDbContext dbContext) : IFolderAccess
{
    public async Task<FolderPermissions> GetEffectivePermissionsAsync(Guid folderId, Guid userId, CancellationToken cancellationToken)
    {
        var target = await dbContext.Folders
            .AsNoTracking()
            .FirstOrDefaultAsync(folder => folder.Id == folderId, cancellationToken);

        if (target is null)
        {
            return FolderPermissions.None;
        }

        var ancestorPaths = FolderPath.AncestorsAndSelf(target.Path);
        var folders = await dbContext.Folders
            .AsNoTracking()
            .Where(folder => ancestorPaths.Contains(folder.Path))
            .ToDictionaryAsync(folder => folder.Id, cancellationToken);

        var groupIds = await dbContext.GroupMemberships
            .AsNoTracking()
            .Where(membership => membership.UserId == userId)
            .Select(membership => membership.GroupId)
            .ToListAsync(cancellationToken);

        var folderIds = folders.Keys.ToList();
        var grants = await dbContext.FolderGrants
            .AsNoTracking()
            .Where(grant => folderIds.Contains(grant.FolderId)
                && ((grant.PrincipalKind == PrincipalKind.User && grant.PrincipalId == userId)
                    || (grant.PrincipalKind == PrincipalKind.Group && groupIds.Contains(grant.PrincipalId))))
            .ToListAsync(cancellationToken);

        var effective = FolderPermissions.None;
        var current = folders[target.Id];

        while (true)
        {
            foreach (var grant in grants.Where(grant => grant.FolderId == current.Id))
            {
                effective |= grant.Permissions;
            }

            if (!current.InheritsPermissions || current.ParentId is not { } parentId || !folders.TryGetValue(parentId, out var parent))
            {
                return effective;
            }

            current = parent;
        }
    }

    public async Task<IReadOnlyDictionary<Guid, FolderPermissions>> GetEffectivePermissionsForAllFoldersAsync(Guid userId, CancellationToken cancellationToken)
    {
        var folders = await dbContext.Folders.AsNoTracking().ToListAsync(cancellationToken);
        var foldersById = folders.ToDictionary(folder => folder.Id);

        var groupIds = await dbContext.GroupMemberships
            .AsNoTracking()
            .Where(membership => membership.UserId == userId)
            .Select(membership => membership.GroupId)
            .ToListAsync(cancellationToken);

        var grants = await dbContext.FolderGrants
            .AsNoTracking()
            .Where(grant => (grant.PrincipalKind == PrincipalKind.User && grant.PrincipalId == userId)
                || (grant.PrincipalKind == PrincipalKind.Group && groupIds.Contains(grant.PrincipalId)))
            .ToListAsync(cancellationToken);

        var grantsByFolder = grants
            .GroupBy(grant => grant.FolderId)
            .ToDictionary(group => group.Key, group => group.Aggregate(FolderPermissions.None, (acc, grant) => acc | grant.Permissions));

        var result = new Dictionary<Guid, FolderPermissions>();

        foreach (var folder in folders)
        {
            var effective = FolderPermissions.None;
            var current = folder;

            while (true)
            {
                if (grantsByFolder.TryGetValue(current.Id, out var own))
                {
                    effective |= own;
                }

                if (!current.InheritsPermissions || current.ParentId is not { } parentId || !foldersById.TryGetValue(parentId, out var parent))
                {
                    break;
                }

                current = parent;
            }

            result[folder.Id] = effective;
        }

        return result;
    }
}
