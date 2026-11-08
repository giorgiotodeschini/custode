// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Custode.Infrastructure.Authentication;
using Custode.Infrastructure.Sessions;
using Custode.Server.Authentication;
using Microsoft.AspNetCore.Http;

namespace Custode.Server.Features.Authentication.Sessions;

public sealed class SignOutHandler(SessionService sessions)
{
    public async Task HandleAsync(HttpContext context, CancellationToken cancellationToken)
    {
        var refreshToken = context.Request.Cookies[RefreshCookie.Name];

        if (!string.IsNullOrEmpty(refreshToken))
        {
            await sessions.SignOutAsync(refreshToken, cancellationToken);
        }

        // An access token that is still valid names its session too, so it stops working even without the cookie.
        if (context.User.FindFirst(JwtTokenIssuer.SessionClaim)?.Value is { } sessionValue
            && context.User.FindFirst(JwtTokenIssuer.TenantIdClaim)?.Value is { } tenantValue
            && context.User.FindFirst(JwtTokenIssuer.SubjectClaim)?.Value is { } subjectValue
            && Guid.TryParse(sessionValue, out var sessionId)
            && Guid.TryParse(tenantValue, out var tenantId)
            && Guid.TryParse(subjectValue, out var userId))
        {
            await sessions.RevokeAsync(tenantId, sessionId, SessionService.SignedOutReason, userId, cancellationToken);
        }
    }
}
