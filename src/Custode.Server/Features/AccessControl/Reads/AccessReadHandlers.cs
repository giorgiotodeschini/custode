// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Custode.Infrastructure.Access;
using Custode.Infrastructure.Identity;
using Custode.Infrastructure.Persistence;
using Custode.Infrastructure.Tenancy;
using Custode.Infrastructure.Vault;
using Custode.Server.Features.AccessControl.Grants;
using Microsoft.EntityFrameworkCore;

namespace Custode.Server.Features.AccessControl.Reads;

public sealed record GroupSummaryResponse(Guid Id, string Name, int MemberCount, int GrantCount);

public sealed record GroupMemberResponse(Guid Id, string Login);

public sealed record GrantSourceResponse(string FolderPath, IReadOnlyList<string> Permissions);

public sealed record PrincipalGrantResponse(
    string PrincipalKind,
    Guid PrincipalId,
    string PrincipalName,
    IReadOnlyList<GrantSourceResponse> Sources,
    IReadOnlyList<string> EffectivePermissions);

public sealed record FolderInheritanceResponse(string Path, string? ParentPath, bool InheritsPermissions);

public sealed record CallerPermissionsResponse(string FolderPath, IReadOnlyList<string> Permissions);

internal static class PermissionNames
{
    public static IReadOnlyList<string> Describe(FolderPermissions permissions)
    {
        var names = new List<string>();

        foreach (var (flag, name) in new[]
                 {
                     (FolderPermissions.Read, "read"),
                     (FolderPermissions.Write, "write"),
                     (FolderPermissions.Create, "create"),
                     (FolderPermissions.Delete, "delete"),
                 })
        {
            if (permissions.HasFlag(flag))
            {
                names.Add(name);
            }
        }

        return names;
    }
}

