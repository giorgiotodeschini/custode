// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Custode.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Custode.Server.Features.SecurityAudit;

public sealed class SecurityAuditSlice : IFeatureSlice
{
    public void RegisterServices(IServiceCollection services)
    {
        services.AddScoped<QueryAuditLogHandler>();
        services.AddScoped<VerifyAuditLogHandler>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var audit = endpoints.MapGroup("/api/audit")
            .RequireAuthorization(TenantPolicies.TenantAdministrator);

        audit.MapGet("/", async (
            DateTimeOffset? from,
            DateTimeOffset? to,
            Guid? userId,
            string? action,
            string? outcome,
            QueryAuditLogHandler handler,
            CancellationToken cancellationToken) =>
            Results.Ok(await handler.HandleAsync(from, to, userId, action, outcome, cancellationToken)));

        audit.MapGet("/verify", async (VerifyAuditLogHandler handler, CancellationToken cancellationToken) =>
            Results.Ok(await handler.HandleAsync(cancellationToken)));
    }
}
