// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Microsoft.AspNetCore.Http;

namespace Custode.Server.Authentication;

// A cross-origin page cannot send this header without a CORS preflight, which the server does not allow.
public static class CustodeRequestHeader
{
    public const string Name = "X-Custode-Request";

    public static bool IsPresent(HttpRequest request)
    {
        return request.Headers.ContainsKey(Name);
    }
}
