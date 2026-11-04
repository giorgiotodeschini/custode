// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Custode.Infrastructure.Access;
using Custode.Infrastructure.Vault;
using Custode.Server.Tests.Hosting;
using Microsoft.EntityFrameworkCore;

namespace Custode.Server.Tests.Access;

public sealed class FolderAccessTests : IAsyncLifetime
{
    private readonly SqliteVaultDatabase _database = new();
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _user = Guid.NewGuid();
    private readonly Guid _group = Guid.NewGuid();
    private Guid _root;
    private Guid _production;
    private Guid _aws;

    public async Task InitializeAsync()
    {
        await _database.InitializeAsync();

        await using var db = _database.CreateContext(_tenant);
        _root = Guid.NewGuid();
        _production = Guid.NewGuid();
        _aws = Guid.NewGuid();

        db.Folders.Add(NewFolder(_root, "/", null));
        db.Folders.Add(NewFolder(_production, "/production", _root));
        db.Folders.Add(NewFolder(_aws, "/production/aws", _production));
        db.Groups.Add(new Group { Id = _group, TenantId = _tenant, Name = "operators" });
        db.GroupMemberships.Add(new GroupMembership { TenantId = _tenant, GroupId = _group, UserId = _user });
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task InheritedGrant_AppliesToDescendantFolders()
    {
        await Grant(_root, PrincipalKind.User, _user, FolderPermissions.Read);

        Assert.Equal(FolderPermissions.Read, await PermissionsOn(_aws));
    }

    [Fact]
    public async Task ChildGrant_AddsToPermissionsInheritedFromParent()
    {
        await Grant(_root, PrincipalKind.Group, _group, FolderPermissions.Read);
        await Grant(_production, PrincipalKind.Group, _group, FolderPermissions.Write);

        Assert.Equal(FolderPermissions.Read | FolderPermissions.Write, await PermissionsOn(_aws));
    }

    [Fact]
    public async Task BrokenInheritance_StopsParentGrantsForTheSubtree()
    {
        await Grant(_root, PrincipalKind.User, _user, FolderPermissions.Read);
        await SetInheritance(_production, inherits: false);

        Assert.Equal(FolderPermissions.None, await PermissionsOn(_production));
        Assert.Equal(FolderPermissions.None, await PermissionsOn(_aws));
    }

    [Fact]
    public async Task BrokenInheritance_KeepsTheFoldersOwnGrants()
    {
        await Grant(_root, PrincipalKind.User, _user, FolderPermissions.Read);
        await SetInheritance(_production, inherits: false);
        await Grant(_production, PrincipalKind.User, _user, FolderPermissions.Create);

        Assert.Equal(FolderPermissions.Create, await PermissionsOn(_production));
        Assert.Equal(FolderPermissions.Create, await PermissionsOn(_aws));
    }

    [Fact]
    public async Task DirectAndGroupGrants_AreCombined()
    {
        await Grant(_production, PrincipalKind.User, _user, FolderPermissions.Read);
        await Grant(_production, PrincipalKind.Group, _group, FolderPermissions.Create);

        Assert.Equal(FolderPermissions.Read | FolderPermissions.Create, await PermissionsOn(_aws));
    }

    [Fact]
    public async Task Grants_AreNeverDeniedByOtherGrants()
    {
        await Grant(_root, PrincipalKind.Group, _group, FolderPermissions.Read);
        await Grant(_aws, PrincipalKind.User, _user, FolderPermissions.Write);

        Assert.Equal(FolderPermissions.Read | FolderPermissions.Write, await PermissionsOn(_aws));
    }

    [Fact]
    public async Task UnknownFolder_HasNoPermissions()
    {
        await using var db = _database.CreateContext(_tenant);
        var access = new FolderAccess(db);

        var permissions = await access.GetEffectivePermissionsAsync(Guid.NewGuid(), _user, CancellationToken.None);

        Assert.Equal(FolderPermissions.None, permissions);
    }

    private Folder NewFolder(Guid id, string path, Guid? parent) =>
        new() { Id = id, TenantId = _tenant, Path = path, ParentId = parent, CreatedAtUtc = DateTimeOffset.UtcNow };

    private async Task Grant(Guid folderId, PrincipalKind kind, Guid principalId, FolderPermissions permissions)
    {
        await using var db = _database.CreateContext(_tenant);
        db.FolderGrants.Add(new FolderGrant
        {
            Id = Guid.NewGuid(),
            TenantId = _tenant,
            FolderId = folderId,
            PrincipalKind = kind,
            PrincipalId = principalId,
            Permissions = permissions,
        });
        await db.SaveChangesAsync();
    }

    private async Task SetInheritance(Guid folderId, bool inherits)
    {
        await using var db = _database.CreateContext(_tenant);
        var folder = await db.Folders.SingleAsync(entry => entry.Id == folderId);
        folder.InheritsPermissions = inherits;
        await db.SaveChangesAsync();
    }

    private async Task<FolderPermissions> PermissionsOn(Guid folderId)
    {
        await using var db = _database.CreateContext(_tenant);

        return await new FolderAccess(db).GetEffectivePermissionsAsync(folderId, _user, CancellationToken.None);
    }
}
