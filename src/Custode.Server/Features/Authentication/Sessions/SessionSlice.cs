// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Custode.Server.Authentication;
using Custode.Server.Features.Authentication.SignIn;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Custode.Server.Features.Authentication.Sessions;

public sealed class SessionSlice : IFeatureSlice
{
    public void RegisterServices(IServiceCollection services)
    {
        services.AddScoped<RefreshSessionHandler>();
        services.AddScoped<SignOutHandler>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/auth/refresh", async (HttpContext context, RefreshSessionHandler handler, CancellationToken cancellationToken) =>
        {
            if (!CustodeRequestHeader.IsPresent(context.Request))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            var issued = await handler.HandleAsync(context.Request.Cookies[RefreshCookie.Name], cancellationToken);

            // The cookie is left as it is on failure: another tab may already hold its replacement.
            if (issued is null)
            {
                return Results.Unauthorized();
            }

            RefreshCookie.Write(context, issued.RefreshToken, issued.RefreshExpiresAtUtc);

            return Results.Ok(new SignInResponse(issued.AccessToken.AccessToken, issued.AccessToken.ExpiresAt));
        })
        .AllowAnonymous();

        endpoints.MapPost("/api/auth/sign-out", async (HttpContext context, SignOutHandler handler, CancellationToken cancellationToken) =>
        {
            if (!CustodeRequestHeader.IsPresent(context.Request))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            await handler.HandleAsync(context, cancellationToken);
            RefreshCookie.Clear(context);

            return Results.NoContent();
        })
        .AllowAnonymous();

        // Activity is recorded by the session check that runs for every authenticated request.
        endpoints.MapPost("/api/auth/activity", (HttpContext context) =>
            CustodeRequestHeader.IsPresent(context.Request) ? Results.NoContent() : Results.StatusCode(StatusCodes.Status403Forbidden))
            .RequireAuthorization();
    }
}
