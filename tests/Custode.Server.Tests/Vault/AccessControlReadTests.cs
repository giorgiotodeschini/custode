// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using System.Net;
using System.Net.Http.Json;
using Custode.Infrastructure.Audit;
using Custode.Server.Tests.Hosting;

namespace Custode.Server.Tests.Vault;

public sealed class AccessControlReadTests : IAsyncLifetime
{
    private VaultWorld _world = null!;

    public async Task InitializeAsync()
    {
        _world = await VaultWorld.CreateAsync(await CustodeTestHost.StartAsync());
        await VaultHttpSteps.CreateFolderAsync(_world.Administrator, "/production");
        await VaultHttpSteps.CreateFolderAsync(_world.Administrator, "/production/aws");
    }

    public async Task DisposeAsync()
    {
        await _world.DisposeAsync();
    }

    [Fact]
    public async Task Groups_ListWithMemberAndGrantCounts_AndTheirMembers()
    {
        var group = await VaultHttpSteps.CreateGroupAsync(_world.Administrator, "platform");
        await VaultHttpSteps.AddMemberAsync(_world.Administrator, group, _world.MemberA.Id);
        await VaultHttpSteps.SetGrantAsync(_world.Administrator, "/production", "group", group, "read");

        var groups = await _world.Administrator.GetAsync<List<GroupBody>>("/api/access/groups");
        var members = await _world.Administrator.GetAsync<List<MemberBody>>($"/api/access/groups/{group}/members");

        var platform = Assert.Single(groups, entry => entry.Name == "platform");
        Assert.Equal(1, platform.MemberCount);
        Assert.Equal(1, platform.GrantCount);
        Assert.Equal(_world.MemberA.UserName, Assert.Single(members).Login);
    }

    [Fact]
    public async Task GroupReads_AreForbiddenForANonAdministrator_AndNotAudited()
    {
        var response = await _world.Member.SendAsync(HttpMethod.Get, "/api/access/groups");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GroupOfAnotherTenant_IsNotFound()
    {
        var otherGroup = await VaultHttpSteps.CreateGroupAsync(_world.As(_world.UserB, _world.TenantB, VaultWorld.AdministratorRole), "other");

        var response = await _world.Administrator.SendAsync(HttpMethod.Get, $"/api/access/groups/{otherGroup}/members");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Grants_ListSourcesAndEffectivePermissions_FromTheAncestorsThatApply()
    {
        var group = await VaultHttpSteps.CreateGroupAsync(_world.Administrator, "auditors");
        await VaultHttpSteps.SetGrantAsync(_world.Administrator, "/production", "group", group, "read");
        await VaultHttpSteps.SetGrantAsync(_world.Administrator, "/production/aws", "group", group, "write");

        var grants = await _world.Administrator.GetAsync<List<PrincipalBody>>("/api/access/grants?folderPath=/production/aws");

        var principal = Assert.Single(grants, entry => entry.PrincipalName == "auditors");
        Assert.Equal(2, principal.Sources.Count);
        Assert.Contains(principal.Sources, source => source.FolderPath == "/production" && source.Permissions.Contains("read"));
        Assert.Contains(principal.Sources, source => source.FolderPath == "/production/aws" && source.Permissions.Contains("write"));
        Assert.Equal(["read", "write"], principal.EffectivePermissions);
    }

    [Fact]
    public async Task Grants_StopAtAFolderWithBrokenInheritance()
    {
        var group = await VaultHttpSteps.CreateGroupAsync(_world.Administrator, "viewers");
        await VaultHttpSteps.SetGrantAsync(_world.Administrator, "/production", "group", group, "read");
        await _world.Administrator.SendAsync(HttpMethod.Post, "/api/access/inheritance/break?folderPath=/production/aws");

        var grants = await _world.Administrator.GetAsync<List<PrincipalBody>>("/api/access/grants?folderPath=/production/aws");

        Assert.DoesNotContain(grants, entry => entry.PrincipalName == "viewers");
    }

    [Fact]
    public async Task Grants_ForAnUnknownFolder_IsNotFound()
    {
        var response = await _world.Administrator.SendAsync(HttpMethod.Get, "/api/access/grants?folderPath=/nowhere");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Folders_ReportInheritanceStatus()
    {
        await _world.Administrator.SendAsync(HttpMethod.Post, "/api/access/inheritance/break?folderPath=/production/aws");

        var folders = await _world.Administrator.GetAsync<List<FolderBody>>("/api/access/folders");

        Assert.False(Assert.Single(folders, folder => folder.Path == "/production/aws").InheritsPermissions);
        Assert.True(Assert.Single(folders, folder => folder.Path == "/production").InheritsPermissions);
        Assert.Equal("/production", Assert.Single(folders, folder => folder.Path == "/production/aws").ParentPath);
    }

    [Fact]
    public async Task CallerPermissions_ListTheCallersOwnLevels_OnlyForTheCaller()
    {
        var group = await VaultHttpSteps.CreateGroupAsync(_world.Administrator, "creators");
        await VaultHttpSteps.AddMemberAsync(_world.Administrator, group, _world.MemberA.Id);
        await VaultHttpSteps.SetGrantAsync(_world.Administrator, "/production", "group", group, "read", "create");

        var mine = await _world.Member.GetAsync<PermissionsBody>("/api/access/me/permissions?folderPath=/production");

        Assert.Equal(["read", "create"], mine.Permissions);
        Assert.DoesNotContain("delete", mine.Permissions);
    }

    [Fact]
    public async Task CallerPermissions_WithoutAnyPermission_IsNotFound_LikeAMissingFolder()
    {
        var inaccessible = await _world.Outsider.SendAsync(HttpMethod.Get, "/api/access/me/permissions?folderPath=/production");
        var missing = await _world.Outsider.SendAsync(HttpMethod.Get, "/api/access/me/permissions?folderPath=/nowhere");

        Assert.Equal(HttpStatusCode.NotFound, inaccessible.StatusCode);
        Assert.Equal(missing.StatusCode, inaccessible.StatusCode);
    }

    private sealed record GroupBody(Guid Id, string Name, int MemberCount, int GrantCount);

    private sealed record MemberBody(Guid Id, string Login);

    private sealed record SourceBody(string FolderPath, List<string> Permissions);

    private sealed record PrincipalBody(string PrincipalKind, Guid PrincipalId, string PrincipalName, List<SourceBody> Sources, List<string> EffectivePermissions);

    private sealed record FolderBody(string Path, string? ParentPath, bool InheritsPermissions);

    private sealed record PermissionsBody(string FolderPath, List<string> Permissions);
}
