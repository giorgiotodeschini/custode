// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Custode.Infrastructure.Vault;
using Microsoft.AspNetCore.Http;

namespace Custode.Server.Errors;

public sealed class VaultExceptionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (VaultException exception) when (!context.Response.HasStarted)
        {
            await Results.Problem(detail: exception.Message, statusCode: exception.StatusCode).ExecuteAsync(context);
        }
        catch (UnauthorizedAccessException) when (!context.Response.HasStarted)
        {
            await Results.Unauthorized().ExecuteAsync(context);
        }
    }
}
