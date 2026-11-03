using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using Custode.Infrastructure.Tenancy;
using Custode.Server.Tests.Hosting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace Custode.Server.Tests.Authentication;

public sealed class TenantBindingTests : IAsyncLifetime
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
    public async Task TokenWithoutTenantClaim_Returns401()
    {
        var token = TestTokens.Create(
            [new Claim("sub", Guid.NewGuid().ToString())],
            DateTime.UtcNow.AddMinutes(10));

        var response = await SendGetAsync("/tests/protected", token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TokenWithMalformedTenantClaim_Returns401()
    {
        var token = TestTokens.Create(
            [new Claim("sub", Guid.NewGuid().ToString()), new Claim("tenant_id", "not-a-guid")],
            DateTime.UtcNow.AddMinutes(10));

        var response = await SendGetAsync("/tests/protected", token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TenantScope_IsTakenFromTokenClaim()
    {
        var tenantId = Guid.NewGuid();
        var token = TestTokens.ForTenant(tenantId);

        var tenant = await ReadTenantAsync(await SendGetAsync("/tests/tenant", token));

        Assert.Equal(tenantId, tenant);
    }

    [Fact]
    public async Task TenantSuppliedInHeader_IsIgnored()
    {
        var tenantId = Guid.NewGuid();
        var token = TestTokens.ForTenant(tenantId);
        var request = new HttpRequestMessage(HttpMethod.Get, "/tests/tenant")
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) },
        };
        request.Headers.Add("X-Tenant-Id", Guid.NewGuid().ToString());

        var tenant = await ReadTenantAsync(await _host.Client.SendAsync(request));

        Assert.Equal(tenantId, tenant);
    }

    [Fact]
    public async Task TenantSuppliedInQueryString_IsIgnored()
    {
        var tenantId = Guid.NewGuid();
        var token = TestTokens.ForTenant(tenantId);

        var tenant = await ReadTenantAsync(
            await SendGetAsync($"/tests/tenant?tenantId={Guid.NewGuid()}", token));

        Assert.Equal(tenantId, tenant);
    }

    [Fact]
    public async Task TenantSuppliedInBody_IsIgnored()
    {
        var tenantId = Guid.NewGuid();
        var token = TestTokens.ForTenant(tenantId);
        var request = new HttpRequestMessage(HttpMethod.Post, "/tests/tenant")
        {
            Content = JsonContent.Create(new CustodeTestHost.TenantBody(Guid.NewGuid())),
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) },
        };

        var tenant = await ReadTenantAsync(await _host.Client.SendAsync(request));

        Assert.Equal(tenantId, tenant);
    }

    [Fact]
    public async Task OperatorPolicy_RequiresOperatorRole()
    {
        var authorization = _host.Services.GetRequiredService<IAuthorizationService>();
        var member = new ClaimsPrincipal(new ClaimsIdentity([new Claim("role", "Member")], "test", "sub", "role"));
        var operatorUser = new ClaimsPrincipal(new ClaimsIdentity([new Claim("role", "Operator")], "test", "sub", "role"));

        var memberResult = await authorization.AuthorizeAsync(member, null, TenantPolicies.Operator);
        var operatorResult = await authorization.AuthorizeAsync(operatorUser, null, TenantPolicies.Operator);

        Assert.False(memberResult.Succeeded);
        Assert.True(operatorResult.Succeeded);
    }

    private Task<HttpResponseMessage> SendGetAsync(string path, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path)
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) },
        };

        return _host.Client.SendAsync(request);
    }

    private static async Task<Guid?> ReadTenantAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var value = document.RootElement.GetProperty("tenantId");

        return value.ValueKind == JsonValueKind.Null ? null : value.GetGuid();
    }
}
