// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Custode.Infrastructure.Audit;
using Custode.Infrastructure.Identity;
using Custode.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Custode.Server.Authorization;

// Authorizes access-control changes inside the endpoint instead of with a policy, so that a denied
// attempt can be recorded in the audit log before the 403 response is returned.
public sealed class TenantAdministratorFilter(string action, string targetType) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var services = httpContext.RequestServices;

        var authorization = await services.GetRequiredService<IAuthorizationService>()
            .AuthorizeAsync(httpContext.User, null, TenantPolicies.TenantAdministrator);

        if (authorization.Succeeded)
        {
            return await next(context);
        }

        var targetId = httpContext.Request.RouteValues.TryGetValue("id", out var routeId) ? routeId?.ToString() : null;

        await services.GetRequiredService<IAuditTrail>().RecordDeniedAsync(
            new AuditRecord(services.GetRequiredService<ICurrentUser>().Id, action, targetType, targetId),
            httpContext.RequestAborted);

        return Results.Forbid();
    }
}
