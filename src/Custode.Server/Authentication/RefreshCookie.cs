// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Microsoft.AspNetCore.Http;

namespace Custode.Server.Authentication;

public static class RefreshCookie
{
    public const string Name = "custode_refresh";

    // The path is restricted to the authentication endpoints, so the cookie is sent nowhere else.
    private const string Path = "/api/auth";

    public static void Write(HttpContext context, string refreshToken, DateTimeOffset expiresAtUtc)
    {
        context.Response.Cookies.Append(Name, refreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = Path,
            Expires = expiresAtUtc,
        });
    }

    public static void Clear(HttpContext context)
    {
        context.Response.Cookies.Delete(Name, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = Path,
        });
    }
}
