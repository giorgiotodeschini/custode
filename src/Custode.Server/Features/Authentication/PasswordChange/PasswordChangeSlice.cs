// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using System.Security.Claims;
using Custode.Infrastructure.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Custode.Server.Features.Authentication.PasswordChange;

public sealed class PasswordChangeSlice : IFeatureSlice
{
    public void RegisterServices(IServiceCollection services)
    {
        services.AddScoped<ChangePasswordHandler>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/auth/change-password", async (
            HttpContext context,
            ChangePasswordRequest request,
            ChangePasswordHandler handler,
            CancellationToken cancellationToken) =>
        {
            var sessionValue = context.User.FindFirst(JwtTokenIssuer.SessionClaim)?.Value;
            var currentSessionId = Guid.TryParse(sessionValue, out var sessionId) ? sessionId : (Guid?)null;

            var failures = await handler.HandleAsync(request, currentSessionId, cancellationToken);

            return failures.Count == 0
                ? Results.NoContent()
                : Results.BadRequest(new { errors = failures });
        })
        .RequireAuthorization();
    }
}
