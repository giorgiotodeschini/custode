// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using System.Net;
using System.Net.Http.Json;
using Custode.Infrastructure.Audit;
using Custode.Server.Tests.Hosting;
using Custode.Server.Tests.Identity;
using Custode.Server.Tests.Sessions;
using Microsoft.EntityFrameworkCore;

namespace Custode.Server.Tests.TenantAdministration;

public sealed class MemberManagementTests : IAsyncLifetime
{
    private CustodeTestHost _host = null!;

    public async Task InitializeAsync()
    {
        _host = await CustodeTestHost.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _host.DisposeAsync();
    }

    [Fact]
    public async Task Members_ListsOnlyTheTenantsMembers_WithStatusAndRoles()
    {
        var tenantId = Guid.NewGuid();
        var (_, adminToken) = await TenantAdministrationTestSupport.SignedInAsync(_host, tenantId, TenantAdministrationTestSupport.AdministratorRole);
        await TenantAdministrationTestSupport.SignedInAsync(_host, Guid.NewGuid());

        var members = await (await TenantAdministrationTestSupport.SendAsync(_host, HttpMethod.Get, "/api/tenant/members", adminToken))
            .Content.ReadFromJsonAsync<List<MemberBody>>();

        Assert.Single(members!);
        Assert.Contains(AdministratorRoleName, members![0].Roles);
        Assert.True(members[0].IsEnabled);
    }

