// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Microsoft.AspNetCore.Http;

namespace Custode.Server.Authentication;

// A user flagged to change the password can use only the password change and sign-out endpoints.
public sealed class PasswordChangeRequirementMiddleware(RequestDelegate next)
{
    public const string ClaimType = "password_change_required";

    private static readonly string[] AllowedPaths =
    [
        "/api/auth/change-password",
        "/api/auth/sign-out",
    ];

    public async Task InvokeAsync(HttpContext context)
    {
        var restricted = context.User.HasClaim(ClaimType, "true");

        if (restricted && !IsAllowed(context.Request))
        {
            await Results.Problem(
                    detail: "Password change required.",
                    statusCode: StatusCodes.Status403Forbidden,
                    extensions: new Dictionary<string, object?> { ["reason"] = "password change required" })
                .ExecuteAsync(context);
            return;
        }

        await next(context);
    }

    private static bool IsAllowed(HttpRequest request)
    {
        return request.Method == HttpMethods.Post
            && AllowedPaths.Contains(request.Path.Value, StringComparer.OrdinalIgnoreCase);
    }
}
