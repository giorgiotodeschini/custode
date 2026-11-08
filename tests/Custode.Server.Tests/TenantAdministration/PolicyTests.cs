// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using System.Net;
using System.Net.Http.Json;
using Custode.Infrastructure.Audit;
using Custode.Server.Tests.Hosting;
using Custode.Server.Tests.Sessions;

namespace Custode.Server.Tests.TenantAdministration;

public sealed class PolicyTests : IAsyncLifetime
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
    public async Task NewTenant_ReadsTheDefaults()
    {
        var (_, memberToken) = await TenantAdministrationTestSupport.SignedInAsync(_host, Guid.NewGuid());

        var policy = await (await TenantAdministrationTestSupport.SendAsync(_host, HttpMethod.Get, "/api/tenant/policies", memberToken))
            .Content.ReadFromJsonAsync<PolicyBody>();

        Assert.Equal(15, policy!.IdleTimeoutMinutes);
        Assert.Equal(15, policy.AccessTokenLifetimeMinutes);
        Assert.Equal(7, policy.RefreshTokenLifetimeDays);
        Assert.Equal(30, policy.ClipboardClearSeconds);
    }

    [Fact]
    public async Task Administrator_SavesAValidPolicy_AndItIsReadByEveryMember()
    {
        var tenantId = Guid.NewGuid();
        var (_, adminToken) = await TenantAdministrationTestSupport.SignedInAsync(_host, tenantId, TenantAdministrationTestSupport.AdministratorRole);
        var (_, memberToken) = await TenantAdministrationTestSupport.SignedInAsync(_host, tenantId);

        var save = await TenantAdministrationTestSupport.SendAsync(_host, HttpMethod.Put, "/api/tenant/policies", adminToken,
            new { idleTimeoutMinutes = 20, accessTokenLifetimeMinutes = 10, refreshTokenLifetimeDays = 3, clipboardClearSeconds = 45 });

        Assert.Equal(HttpStatusCode.OK, save.StatusCode);
        var seen = await (await TenantAdministrationTestSupport.SendAsync(_host, HttpMethod.Get, "/api/tenant/policies", memberToken))
            .Content.ReadFromJsonAsync<PolicyBody>();
        Assert.Equal(20, seen!.IdleTimeoutMinutes);
        Assert.Equal(45, seen.ClipboardClearSeconds);
        Assert.True(await TenantAdministrationTestSupport.AuditContainsAsync(_host, tenantId, AuditActions.TenantPolicyChanged));
    }

    [Fact]
    public async Task OutOfRangeValue_IsRejectedNamingTheField_AndNothingIsSaved()
    {
        var tenantId = Guid.NewGuid();
        var (_, adminToken) = await TenantAdministrationTestSupport.SignedInAsync(_host, tenantId, TenantAdministrationTestSupport.AdministratorRole);

        var response = await TenantAdministrationTestSupport.SendAsync(_host, HttpMethod.Put, "/api/tenant/policies", adminToken,
            new { idleTimeoutMinutes = 2, accessTokenLifetimeMinutes = 15, refreshTokenLifetimeDays = 7, clipboardClearSeconds = 30 });
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("idleTimeoutMinutes", body);
        var current = await (await TenantAdministrationTestSupport.SendAsync(_host, HttpMethod.Get, "/api/tenant/policies", adminToken))
            .Content.ReadFromJsonAsync<PolicyBody>();
        Assert.Equal(15, current!.IdleTimeoutMinutes);
    }

    [Fact]
    public async Task MemberWhoTriesToChangeThePolicy_IsForbidden_AndTheDenialIsAudited()
    {
        var tenantId = Guid.NewGuid();
        var (_, memberToken) = await TenantAdministrationTestSupport.SignedInAsync(_host, tenantId);

        var response = await TenantAdministrationTestSupport.SendAsync(_host, HttpMethod.Put, "/api/tenant/policies", memberToken,
            new { idleTimeoutMinutes = 20, accessTokenLifetimeMinutes = 15, refreshTokenLifetimeDays = 7, clipboardClearSeconds = 30 });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.True(await TenantAdministrationTestSupport.AuditContainsAsync(_host, tenantId, AuditActions.TenantPolicyChanged, "denied"));
    }

    [Fact]
    public void Validator_AcceptsTheBoundaries_AndRejectsOneStepOutside()
    {
        Assert.Empty(Custode.Infrastructure.Administration.TenantPolicyValidator.Validate(5, 5, 1, 10));
        Assert.Empty(Custode.Infrastructure.Administration.TenantPolicyValidator.Validate(120, 60, 30, 120));
        Assert.Equal(4, Custode.Infrastructure.Administration.TenantPolicyValidator.Validate(4, 61, 0, 121).Count);
        Assert.Single(Custode.Infrastructure.Administration.TenantPolicyValidator.Validate(null, 15, 7, 30));
    }

    private sealed record PolicyBody(int IdleTimeoutMinutes, int AccessTokenLifetimeMinutes, int RefreshTokenLifetimeDays, int ClipboardClearSeconds);
}
