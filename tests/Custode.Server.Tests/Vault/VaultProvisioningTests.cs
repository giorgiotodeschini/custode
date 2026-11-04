// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using System.Net;
using Custode.Infrastructure.Access;
using Custode.Infrastructure.Audit;
using Custode.Infrastructure.Identity;
using Custode.Infrastructure.Vault;
using Custode.Server.Features.SecurityAudit;
using Custode.Server.Tests.Hosting;
using Custode.Server.Tests.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Custode.Server.Tests.Vault;

public sealed class VaultProvisioningTests : IAsyncLifetime
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
    public async Task Operator_InitializesVault_WithRootAndFullBootstrapGrant()
    {
        var tenant = Guid.NewGuid();
        var administrator = await CreateUserAsync("admin-new", tenant, VaultWorld.AdministratorRole);

        var response = await _world.Operator.SendAsync(HttpMethod.Post, $"/api/operator/tenants/{tenant}/vault", new { administratorUserId = administrator.Id });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var db = _world.Host.CreateTenantContext(tenant);
        var root = await db.Folders.SingleAsync();
        Assert.Equal("/", root.Path);
        Assert.Null(root.ParentId);
        Assert.True(root.InheritsPermissions);

        var grant = await db.FolderGrants.SingleAsync();
        Assert.Equal(root.Id, grant.FolderId);
        Assert.Equal(PrincipalKind.User, grant.PrincipalKind);
        Assert.Equal(administrator.Id, grant.PrincipalId);
        Assert.Equal(FolderPermissions.Read | FolderPermissions.Write | FolderPermissions.Create | FolderPermissions.Delete, grant.Permissions);
    }

    [Fact]
    public async Task Initialization_IsAuditedInTheTargetTenant_WithTheOperatorAsActor()
    {
        var tenant = Guid.NewGuid();
        var administrator = await CreateUserAsync("admin-audited", tenant, VaultWorld.AdministratorRole);
        await _world.Operator.SendAsync(HttpMethod.Post, $"/api/operator/tenants/{tenant}/vault", new { administratorUserId = administrator.Id });

        var entries = await _world.As(administrator, tenant, VaultWorld.AdministratorRole)
            .GetAsync<List<AuditEntryResponse>>("/api/audit");
        var root = await VaultSeeder.FolderIdAsync(_world.Host, tenant, "/");

        var initialized = Assert.Single(entries, entry => entry.Action == AuditActions.VaultInitialized);
        Assert.Equal("succeeded", initialized.Outcome);
        Assert.Equal(_world.OperatorUser.Id, initialized.ActorUserId);
        Assert.Equal(root.ToString("D"), initialized.TargetId);
        Assert.Equal(administrator.Id.ToString("D"), initialized.SecondaryTargetId);

        var grant = Assert.Single(entries, entry => entry.Action == AuditActions.GrantSet);
        Assert.Equal(_world.OperatorUser.Id, grant.ActorUserId);
        Assert.Equal(root.ToString("D"), grant.TargetId);
        Assert.Equal(administrator.Id.ToString("D"), grant.SecondaryTargetId);

        Assert.True((await _world.As(administrator, tenant, VaultWorld.AdministratorRole)
            .GetAsync<AuditVerificationResponse>("/api/audit/verify")).IsIntact);
    }

    [Fact]
    public async Task NonOperator_CannotInitializeAnyVault()
    {
        var tenant = Guid.NewGuid();
        var administrator = await CreateUserAsync("admin-blocked", tenant, VaultWorld.AdministratorRole);

        var fromTenantAdministrator = await _world.Administrator.SendAsync(HttpMethod.Post, $"/api/operator/tenants/{tenant}/vault", new { administratorUserId = administrator.Id });
        var fromMember = await _world.Member.SendAsync(HttpMethod.Post, $"/api/operator/tenants/{tenant}/vault", new { administratorUserId = administrator.Id });

        Assert.Equal(HttpStatusCode.Forbidden, fromTenantAdministrator.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, fromMember.StatusCode);

        await using var db = _world.Host.CreateTenantContext(tenant);
        Assert.False(await db.Folders.AnyAsync());
    }

    [Fact]
    public async Task Initialization_TwiceForTheSameTenant_IsConflict()
    {
        var first = await _world.Operator.SendAsync(HttpMethod.Post, $"/api/operator/tenants/{_world.TenantA}/vault", new { administratorUserId = _world.AdministratorA.Id });

        Assert.Equal(HttpStatusCode.Conflict, first.StatusCode);
    }

    [Fact]
    public async Task AdministratorOutsideTheTenant_IsRejected_AndNothingIsCreated()
    {
        var tenant = Guid.NewGuid();

        var response = await _world.Operator.SendAsync(HttpMethod.Post, $"/api/operator/tenants/{tenant}/vault", new { administratorUserId = _world.UserB.Id });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        await using var db = _world.Host.CreateTenantContext(tenant);
        Assert.False(await db.Folders.AnyAsync());
        Assert.False(await db.AuditEntries.AnyAsync());
    }

    private async Task<ApplicationUser> CreateUserAsync(string login, Guid tenant, params string[] roles)
    {
        using var scope = _world.Host.Services.CreateScope();

        return await IdentityTestServices.CreateUserAsync(scope.ServiceProvider, login, tenant, roles);
    }
}
