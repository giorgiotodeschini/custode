// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Custode.Infrastructure.Identity;
using Custode.Infrastructure.Persistence;
using Custode.Server.Tests.Hosting;
using Custode.Server.Tests.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Custode.Server.Tests.Sessions;

public static class SessionTestSupport
{
    public const string CookieName = "custode_refresh";

    public static async Task<ApplicationUser> CreateUserAsync(CustodeTestHost host, Guid tenantId, params string[] roles)
    {
        using var scope = host.Services.CreateScope();

        return await IdentityTestServices.CreateUserAsync(scope.ServiceProvider, "user-" + Guid.NewGuid().ToString("N"), tenantId, roles);
    }

    public static async Task<HttpResponseMessage> SignInAsync(CustodeTestHost host, string login, string password = IdentityTestServices.Password)
    {
        return await host.Client.PostAsJsonAsync("/api/auth/sign-in", new { login, password });
    }

    public static string? CookieValue(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var values))
        {
            return null;
        }

        var cookie = values.FirstOrDefault(value => value.StartsWith(CookieName + "=", StringComparison.Ordinal));

        return cookie is null ? null : cookie[(CookieName.Length + 1)..].Split(';')[0];
    }

    public static async Task<(HttpStatusCode Status, string? AccessToken, string? RefreshCookie)> SignInTokensAsync(CustodeTestHost host, string login)
    {
        var response = await SignInAsync(host, login);
        var body = response.StatusCode == HttpStatusCode.OK
            ? await response.Content.ReadFromJsonAsync<SignInBody>()
            : null;

        return (response.StatusCode, body?.AccessToken, CookieValue(response));
    }

    public static Task<HttpResponseMessage> RefreshAsync(CustodeTestHost host, string? refreshCookie, bool withHeader = true)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");

        if (refreshCookie is not null)
        {
            request.Headers.Add("Cookie", CookieName + "=" + refreshCookie);
        }

        if (withHeader)
        {
            request.Headers.Add("X-Custode-Request", "1");
        }

        return host.Client.SendAsync(request);
    }

    public static Task<HttpResponseMessage> SignOutAsync(CustodeTestHost host, string? accessToken, string? refreshCookie)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/sign-out");
        request.Headers.Add("X-Custode-Request", "1");

        if (refreshCookie is not null)
        {
            request.Headers.Add("Cookie", CookieName + "=" + refreshCookie);
        }

        if (accessToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        return host.Client.SendAsync(request);
    }

    public static Task<HttpResponseMessage> GetProtectedAsync(CustodeTestHost host, string accessToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/tests/protected")
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", accessToken) },
        };

        return host.Client.SendAsync(request);
    }

    public static async Task<bool> AuditContainsAsync(CustodeTestHost host, Guid tenantId, string action, string outcome = "succeeded")
    {
        await using var db = host.CreateTenantContext(tenantId);

        return await db.AuditEntries.AnyAsync(entry => entry.Action == action && entry.Outcome == outcome);
    }

    public static async Task<Custode.Infrastructure.Sessions.Session> SessionOfAsync(CustodeTestHost host, Guid tenantId, Guid userId)
    {
        await using var db = host.CreateTenantContext(tenantId);

        var sessions = await db.Sessions.Where(session => session.UserId == userId).ToListAsync();

        return sessions.OrderByDescending(session => session.StartedAtUtc).First();
    }

    private sealed record SignInBody(string AccessToken, DateTimeOffset ExpiresAt);
}
