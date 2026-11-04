// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using System.Net;
using System.Net.Http.Json;
using Custode.Server.Features.Authentication.SignIn;
using Custode.Server.Tests.Hosting;
using Custode.Server.Tests.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Custode.Server.Tests.Authentication;

public sealed class SignInEndpointTests : IAsyncLifetime
{
    private CustodeTestHost _host = null!;

    public async Task InitializeAsync()
    {
        _host = await CustodeTestHost.StartAsync();
        using var scope = _host.Services.CreateScope();
        await IdentityTestServices.CreateUserAsync(scope.ServiceProvider, "alice", Guid.NewGuid(), "Operator");
    }

    public async Task DisposeAsync()
    {
        await _host.DisposeAsync();
    }

    [Fact]
    public async Task ValidCredentials_ReturnTokenAndExpiry()
    {
        var response = await _host.Client.PostAsJsonAsync("/api/auth/sign-in", new SignInRequest("alice", IdentityTestServices.Password));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<SignInResponse>();
        Assert.NotNull(body);
        Assert.False(string.IsNullOrWhiteSpace(body.AccessToken));
        Assert.True(body.ExpiresAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task WrongPassword_Returns401()
    {
        var response = await _host.Client.PostAsJsonAsync("/api/auth/sign-in", new SignInRequest("alice", "wrong-password-1!"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UnknownLogin_Returns401()
    {
        var response = await _host.Client.PostAsJsonAsync("/api/auth/sign-in", new SignInRequest("nobody", IdentityTestServices.Password));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task WrongPasswordAndUnknownLogin_ReturnIdenticalResponses()
    {
        var wrongPassword = await _host.Client.PostAsJsonAsync("/api/auth/sign-in", new SignInRequest("alice", "wrong-password-1!"));
        var unknownLogin = await _host.Client.PostAsJsonAsync("/api/auth/sign-in", new SignInRequest("nobody", "wrong-password-1!"));

        Assert.Equal(wrongPassword.StatusCode, unknownLogin.StatusCode);
        Assert.Equal(await wrongPassword.Content.ReadAsStringAsync(), await unknownLogin.Content.ReadAsStringAsync());
    }
}
