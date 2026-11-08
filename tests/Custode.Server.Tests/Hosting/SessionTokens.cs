// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Custode.Infrastructure.Authentication;
using Custode.Infrastructure.Identity;
using Custode.Infrastructure.Sessions;
using Custode.Server.Tests.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Custode.Server.Tests.Hosting;

// Creates the server-side session that every authenticated token must reference.
public static class SessionTokens
{
    public static async Task<Guid> StartSessionAsync(IServiceProvider services, ApplicationUser user)
    {
        using var scope = services.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<SessionService>();

        var started = await sessions.StartAsync(user, CancellationToken.None);

        return started.SessionId;
    }

    public static async Task<string> IssueAsync(IServiceProvider services, ApplicationUser user)
    {
        using var scope = services.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<SessionService>();
        var issuer = scope.ServiceProvider.GetRequiredService<ITokenIssuer>();

        var started = await sessions.StartAsync(user, CancellationToken.None);
        var issued = await issuer.IssueAsync(user, started.SessionId);

        return issued.AccessToken;
    }

    public static async Task<string> IssueForNewUserAsync(IServiceProvider services, Guid tenantId, params string[] roles)
    {
        using var scope = services.CreateScope();
        var user = await IdentityTestServices.CreateUserAsync(scope.ServiceProvider, "user-" + Guid.NewGuid().ToString("N"), tenantId, roles);

        return await IssueAsync(services, user);
    }
}
