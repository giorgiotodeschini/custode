// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Custode.Infrastructure.Authentication;
using Custode.Infrastructure.Identity;
using Custode.Infrastructure.Sessions;
using Custode.Server.Authentication;
using Microsoft.AspNetCore.Identity;

namespace Custode.Server.Features.Authentication.SignIn;

public sealed class SignInHandler(
    UserManager<ApplicationUser> userManager,
    SessionService sessions,
    ITokenIssuer tokenIssuer)
{
    public async Task<IssuedSession?> HandleAsync(SignInRequest request, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByNameAsync(request.Login);

        // A disabled account gets the same generic failure as an unknown login.
        if (user is null || !await userManager.CheckPasswordAsync(user, request.Password) || !user.IsEnabled)
        {
            return null;
        }

        var session = await sessions.StartAsync(user, cancellationToken);
        var accessToken = await tokenIssuer.IssueAsync(user, session.SessionId, cancellationToken);

        return new IssuedSession(accessToken, session.RefreshToken, session.RefreshExpiresAtUtc);
    }
}
