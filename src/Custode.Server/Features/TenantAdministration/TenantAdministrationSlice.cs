// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using System.Security.Claims;
using Custode.Infrastructure.Audit;
using Custode.Infrastructure.Tenancy;
using Custode.Server.Authorization;
using Custode.Server.Features.TenantAdministration.Members;
using Custode.Server.Features.TenantAdministration.Policies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Custode.Server.Features.TenantAdministration;

public sealed class TenantAdministrationSlice : IFeatureSlice
{
    public void RegisterServices(IServiceCollection services)
    {
        services.AddScoped<ListMembersHandler>();
        services.AddScoped<CreateMemberHandler>();
        services.AddScoped<SetMemberEnabledHandler>();
        services.AddScoped<SetAdministratorHandler>();
        services.AddScoped<GetPolicyHandler>();
        services.AddScoped<SetPolicyHandler>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var tenant = endpoints.MapGroup("/api/tenant");

        tenant.MapGet("/members", async (ListMembersHandler handler, CancellationToken cancellationToken) =>
                Results.Ok(await handler.HandleAsync(cancellationToken)))
            .RequireAuthorization(TenantPolicies.TenantAdministrator);

        tenant.MapPost("/members", async (CreateMemberRequest request, CreateMemberHandler handler, CancellationToken cancellationToken) =>
            {
                var created = await handler.HandleAsync(request, cancellationToken);

                return Results.Created($"/api/tenant/members/{created.Id:D}", created);
            })
            .AddEndpointFilter(new TenantAdministratorFilter(AuditActions.MemberCreated, "member"));

        tenant.MapPost("/members/{id:guid}/disable", async (Guid id, SetMemberEnabledHandler handler, CancellationToken cancellationToken) =>
            {
                await handler.HandleAsync(id, enabled: false, cancellationToken);
                return Results.NoContent();
            })
            .AddEndpointFilter(new TenantAdministratorFilter(AuditActions.MemberDisabled, "member"));

        tenant.MapPost("/members/{id:guid}/enable", async (Guid id, SetMemberEnabledHandler handler, CancellationToken cancellationToken) =>
            {
                await handler.HandleAsync(id, enabled: true, cancellationToken);
                return Results.NoContent();
            })
            .AddEndpointFilter(new TenantAdministratorFilter(AuditActions.MemberEnabled, "member"));

        tenant.MapPut("/members/{id:guid}/administrator", async (Guid id, SetAdministratorRequest request, SetAdministratorHandler handler, CancellationToken cancellationToken) =>
            {
                await handler.HandleAsync(id, request, cancellationToken);
                return Results.NoContent();
            })
            .AddEndpointFilter(new TenantAdministratorFilter(AuditActions.AdministratorRoleChanged, "member"));

        tenant.MapGet("/policies", async (GetPolicyHandler handler, CancellationToken cancellationToken) =>
                Results.Ok(await handler.HandleAsync(cancellationToken)))
            .RequireAuthorization();

        tenant.MapPut("/policies", async (SetPolicyRequest request, SetPolicyHandler handler, CancellationToken cancellationToken) =>
            {
                var failures = await handler.HandleAsync(request, cancellationToken);

                return failures.Count == 0
                    ? Results.Ok(await handler.ReadAsync(cancellationToken))
                    : Results.BadRequest(new { errors = failures });
            })
            .AddEndpointFilter(new TenantAdministratorFilter(AuditActions.TenantPolicyChanged, "tenant policy"));
    }
}
