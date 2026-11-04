// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using Custode.Server.Tests.Hosting;
using Custode.Server.Tests.Identity;

namespace Custode.Server.Tests.Authentication;

public sealed class BearerValidationTests : IAsyncLifetime
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
    public async Task RequestWithoutToken_ToProtectedEndpoint_Returns401()
    {
        var response = await _host.Client.GetAsync("/tests/protected");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ValidToken_ToProtectedEndpoint_Returns200()
    {
        var token = TestTokens.ForTenant(Guid.NewGuid());

        var response = await SendWithTokenAsync(token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ExpiredToken_Returns401()
    {
        var token = TestTokens.Create(TenantClaims(Guid.NewGuid()), DateTime.UtcNow.AddHours(-1));

        var response = await SendWithTokenAsync(token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TokenWithInvalidSignature_Returns401()
    {
        var token = TestTokens.Create(
            TenantClaims(Guid.NewGuid()),
            DateTime.UtcNow.AddMinutes(10),
            TestTokens.OtherSigningKey);

        var response = await SendWithTokenAsync(token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private Task<HttpResponseMessage> SendWithTokenAsync(string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/tests/protected")
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) },
        };

        return _host.Client.SendAsync(request);
    }

    private static IEnumerable<Claim> TenantClaims(Guid tenantId)
    {
        return
        [
            new Claim("sub", Guid.NewGuid().ToString()),
            new Claim("tenant_id", tenantId.ToString()),
        ];
    }
}