internal sealed class ListGroupsReadHandler(AppDbContext dbContext, ITenantContext tenantContext)
{
    public async Task<IReadOnlyList<GroupSummaryResponse>> HandleAsync(CancellationToken cancellationToken)
    {
        var groups = await dbContext.Groups.AsNoTracking().OrderBy(group => group.Name).ToListAsync(cancellationToken);
        var memberCounts = await dbContext.GroupMemberships.AsNoTracking()
            .GroupBy(membership => membership.GroupId)
            .Select(group => new { GroupId = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);
        var grantCounts = await dbContext.FolderGrants.AsNoTracking()
            .Where(grant => grant.PrincipalKind == PrincipalKind.Group)
            .GroupBy(grant => grant.PrincipalId)
            .Select(group => new { GroupId = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);

        return groups.Select(group => new GroupSummaryResponse(
            group.Id,
            group.Name,
            memberCounts.FirstOrDefault(entry => entry.GroupId == group.Id)?.Count ?? 0,
            grantCounts.FirstOrDefault(entry => entry.GroupId == group.Id)?.Count ?? 0)).ToList();
    }
}

internal sealed class ListGroupMembersReadHandler(AppDbContext dbContext)
{
    public async Task<IReadOnlyList<GroupMemberResponse>> HandleAsync(Guid groupId, CancellationToken cancellationToken)
    {
        // The group filter applies the tenant scope, so a group of another tenant is reported as not found.
        _ = await dbContext.Groups.AsNoTracking().FirstOrDefaultAsync(group => group.Id == groupId, cancellationToken)
            ?? throw new VaultNotFoundException("The group does not exist in this tenant.");

        return await (
            from membership in dbContext.GroupMemberships.AsNoTracking()
            join user in dbContext.Users.AsNoTracking() on membership.UserId equals user.Id
            where membership.GroupId == groupId && user.TenantId == membership.TenantId
            orderby user.UserName
            select new GroupMemberResponse(user.Id, user.UserName!)).ToListAsync(cancellationToken);
    }
}

internal sealed class GrantsReadHandler(AppDbContext dbContext)
{
    public async Task<IReadOnlyList<PrincipalGrantResponse>> HandleAsync(string? rawFolderPath, CancellationToken cancellationToken)
    {
        var folderPath = FolderPath.Normalize(rawFolderPath);

        var folder = await dbContext.Folders.AsNoTracking().FirstOrDefaultAsync(entity => entity.Path == folderPath, cancellationToken)
            ?? throw new VaultNotFoundException("The requested folder or item does not exist.");

        // Walk the folder and its ancestors, stopping above a folder whose inheritance is broken.
        var ancestorPaths = FolderPath.AncestorsAndSelf(folderPath);
        var folders = await dbContext.Folders.AsNoTracking()
            .Where(entity => ancestorPaths.Contains(entity.Path))
            .ToDictionaryAsync(entity => entity.Id, cancellationToken);

        var applicable = new List<(string Path, Guid FolderId)>();
        var current = folder;

        while (true)
        {
            applicable.Add((current.Path, current.Id));

            if (!current.InheritsPermissions || current.ParentId is not { } parentId || !folders.TryGetValue(parentId, out var parent))
            {
                break;
            }

            current = parent;
        }

        var folderIds = applicable.Select(entry => entry.FolderId).ToList();
        var grants = await dbContext.FolderGrants.AsNoTracking()
            .Where(grant => folderIds.Contains(grant.FolderId))
            .ToListAsync(cancellationToken);

        var groupNames = await dbContext.Groups.AsNoTracking().ToDictionaryAsync(group => group.Id, group => group.Name, cancellationToken);
        var userNames = await dbContext.Users.AsNoTracking()
            .Where(user => grants.Select(grant => grant.PrincipalId).Contains(user.Id))
            .ToDictionaryAsync(user => user.Id, user => user.UserName!, cancellationToken);

        var principals = grants
            .GroupBy(grant => (grant.PrincipalKind, grant.PrincipalId))
            .Select(group =>
            {
                var sources = applicable
                    .Select(entry => (entry.Path, Grant: group.FirstOrDefault(grant => grant.FolderId == entry.FolderId)))
                    .Where(entry => entry.Grant is not null)
                    .Select(entry => new GrantSourceResponse(entry.Path, PermissionNames.Describe(entry.Grant!.Permissions)))
                    .ToList();

                var effective = group.Where(grant => folderIds.Contains(grant.FolderId))
                    .Aggregate(FolderPermissions.None, (acc, grant) => acc | grant.Permissions);

                var kind = group.Key.PrincipalKind == PrincipalKind.Group ? "group" : "user";
                var name = group.Key.PrincipalKind == PrincipalKind.Group
                    ? groupNames.GetValueOrDefault(group.Key.PrincipalId, string.Empty)
                    : userNames.GetValueOrDefault(group.Key.PrincipalId, string.Empty);

                return new PrincipalGrantResponse(kind, group.Key.PrincipalId, name, sources, PermissionNames.Describe(effective));
            })
            .OrderBy(principal => principal.PrincipalKind)
            .ThenBy(principal => principal.PrincipalName)
            .ToList();

        return principals;
    }
}

internal sealed class FolderInheritanceReadHandler(AppDbContext dbContext)
{
    public async Task<IReadOnlyList<FolderInheritanceResponse>> HandleAsync(CancellationToken cancellationToken)
    {
        var folders = await dbContext.Folders.AsNoTracking().OrderBy(folder => folder.Path).ToListAsync(cancellationToken);
        var paths = folders.ToDictionary(folder => folder.Id, folder => folder.Path);

        return folders.Select(folder => new FolderInheritanceResponse(
            folder.Path,
            folder.ParentId is { } parentId ? paths.GetValueOrDefault(parentId) : null,
            folder.InheritsPermissions)).ToList();
    }
}

internal sealed class CallerPermissionsReadHandler(AppDbContext dbContext, IFolderAccess folderAccess, ICurrentUser currentUser)
{
    public async Task<CallerPermissionsResponse> HandleAsync(string? rawFolderPath, CancellationToken cancellationToken)
    {
        var folderPath = FolderPath.Normalize(rawFolderPath);

        var folder = await dbContext.Folders.AsNoTracking().FirstOrDefaultAsync(entity => entity.Path == folderPath, cancellationToken)
            ?? throw new VaultNotFoundException("The requested folder or item does not exist.");

        var effective = await folderAccess.GetEffectivePermissionsAsync(folder.Id, currentUser.Id, cancellationToken);

        // Without any effective permission the folder looks the same as one that does not exist.
        if (effective == FolderPermissions.None)
        {
            throw new VaultNotFoundException("The requested folder or item does not exist.");
        }

        return new CallerPermissionsResponse(folderPath, PermissionNames.Describe(effective));
    }
}
