// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Custode.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Custode.Server.Features.TenantProvisioning;

// Tenant-independent operations: they act on a tenant other than the operator's own,
// so they are restricted to the Operator policy (see the tenant-isolation spec).
public sealed class TenantProvisioningSlice : IFeatureSlice
{
    public void RegisterServices(IServiceCollection services)
    {
        services.AddScoped<InitializeVaultHandler>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var operations = endpoints.MapGroup("/api/operator/tenants")
            .RequireAuthorization(TenantPolicies.Operator);

        operations.MapPost("/{tenantId:guid}/vault", async (
            Guid tenantId,
            InitializeVaultRequest request,
            InitializeVaultHandler handler,
            CancellationToken cancellationToken) =>
            Results.Ok(await handler.HandleAsync(tenantId, request.AdministratorUserId, cancellationToken)));
    }
}
