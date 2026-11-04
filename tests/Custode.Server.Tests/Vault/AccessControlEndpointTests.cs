// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using System.Net;
using Custode.Infrastructure.Audit;
using Custode.Infrastructure.Vault;
using Custode.Server.Tests.Hosting;
using Custode.Server.Features.SecurityAudit;
using Microsoft.EntityFrameworkCore;

namespace Custode.Server.Tests.Vault;

public sealed class AccessControlEndpointTests : IAsyncLifetime
{
    private VaultWorld _world = null!;

    public async Task InitializeAsync()
    {
        _world = await VaultWorld.CreateAsync(await CustodeTestHost.StartAsync());
    }

    public async Task DisposeAsync()
    {
        await _world.DisposeAsync();
    }

    [Fact]
    public async Task NonAdministrator_CannotCreateGroups()
    {
        var response = await _world.Member.SendAsync(HttpMethod.Post, "/api/access/groups", new { name = "ops" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Administrator_CreatesGroup_AndRejectsDuplicateName()
    {
        var created = await _world.Administrator.SendAsync(HttpMethod.Post, "/api/access/groups", new { name = "ops" });
        var duplicate = await _world.Administrator.SendAsync(HttpMethod.Post, "/api/access/groups", new { name = "ops" });

        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    [Fact]
    public async Task AddingUserOfAnotherTenant_IsRejected_AndNoMembershipIsCreated()
    {
        var groupId = await VaultHttpSteps.CreateGroupAsync(_world.Administrator, "ops");

        var response = await _world.Administrator.SendAsync(HttpMethod.Post, $"/api/access/groups/{groupId}/members", new { userId = _world.UserB.Id });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        await using var db = _world.Host.CreateTenantContext(_world.TenantA);
        Assert.False(await db.GroupMemberships.AnyAsync(membership => membership.GroupId == groupId));
    }

    [Fact]
    public async Task MembershipRemoval_TakesEffectOnTheNextRequest()
    {
        await VaultHttpSteps.CreateFolderAsync(_world.Administrator, "/production");
        var groupId = await VaultHttpSteps.CreateGroupAsync(_world.Administrator, "ops");
        await VaultHttpSteps.SetGrantAsync(_world.Administrator, "/production", "group", groupId, "read");
        await VaultHttpSteps.AddMemberAsync(_world.Administrator, groupId, _world.MemberA.Id);

        var beforeRemoval = await _world.Member.SendAsync(HttpMethod.Get, "/api/secrets?folderPath=/production");
        var removal = await _world.Administrator.SendAsync(HttpMethod.Delete, $"/api/access/groups/{groupId}/members/{_world.MemberA.Id}");
        var afterRemoval = await _world.Member.SendAsync(HttpMethod.Get, "/api/secrets?folderPath=/production");

        Assert.Equal(HttpStatusCode.OK, beforeRemoval.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, removal.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, afterRemoval.StatusCode);
    }

    [Fact]
    public async Task GroupGrant_AppliesToSubfolders()
    {
        await VaultHttpSteps.CreateFolderAsync(_world.Administrator, "/production");
        await VaultHttpSteps.CreateFolderAsync(_world.Administrator, "/production/aws");
        var groupId = await VaultHttpSteps.CreateGroupAsync(_world.Administrator, "ops");
        await VaultHttpSteps.SetGrantAsync(_world.Administrator, "/production", "group", groupId, "read");
        await VaultHttpSteps.AddMemberAsync(_world.Administrator, groupId, _world.MemberA.Id);

        var response = await _world.Member.SendAsync(HttpMethod.Get, "/api/secrets?folderPath=/production/aws");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GrantToPrincipalOfAnotherTenant_IsRejected()
    {
        await VaultHttpSteps.CreateFolderAsync(_world.Administrator, "/production");
        var groupOfTenantB = await SeedTenantBGroupAsync();

        var userGrant = await _world.Administrator.SendAsync(HttpMethod.Put, "/api/access/grants",
            new { folderPath = "/production", principalKind = "user", principalId = _world.UserB.Id, permissions = new[] { "read" } });
        var groupGrant = await _world.Administrator.SendAsync(HttpMethod.Put, "/api/access/grants",
            new { folderPath = "/production", principalKind = "group", principalId = groupOfTenantB, permissions = new[] { "read" } });

        Assert.Equal(HttpStatusCode.NotFound, userGrant.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, groupGrant.StatusCode);
    }

    [Fact]
    public async Task BrokenInheritance_RemovesInheritedAccess_AndRestoreBringsItBack()
    {
        await VaultHttpSteps.CreateFolderAsync(_world.Administrator, "/production");
        await VaultHttpSteps.CreateFolderAsync(_world.Administrator, "/production/aws");
        var groupId = await VaultHttpSteps.CreateGroupAsync(_world.Administrator, "ops");
        await VaultHttpSteps.SetGrantAsync(_world.Administrator, "/production", "group", groupId, "read");
        await VaultHttpSteps.AddMemberAsync(_world.Administrator, groupId, _world.MemberA.Id);

        var broken = await _world.Administrator.SendAsync(HttpMethod.Post, "/api/access/inheritance/break?folderPath=/production/aws");
        var whileBroken = await _world.Member.SendAsync(HttpMethod.Get, "/api/secrets?folderPath=/production/aws");
        var restored = await _world.Administrator.SendAsync(HttpMethod.Post, "/api/access/inheritance/restore?folderPath=/production/aws");
        var afterRestore = await _world.Member.SendAsync(HttpMethod.Get, "/api/secrets?folderPath=/production/aws");

        Assert.Equal(HttpStatusCode.NoContent, broken.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, whileBroken.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, restored.StatusCode);
        Assert.Equal(HttpStatusCode.OK, afterRestore.StatusCode);
    }

    [Fact]
    public async Task EveryAccessControlChange_IsRecordedWithItsTargets()
    {
        await VaultHttpSteps.CreateFolderAsync(_world.Administrator, "/production");
        var folderId = await VaultSeeder.FolderIdAsync(_world.Host, _world.TenantA, "/production");
        var groupId = await VaultHttpSteps.CreateGroupAsync(_world.Administrator, "ops");
        await VaultHttpSteps.AddMemberAsync(_world.Administrator, groupId, _world.MemberA.Id);
        await VaultHttpSteps.SetGrantAsync(_world.Administrator, "/production", "group", groupId, "read");
        await _world.Administrator.SendAsync(HttpMethod.Post, "/api/access/inheritance/break?folderPath=/production");
        await _world.Administrator.SendAsync(HttpMethod.Post, "/api/access/inheritance/restore?folderPath=/production");
        await _world.Administrator.SendAsync(HttpMethod.Delete, $"/api/access/groups/{groupId}/members/{_world.MemberA.Id}");

        var entries = await VaultHttpSteps.AuditAsync(_world.Administrator);

        Assert.Contains(entries, entry => entry.Action == AuditActions.GroupCreated && entry.TargetId == groupId.ToString("D"));
        Assert.Contains(entries, entry => entry.Action == AuditActions.MembershipAdded
            && entry.TargetId == groupId.ToString("D") && entry.SecondaryTargetId == _world.MemberA.Id.ToString("D"));
        Assert.Contains(entries, entry => entry.Action == AuditActions.GrantSet
            && entry.TargetId == folderId.ToString("D") && entry.SecondaryTargetId == groupId.ToString("D"));
        Assert.Contains(entries, entry => entry.Action == AuditActions.InheritanceBroken && entry.TargetId == folderId.ToString("D"));
        Assert.Contains(entries, entry => entry.Action == AuditActions.InheritanceRestored && entry.TargetId == folderId.ToString("D"));
        Assert.Contains(entries, entry => entry.Action == AuditActions.MembershipRemoved && entry.TargetId == groupId.ToString("D"));
    }

    [Fact]
    public async Task NonAdministrator_AccessControlChanges_AreForbidden_AndEachDenialIsAudited()
    {
        await VaultHttpSteps.CreateFolderAsync(_world.Administrator, "/production");
        var groupId = await VaultHttpSteps.CreateGroupAsync(_world.Administrator, "ops");

        var createGroup = await _world.Member.SendAsync(HttpMethod.Post, "/api/access/groups", new { name = "rogue" });
        var addMember = await _world.Member.SendAsync(HttpMethod.Post, $"/api/access/groups/{groupId}/members", new { userId = _world.MemberA.Id });
        var grant = await _world.Member.SendAsync(HttpMethod.Put, "/api/access/grants",
            new { folderPath = "/production", principalKind = "group", principalId = groupId, permissions = new[] { "read" } });
        var breakInheritance = await _world.Member.SendAsync(HttpMethod.Post, "/api/access/inheritance/break?folderPath=/production");

        Assert.Equal(HttpStatusCode.Forbidden, createGroup.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, addMember.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, grant.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, breakInheritance.StatusCode);

        var denied = await VaultHttpSteps.AuditAsync(_world.Administrator, "?outcome=denied");
        Assert.Contains(denied, entry => entry.Action == AuditActions.GroupCreated && entry.ActorUserId == _world.MemberA.Id);
        Assert.Contains(denied, entry => entry.Action == AuditActions.MembershipAdded && entry.TargetId == groupId.ToString("D"));
        Assert.Contains(denied, entry => entry.Action == AuditActions.GrantSet && entry.ActorUserId == _world.MemberA.Id);
        Assert.Contains(denied, entry => entry.Action == AuditActions.InheritanceBroken && entry.ActorUserId == _world.MemberA.Id);
        Assert.All(denied, entry => Assert.Equal("denied", entry.Outcome));

        await using var db = _world.Host.CreateTenantContext(_world.TenantA);
        Assert.False(await db.Groups.AnyAsync(group => group.Name == "rogue"));
        Assert.False(await db.GroupMemberships.AnyAsync(membership => membership.UserId == _world.MemberA.Id));
        Assert.False(await db.FolderGrants.AnyAsync(grantEntry => grantEntry.PrincipalId == groupId));
    }

    private async Task<Guid> SeedTenantBGroupAsync()
    {
        var group = new Group { Id = Guid.NewGuid(), TenantId = _world.TenantB, Name = "tenant-b-ops" };

        await using var db = _world.Host.CreateTenantContext(_world.TenantB);
        db.Groups.Add(group);
        await db.SaveChangesAsync();

        return group.Id;
    }
}
