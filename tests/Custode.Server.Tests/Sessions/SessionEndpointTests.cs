// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Custode.Infrastructure.Audit;
using Custode.Infrastructure.Persistence;
using Custode.Infrastructure.Sessions;
using Custode.Infrastructure.Tenancy;
using Custode.Server.Tests.Hosting;
using Custode.Server.Tests.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Custode.Server.Tests.Sessions;

public sealed class SessionEndpointTests : IAsyncLifetime
{
    private readonly ManualClock _clock = new(DateTimeOffset.UtcNow);
    private CustodeTestHost _host = null!;

    public async Task InitializeAsync()
    {
        _host = await CustodeTestHost.StartAsync(_clock);
    }

    public async Task DisposeAsync()
    {
        await _host.DisposeAsync();
    }

    [Fact]
    public async Task SignIn_SetsRefreshCookie_WithRequiredAttributes_AndOmitsItFromTheBody()
    {
        var user = await SessionTestSupport.CreateUserAsync(_host, Guid.NewGuid());

        var response = await SessionTestSupport.SignInAsync(_host, user.UserName!);
        var setCookie = string.Join(";", response.Headers.GetValues("Set-Cookie"));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("custode_refresh=", setCookie);
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/api/auth", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("refresh", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AccessToken_CarriesTheSessionIdentifier()
    {
        var tenantId = Guid.NewGuid();
        var user = await SessionTestSupport.CreateUserAsync(_host, tenantId);

        var (_, accessToken, _) = await SessionTestSupport.SignInTokensAsync(_host, user.UserName!);
        var session = await SessionTestSupport.SessionOfAsync(_host, tenantId, user.Id);
        var token = new JsonWebTokenHandler().ReadJsonWebToken(accessToken);

        Assert.Equal(session.Id.ToString(), token.GetClaim("sid")?.Value);
    }

    [Fact]
    public async Task Refresh_WithoutTheRefreshCookie_IsUnauthorized()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await SessionTestSupport.RefreshAsync(_host, null)).StatusCode);
    }

    [Fact]
    public async Task RefreshToken_IsStoredOnlyAsAHash()
    {
        var tenantId = Guid.NewGuid();
        var user = await SessionTestSupport.CreateUserAsync(_host, tenantId);
        var (_, _, cookie) = await SessionTestSupport.SignInTokensAsync(_host, user.UserName!);

        await using var db = _host.CreateTenantContext(tenantId);
        var stored = await db.RefreshTokens.SingleAsync();

        Assert.NotEqual(cookie, stored.TokenHash);
        Assert.Equal(Custode.Infrastructure.Sessions.RefreshTokens.Hash(cookie!), stored.TokenHash);
    }

    [Fact]
    public async Task ShortenedRefreshLifetime_AppliesToAnExistingSession_AtItsNextRefresh()
    {
        var tenantId = Guid.NewGuid();
        var user = await SessionTestSupport.CreateUserAsync(_host, tenantId);
        var (_, _, cookie) = await SessionTestSupport.SignInTokensAsync(_host, user.UserName!);

        _clock.Advance(TimeSpan.FromDays(2));
        await using (var db = _host.CreateTenantContext(tenantId))
        {
            db.TenantPolicies.Add(new Custode.Infrastructure.Administration.TenantPolicy
            {
                TenantId = tenantId,
                IdleTimeoutMinutes = 120,
                RefreshTokenLifetimeDays = 1,
            });
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await SessionTestSupport.RefreshAsync(_host, cookie)).StatusCode);
    }

    [Fact]
    public async Task Refresh_WithoutTheCustodeRequestHeader_IsForbidden()
    {
        var user = await SessionTestSupport.CreateUserAsync(_host, Guid.NewGuid());
        var (_, _, cookie) = await SessionTestSupport.SignInTokensAsync(_host, user.UserName!);

        var response = await SessionTestSupport.RefreshAsync(_host, cookie, withHeader: false);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_RotatesTheRefreshToken_AndIssuesANewAccessToken()
    {
        var user = await SessionTestSupport.CreateUserAsync(_host, Guid.NewGuid());
        var (_, _, cookie) = await SessionTestSupport.SignInTokensAsync(_host, user.UserName!);

        var response = await SessionTestSupport.RefreshAsync(_host, cookie);
        var rotated = SessionTestSupport.CookieValue(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(rotated);
        Assert.NotEqual(cookie, rotated);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SessionTestSupport.RefreshAsync(_host, cookie)).StatusCode);
    }

    [Fact]
    public async Task ReplacedRefreshToken_WithinTheGraceWindow_IsRejected_AndTheSessionStaysActive()
    {
        var tenantId = Guid.NewGuid();
        var user = await SessionTestSupport.CreateUserAsync(_host, tenantId);
        var (_, _, cookie) = await SessionTestSupport.SignInTokensAsync(_host, user.UserName!);
        var rotated = SessionTestSupport.CookieValue(await SessionTestSupport.RefreshAsync(_host, cookie));

        _clock.Advance(TimeSpan.FromSeconds(3));
        var replayed = await SessionTestSupport.RefreshAsync(_host, cookie);

        Assert.Equal(HttpStatusCode.Unauthorized, replayed.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SessionTestSupport.RefreshAsync(_host, rotated)).StatusCode);
    }

    [Fact]
    public async Task ReplacedRefreshToken_AfterTheGraceWindow_RevokesTheSession_AndIsAudited()
    {
        var tenantId = Guid.NewGuid();
        var user = await SessionTestSupport.CreateUserAsync(_host, tenantId);
        var (_, accessToken, cookie) = await SessionTestSupport.SignInTokensAsync(_host, user.UserName!);
        var rotated = SessionTestSupport.CookieValue(await SessionTestSupport.RefreshAsync(_host, cookie));

        _clock.Advance(TimeSpan.FromSeconds(30));
        var reused = await SessionTestSupport.RefreshAsync(_host, cookie);

        Assert.Equal(HttpStatusCode.Unauthorized, reused.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SessionTestSupport.RefreshAsync(_host, rotated)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SessionTestSupport.GetProtectedAsync(_host, accessToken!)).StatusCode);
        Assert.True(await SessionTestSupport.AuditContainsAsync(_host, tenantId, AuditActions.RefreshTokenReuseDetected, "denied"));
    }

    [Fact]
    public async Task ReplacedRefreshToken_TwoRotationsBack_AfterTheGraceWindow_RevokesTheSession()
    {
        var user = await SessionTestSupport.CreateUserAsync(_host, Guid.NewGuid());
        var (_, accessToken, first) = await SessionTestSupport.SignInTokensAsync(_host, user.UserName!);
        var second = SessionTestSupport.CookieValue(await SessionTestSupport.RefreshAsync(_host, first));
        var third = SessionTestSupport.CookieValue(await SessionTestSupport.RefreshAsync(_host, second));

        _clock.Advance(TimeSpan.FromMinutes(1));
        var reused = await SessionTestSupport.RefreshAsync(_host, first);

        Assert.Equal(HttpStatusCode.Unauthorized, reused.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SessionTestSupport.RefreshAsync(_host, third)).StatusCode);
    }

    [Fact]
    public async Task Refresh_DoesNotCountAsActivity_SoAnIdleSessionEnds()
    {
        var user = await SessionTestSupport.CreateUserAsync(_host, Guid.NewGuid());
        var (_, _, cookie) = await SessionTestSupport.SignInTokensAsync(_host, user.UserName!);

        _clock.Advance(TimeSpan.FromMinutes(10));
        var renewed = SessionTestSupport.CookieValue(await SessionTestSupport.RefreshAsync(_host, cookie));
        _clock.Advance(TimeSpan.FromMinutes(10));

        Assert.Equal(HttpStatusCode.Unauthorized, (await SessionTestSupport.RefreshAsync(_host, renewed)).StatusCode);
    }

    [Fact]
    public async Task ActivityCall_ExtendsTheIdleTimeout()
    {
        var user = await SessionTestSupport.CreateUserAsync(_host, Guid.NewGuid());
        var (_, accessToken, _) = await SessionTestSupport.SignInTokensAsync(_host, user.UserName!);

        _clock.Advance(TimeSpan.FromMinutes(10));
        var activity = new HttpRequestMessage(HttpMethod.Post, "/api/auth/activity");
        activity.Headers.Add("X-Custode-Request", "1");
        activity.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        Assert.Equal(HttpStatusCode.NoContent, (await _host.Client.SendAsync(activity)).StatusCode);

        _clock.Advance(TimeSpan.FromMinutes(10));

        Assert.Equal(HttpStatusCode.OK, (await SessionTestSupport.GetProtectedAsync(_host, accessToken!)).StatusCode);
    }

    [Fact]
    public async Task IdleSession_AccessTokenRejected_AfterTheTimeout()
    {
        var user = await SessionTestSupport.CreateUserAsync(_host, Guid.NewGuid());
        var (_, accessToken, _) = await SessionTestSupport.SignInTokensAsync(_host, user.UserName!);

        _clock.Advance(TimeSpan.FromMinutes(16));

        Assert.Equal(HttpStatusCode.Unauthorized, (await SessionTestSupport.GetProtectedAsync(_host, accessToken!)).StatusCode);
    }

    [Fact]
    public async Task SessionPastItsAbsoluteLifetime_EndsOnRefresh()
    {
        var tenantId = Guid.NewGuid();
        var user = await SessionTestSupport.CreateUserAsync(_host, tenantId);
        await using (var db = _host.CreateTenantContext(tenantId))
        {
            db.TenantPolicies.Add(new Custode.Infrastructure.Administration.TenantPolicy
            {
                TenantId = tenantId,
                IdleTimeoutMinutes = 120,
                RefreshTokenLifetimeDays = 1,
            });
            await db.SaveChangesAsync();
        }

        var (_, _, cookie) = await SessionTestSupport.SignInTokensAsync(_host, user.UserName!);
        for (var hour = 0; hour < 25; hour++)
        {
            _clock.Advance(TimeSpan.FromHours(1));
            var session = await SessionTestSupport.SessionOfAsync(_host, tenantId, user.Id);
            Assert.Null(session.RevokedAtUtc);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await SessionTestSupport.RefreshAsync(_host, cookie)).StatusCode);
    }

    [Fact]
    public async Task AccessToken_IsRejected_OnceTheRefreshLifetimeHasPassed_EvenWhenActive()
    {
        var tenantId = Guid.NewGuid();
        var user = await SessionTestSupport.CreateUserAsync(_host, tenantId);
        await using (var db = _host.CreateTenantContext(tenantId))
        {
            db.TenantPolicies.Add(new Custode.Infrastructure.Administration.TenantPolicy
            {
                TenantId = tenantId,
                IdleTimeoutMinutes = 120,
                RefreshTokenLifetimeDays = 1,
            });
            await db.SaveChangesAsync();
        }
        var (_, accessToken, _) = await SessionTestSupport.SignInTokensAsync(_host, user.UserName!);

        for (var hour = 0; hour < 24; hour++)
        {
            _clock.Advance(TimeSpan.FromHours(1));
            Assert.Equal(HttpStatusCode.OK, (await SessionTestSupport.GetProtectedAsync(_host, accessToken!)).StatusCode);
        }
        _clock.Advance(TimeSpan.FromHours(1));

        Assert.Equal(HttpStatusCode.Unauthorized, (await SessionTestSupport.GetProtectedAsync(_host, accessToken!)).StatusCode);
    }

    [Fact]
    public async Task SignOut_RevokesTheSession_ClearsTheCookie_AndRejectsTheAccessToken()
    {
        var tenantId = Guid.NewGuid();
        var user = await SessionTestSupport.CreateUserAsync(_host, tenantId);
        var (_, accessToken, cookie) = await SessionTestSupport.SignInTokensAsync(_host, user.UserName!);

        var response = await SessionTestSupport.SignOutAsync(_host, accessToken, cookie);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Contains("custode_refresh=;", string.Join(";", response.Headers.GetValues("Set-Cookie")));
        Assert.Equal(HttpStatusCode.Unauthorized, (await SessionTestSupport.GetProtectedAsync(_host, accessToken!)).StatusCode);
        Assert.True(await SessionTestSupport.AuditContainsAsync(_host, tenantId, AuditActions.SessionRevoked));
    }

    [Fact]
    public async Task SignOut_WithoutTheCustodeRequestHeader_IsForbidden()
    {
        var user = await SessionTestSupport.CreateUserAsync(_host, Guid.NewGuid());
        var (_, _, cookie) = await SessionTestSupport.SignInTokensAsync(_host, user.UserName!);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/sign-out");
        request.Headers.Add("Cookie", "custode_refresh=" + cookie);

        Assert.Equal(HttpStatusCode.Forbidden, (await _host.Client.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task DisabledAccount_CannotSignIn_AndItsAccessTokenIsRejected()
    {
        var tenantId = Guid.NewGuid();
        var user = await SessionTestSupport.CreateUserAsync(_host, tenantId);
        var (_, accessToken, _) = await SessionTestSupport.SignInTokensAsync(_host, user.UserName!);

        await using (var db = _host.CreateTenantContext(tenantId))
        {
            var stored = await db.Users.SingleAsync(entity => entity.Id == user.Id);
            stored.IsEnabled = false;
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await SessionTestSupport.SignInAsync(_host, user.UserName!)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SessionTestSupport.GetProtectedAsync(_host, accessToken!)).StatusCode);
    }

    [Fact]
    public async Task TokenWithoutSessionIdentifier_IsRejected()
    {
        var tenantId = Guid.NewGuid();
        var user = await SessionTestSupport.CreateUserAsync(_host, tenantId);
        var token = TestTokens.Create(
            [
                new System.Security.Claims.Claim("sub", user.Id.ToString()),
                new System.Security.Claims.Claim("tenant_id", tenantId.ToString()),
            ],
            DateTime.UtcNow.AddMinutes(10));

        Assert.Equal(HttpStatusCode.Unauthorized, (await SessionTestSupport.GetProtectedAsync(_host, token)).StatusCode);
    }

    [Fact]
    public async Task ResolvingACredential_ReturnsTheTenantOfTheSession_WithoutAnyTenantScope()
    {
        var tenantId = Guid.NewGuid();
        var user = await SessionTestSupport.CreateUserAsync(_host, tenantId);
        var (_, _, cookie) = await SessionTestSupport.SignInTokensAsync(_host, user.UserName!);

        using var scope = _host.Services.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<SessionCredentialResolver>();

        var credential = await resolver.ResolveAsync(cookie!, CancellationToken.None);

        Assert.NotNull(credential);
        Assert.Equal(tenantId, credential.TenantId);
        Assert.Null(credential.ReplacedAtUtc);
    }
}
