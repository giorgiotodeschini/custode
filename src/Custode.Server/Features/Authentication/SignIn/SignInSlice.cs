// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Custode.Server.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Custode.Server.Features.Authentication.SignIn;

public sealed class SignInSlice : IFeatureSlice
{
    public void RegisterServices(IServiceCollection services)
    {
        services.AddScoped<SignInHandler>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/auth/sign-in", async (HttpContext context, SignInRequest request, SignInHandler handler, CancellationToken cancellationToken) =>
        {
            var issued = await handler.HandleAsync(request, cancellationToken);

            if (issued is null)
            {
                return Results.Unauthorized();
            }

            RefreshCookie.Write(context, issued.RefreshToken, issued.RefreshExpiresAtUtc);

            return Results.Ok(new SignInResponse(issued.AccessToken.AccessToken, issued.AccessToken.ExpiresAt));
        })
        .AllowAnonymous();
    }
}