    [Fact]
    public async Task Members_ListingIsForbiddenForANonAdministrator()
    {
        var (_, memberToken) = await TenantAdministrationTestSupport.SignedInAsync(_host, Guid.NewGuid());

        var response = await TenantAdministrationTestSupport.SendAsync(_host, HttpMethod.Get, "/api/tenant/members", memberToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreatedMember_JoinsTheAdministratorsTenant_IsEnabled_FlaggedForPasswordChange_AndAudited()
    {
        var tenantId = Guid.NewGuid();
        var (_, adminToken) = await TenantAdministrationTestSupport.SignedInAsync(_host, tenantId, AdministratorRoleName);
        var login = "created-" + Guid.NewGuid().ToString("N")[..8];

        var response = await TenantAdministrationTestSupport.SendAsync(_host, HttpMethod.Post, "/api/tenant/members", adminToken,
            new { login, initialPassword = IdentityTestServices.Password, tenantId = Guid.NewGuid() });
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.DoesNotContain(IdentityTestServices.Password, body);
        var member = await TenantAdministrationTestSupport.SendAsync(_host, HttpMethod.Get, "/api/tenant/members", adminToken);
        var listed = (await member.Content.ReadFromJsonAsync<List<MemberBody>>())!.Single(entry => entry.Login == login);
        Assert.True(listed.IsEnabled);
        Assert.True(listed.MustChangePassword);
        Assert.DoesNotContain(listed.Roles, role => role != AdministratorRoleName);
        Assert.True(await TenantAdministrationTestSupport.AuditContainsAsync(_host, tenantId, AuditActions.MemberCreated));
    }

    [Fact]
    public async Task CreatedMember_SignsInWithTheInitialPassword_AndIsRestrictedUntilChanged()
    {
        var tenantId = Guid.NewGuid();
        var (_, adminToken) = await TenantAdministrationTestSupport.SignedInAsync(_host, tenantId, AdministratorRoleName);
        var login = "fresh-" + Guid.NewGuid().ToString("N")[..8];
        await TenantAdministrationTestSupport.SendAsync(_host, HttpMethod.Post, "/api/tenant/members", adminToken,
            new { login, initialPassword = IdentityTestServices.Password });

        var (status, token, _) = await SessionTestSupport.SignInTokensAsync(_host, login);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(HttpStatusCode.Forbidden, (await SessionTestSupport.GetProtectedAsync(_host, token!)).StatusCode);
    }

    [Fact]
    public async Task DuplicateLogin_IsConflict()
    {
        var (user, adminToken) = await TenantAdministrationTestSupport.SignedInAsync(_host, Guid.NewGuid(), AdministratorRoleName);

        var response = await TenantAdministrationTestSupport.SendAsync(_host, HttpMethod.Post, "/api/tenant/members", adminToken,
            new { login = user.UserName, initialPassword = IdentityTestServices.Password });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task PasswordBelowThePolicy_IsBadRequest_AndNoMemberIsCreated()
    {
        var tenantId = Guid.NewGuid();
        var (_, adminToken) = await TenantAdministrationTestSupport.SignedInAsync(_host, tenantId, AdministratorRoleName);

        var response = await TenantAdministrationTestSupport.SendAsync(_host, HttpMethod.Post, "/api/tenant/members", adminToken,
            new { login = "weak-" + Guid.NewGuid().ToString("N")[..6], initialPassword = "short" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Disable_RevokesSessions_BlocksSignIn_AndIsAudited()
    {
        var tenantId = Guid.NewGuid();
        var (_, adminToken) = await TenantAdministrationTestSupport.SignedInAsync(_host, tenantId, AdministratorRoleName);
        var member = await SessionTestSupport.CreateUserAsync(_host, tenantId);
        var (_, memberToken, _) = await SessionTestSupport.SignInTokensAsync(_host, member.UserName!);

        var response = await TenantAdministrationTestSupport.SendAsync(_host, HttpMethod.Post, $"/api/tenant/members/{member.Id}/disable", adminToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SessionTestSupport.GetProtectedAsync(_host, memberToken!)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SessionTestSupport.SignInAsync(_host, member.UserName!)).StatusCode);
        Assert.True(await TenantAdministrationTestSupport.AuditContainsAsync(_host, tenantId, AuditActions.MemberDisabled, targetId: member.Id.ToString("D")));
        Assert.True(await TenantAdministrationTestSupport.AuditContainsAsync(_host, tenantId, AuditActions.SessionRevoked));
    }

    [Fact]
    public async Task Enable_RestoresSignIn()
    {
        var tenantId = Guid.NewGuid();
        var (_, adminToken) = await TenantAdministrationTestSupport.SignedInAsync(_host, tenantId, AdministratorRoleName);
        var member = await SessionTestSupport.CreateUserAsync(_host, tenantId);
        await TenantAdministrationTestSupport.SendAsync(_host, HttpMethod.Post, $"/api/tenant/members/{member.Id}/disable", adminToken);

        var response = await TenantAdministrationTestSupport.SendAsync(_host, HttpMethod.Post, $"/api/tenant/members/{member.Id}/enable", adminToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SessionTestSupport.SignInAsync(_host, member.UserName!)).StatusCode);
        Assert.True(await TenantAdministrationTestSupport.AuditContainsAsync(_host, tenantId, AuditActions.MemberEnabled));
    }

    [Fact]
    public async Task DisablingTheLastEnabledAdministrator_IsConflict()
    {
        var tenantId = Guid.NewGuid();
        var (admin, adminToken) = await TenantAdministrationTestSupport.SignedInAsync(_host, tenantId, AdministratorRoleName);

        var response = await TenantAdministrationTestSupport.SendAsync(_host, HttpMethod.Post, $"/api/tenant/members/{admin.Id}/disable", adminToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await TenantAdministrationTestSupport.SendAsync(_host, HttpMethod.Get, "/api/tenant/members", adminToken)).StatusCode);
    }

    [Fact]
    public async Task MemberOfAnotherTenant_IsNotFound()
    {
        var (_, adminToken) = await TenantAdministrationTestSupport.SignedInAsync(_host, Guid.NewGuid(), AdministratorRoleName);
        var outsider = await SessionTestSupport.CreateUserAsync(_host, Guid.NewGuid());

        var response = await TenantAdministrationTestSupport.SendAsync(_host, HttpMethod.Post, $"/api/tenant/members/{outsider.Id}/disable", adminToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DisableByANonAdministrator_IsForbidden_AndTheDenialIsAudited()
    {
        var tenantId = Guid.NewGuid();
        var (_, memberToken) = await TenantAdministrationTestSupport.SignedInAsync(_host, tenantId);
        var target = await SessionTestSupport.CreateUserAsync(_host, tenantId);

        var response = await TenantAdministrationTestSupport.SendAsync(_host, HttpMethod.Post, $"/api/tenant/members/{target.Id}/disable", memberToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.True(await TenantAdministrationTestSupport.AuditContainsAsync(_host, tenantId, AuditActions.MemberDisabled, "denied"));
    }

    [Fact]
    public async Task AdministratorRole_IsGrantedAndWithdrawn_WithTheSessionsEndedOnWithdrawal()
    {
        var tenantId = Guid.NewGuid();
        var (_, adminToken) = await TenantAdministrationTestSupport.SignedInAsync(_host, tenantId, AdministratorRoleName);
        var colleague = await SessionTestSupport.CreateUserAsync(_host, tenantId);
        var (_, colleagueToken, _) = await SessionTestSupport.SignInTokensAsync(_host, colleague.UserName!);

        var grant = await TenantAdministrationTestSupport.SendAsync(_host, HttpMethod.Put, $"/api/tenant/members/{colleague.Id}/administrator", adminToken,
            new { isAdministrator = true });
        Assert.Equal(HttpStatusCode.NoContent, grant.StatusCode);
        Assert.True(await TenantAdministrationTestSupport.AuditContainsAsync(_host, tenantId, AuditActions.AdministratorRoleGranted));

        var withdraw = await TenantAdministrationTestSupport.SendAsync(_host, HttpMethod.Put, $"/api/tenant/members/{colleague.Id}/administrator", adminToken,
            new { isAdministrator = false });
        Assert.Equal(HttpStatusCode.NoContent, withdraw.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SessionTestSupport.GetProtectedAsync(_host, colleagueToken!)).StatusCode);
        Assert.True(await TenantAdministrationTestSupport.AuditContainsAsync(_host, tenantId, AuditActions.AdministratorRoleWithdrawn));
    }

    [Fact]
    public async Task WithdrawingTheRoleFromTheLastEnabledAdministrator_IsConflict()
    {
        var tenantId = Guid.NewGuid();
        var (admin, adminToken) = await TenantAdministrationTestSupport.SignedInAsync(_host, tenantId, AdministratorRoleName);

        var response = await TenantAdministrationTestSupport.SendAsync(_host, HttpMethod.Put, $"/api/tenant/members/{admin.Id}/administrator", adminToken,
            new { isAdministrator = false });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    private const string AdministratorRoleName = TenantAdministrationTestSupport.AdministratorRole;

    private sealed record MemberBody(Guid Id, string Login, List<string> Roles, int GroupCount, bool IsEnabled, bool MustChangePassword);
}
