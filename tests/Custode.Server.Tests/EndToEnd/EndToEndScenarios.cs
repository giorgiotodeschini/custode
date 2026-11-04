// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using Custode.Infrastructure.SecretProtection;
using Custode.Infrastructure.Tenancy;
using Custode.Server.Features.Authentication.SignIn;
using Custode.Server.Tests.Hosting;
using Custode.Server.Tests.Identity;
using Custode.Server.Tests.SecretProtection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Xunit;

namespace Custode.Server.Tests.EndToEnd;

public static class EndToEndScenarios
{
    public static async Task TwoTenantsAndOneOperator(Func<string, Task<CustodeTestHost>> startHost)
    {
        await using var host = await startHost(TestKeyEncryptionCertificate.Shared);

        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        using (var scope = host.Services.CreateScope())
        {
            await IdentityTestServices.CreateUserAsync(scope.ServiceProvider, "alice", tenantA);
            await IdentityTestServices.CreateUserAsync(scope.ServiceProvider, "bob", tenantB);
            await IdentityTestServices.CreateUserAsync(scope.ServiceProvider, "ops", Guid.NewGuid(), "Operator");
        }

        var aliceToken = await SignInAsync(host, "alice");
        var bobToken = await SignInAsync(host, "bob");
        var opsToken = await SignInAsync(host, "ops");

        var aliceTenant = new JsonWebTokenHandler().ReadJsonWebToken(aliceToken).GetClaim("tenant_id")?.Value;
        var bobTenant = new JsonWebTokenHandler().ReadJsonWebToken(bobToken).GetClaim("tenant_id")?.Value;
        Assert.Equal(tenantA.ToString(), aliceTenant);
        Assert.Equal(tenantB.ToString(), bobTenant);

        var authorization = host.Services.GetRequiredService<IAuthorizationService>();
        Assert.False((await authorization.AuthorizeAsync(PrincipalFor(aliceToken), null, TenantPolicies.Operator)).Succeeded);
        Assert.True((await authorization.AuthorizeAsync(PrincipalFor(opsToken), null, TenantPolicies.Operator)).Succeeded);

        var protector = host.Services.GetRequiredService<ISecretProtector>();
        var aliceSecret = protector.Protect(tenantA, "alice-secret");
        Assert.Throws<CryptographicException>(() => protector.Unprotect(tenantB, aliceSecret));
        Assert.Equal("alice-secret", protector.Unprotect(tenantA, aliceSecret));

        var rejected = await host.Client.PostAsJsonAsync("/api/auth/sign-in", new SignInRequest("alice", "wrong-password-1!"));
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, rejected.StatusCode);
    }

    public static async Task SecretsProtectedBeforeRestart(Func<string, Task<CustodeTestHost>> startHost)
    {
        var certificatePath = TestKeyEncryptionCertificate.Create("kek-restart");
        var tenantId = Guid.NewGuid();

        string ciphertext;
        await using (var before = await startHost(certificatePath))
        {
            ciphertext = before.Services.GetRequiredService<ISecretProtector>().Protect(tenantId, "survives-restart");
        }

        await using var after = await startHost(certificatePath);
        var protector = after.Services.GetRequiredService<ISecretProtector>();

        Assert.Equal("survives-restart", protector.Unprotect(tenantId, ciphertext));
    }

    private static async Task<string> SignInAsync(CustodeTestHost host, string login)
    {
        var response = await host.Client.PostAsJsonAsync(
            "/api/auth/sign-in",
            new SignInRequest(login, IdentityTestServices.Password));

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<SignInResponse>();
        return body!.AccessToken;
    }

    private static ClaimsPrincipal PrincipalFor(string token)
    {
        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(token);
        return new ClaimsPrincipal(new ClaimsIdentity(jwt.Claims, "test", "sub", "role"));
    }
}
