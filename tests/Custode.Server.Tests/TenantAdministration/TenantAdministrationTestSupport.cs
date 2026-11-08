// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using System.Net.Http.Headers;
using System.Net.Http.Json;
using Custode.Infrastructure.Audit;
using Custode.Infrastructure.Identity;
using Custode.Infrastructure.Persistence;
using Custode.Server.Tests.Hosting;
using Custode.Server.Tests.Sessions;
using Microsoft.EntityFrameworkCore;

namespace Custode.Server.Tests.TenantAdministration;

public static class TenantAdministrationTestSupport
{
    public const string AdministratorRole = "TenantAdministrator";

    public static async Task<(ApplicationUser User, string Token)> SignedInAsync(CustodeTestHost host, Guid tenantId, params string[] roles)
    {
        var user = await SessionTestSupport.CreateUserAsync(host, tenantId, roles);
        var (_, token, _) = await SessionTestSupport.SignInTokensAsync(host, user.UserName!);

        return (user, token!);
    }

    public static async Task<HttpResponseMessage> SendAsync(CustodeTestHost host, HttpMethod method, string url, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url)
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) },
        };

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await host.Client.SendAsync(request);
    }

    public static async Task<bool> AuditContainsAsync(CustodeTestHost host, Guid tenantId, string action, string outcome = "succeeded", string? targetId = null)
    {
        await using var db = host.CreateTenantContext(tenantId);

        return await db.AuditEntries.AnyAsync(entry =>
            entry.Action == action && entry.Outcome == outcome && (targetId == null || entry.TargetId == targetId));
    }
}
