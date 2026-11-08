// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Custode.Infrastructure.Identity;
using Custode.Infrastructure.Authentication;
using Custode.Infrastructure.Sessions;
using Custode.Server.Authentication;
using Microsoft.AspNetCore.Identity;

namespace Custode.Server.Features.Authentication.Sessions;

public sealed class RefreshSessionHandler(
    SessionService sessions,
    UserManager<ApplicationUser> userManager,
    ITokenIssuer tokenIssuer)
{
    public async Task<IssuedSession?> HandleAsync(string? refreshToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(refreshToken))
        {
            return null;
        }

        var refreshed = await sessions.RefreshAsync(refreshToken, cancellationToken);

        if (refreshed is null)
        {
            return null;
        }

        var user = await userManager.FindByIdAsync(refreshed.UserId.ToString());

        if (user is null)
        {
            return null;
        }

        var accessToken = await tokenIssuer.IssueAsync(user, refreshed.SessionId, cancellationToken);

        return new IssuedSession(accessToken, refreshed.RefreshToken, refreshed.RefreshExpiresAtUtc);
    }
}
