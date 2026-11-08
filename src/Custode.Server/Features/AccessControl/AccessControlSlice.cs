// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Custode.Server.Authorization;
using Custode.Infrastructure.Audit;
using Custode.Infrastructure.Tenancy;
using Custode.Server.Features.AccessControl.Common;
using Custode.Server.Features.AccessControl.Grants;
using Custode.Server.Features.AccessControl.Groups;
using Custode.Server.Features.AccessControl.Inheritance;
using Custode.Server.Features.AccessControl.Memberships;
using Custode.Server.Features.AccessControl.Reads;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Custode.Server.Features.AccessControl;

public sealed class AccessControlSlice : IFeatureSlice
{
    public void RegisterServices(IServiceCollection services)
    {
        services.AddScoped<TenantUserLookup>();
        services.AddScoped<CreateGroupHandler>();
        services.AddScoped<RenameGroupHandler>();
        services.AddScoped<DeleteGroupHandler>();
        services.AddScoped<AddMemberHandler>();
        services.AddScoped<RemoveMemberHandler>();
        services.AddScoped<SetGrantHandler>();
        services.AddScoped<RevokeGrantHandler>();
        services.AddScoped<BreakInheritanceHandler>();
        services.AddScoped<RestoreInheritanceHandler>();
        services.AddScoped<ListGroupsReadHandler>();
        services.AddScoped<ListGroupMembersReadHandler>();
        services.AddScoped<GrantsReadHandler>();
        services.AddScoped<FolderInheritanceReadHandler>();
        services.AddScoped<CallerPermissionsReadHandler>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        // Authentication comes from the fallback policy; tenant administration is enforced per endpoint by
        // TenantAdministratorFilter, which records denied attempts in the audit log.
        var access = endpoints.MapGroup("/api/access");

        // Reads of tenant structure are limited to administrators; the caller's own permissions are open to any member.
        access.MapGet("/groups", async (ListGroupsReadHandler handler, CancellationToken cancellationToken) =>
                Results.Ok(await handler.HandleAsync(cancellationToken)))
            .RequireAuthorization(TenantPolicies.TenantAdministrator);

        access.MapGet("/groups/{id:guid}/members", async (Guid id, ListGroupMembersReadHandler handler, CancellationToken cancellationToken) =>
                Results.Ok(await handler.HandleAsync(id, cancellationToken)))
            .RequireAuthorization(TenantPolicies.TenantAdministrator);

        access.MapGet("/grants", async (string? folderPath, GrantsReadHandler handler, CancellationToken cancellationToken) =>
                Results.Ok(await handler.HandleAsync(folderPath, cancellationToken)))
            .RequireAuthorization(TenantPolicies.TenantAdministrator);

        access.MapGet("/folders", async (FolderInheritanceReadHandler handler, CancellationToken cancellationToken) =>
                Results.Ok(await handler.HandleAsync(cancellationToken)))
            .RequireAuthorization(TenantPolicies.TenantAdministrator);

        access.MapGet("/me/permissions", async (string? folderPath, CallerPermissionsReadHandler handler, CancellationToken cancellationToken) =>
                Results.Ok(await handler.HandleAsync(folderPath, cancellationToken)))
            .RequireAuthorization();

        access.MapPost("/groups", async (GroupNameRequest request, CreateGroupHandler handler, CancellationToken cancellationToken) =>
                Results.Ok(await handler.HandleAsync(request, cancellationToken)))
            .AddEndpointFilter(new TenantAdministratorFilter(AuditActions.GroupCreated, "group"));

        access.MapPut("/groups/{id:guid}", async (Guid id, GroupNameRequest request, RenameGroupHandler handler, CancellationToken cancellationToken) =>
                Results.Ok(await handler.HandleAsync(id, request, cancellationToken)))
            .AddEndpointFilter(new TenantAdministratorFilter(AuditActions.GroupRenamed, "group"));

        access.MapDelete("/groups/{id:guid}", async (Guid id, DeleteGroupHandler handler, CancellationToken cancellationToken) =>
            {
                await handler.HandleAsync(id, cancellationToken);
                return Results.NoContent();
            })
            .AddEndpointFilter(new TenantAdministratorFilter(AuditActions.GroupDeleted, "group"));

        access.MapPost("/groups/{id:guid}/members", async (Guid id, AddMemberRequest request, AddMemberHandler handler, CancellationToken cancellationToken) =>
            {
                await handler.HandleAsync(id, request.UserId, cancellationToken);
                return Results.NoContent();
            })
            .AddEndpointFilter(new TenantAdministratorFilter(AuditActions.MembershipAdded, "group"));

        access.MapDelete("/groups/{id:guid}/members/{userId:guid}", async (Guid id, Guid userId, RemoveMemberHandler handler, CancellationToken cancellationToken) =>
            {
                await handler.HandleAsync(id, userId, cancellationToken);
                return Results.NoContent();
            })
            .AddEndpointFilter(new TenantAdministratorFilter(AuditActions.MembershipRemoved, "group"));

        access.MapPut("/grants", async (SetGrantRequest request, SetGrantHandler handler, CancellationToken cancellationToken) =>
            {
                await handler.HandleAsync(request, cancellationToken);
                return Results.NoContent();
            })
            .AddEndpointFilter(new TenantAdministratorFilter(AuditActions.GrantSet, "folder"));

        access.MapDelete("/grants", async (string? folderPath, string? principalKind, Guid principalId, RevokeGrantHandler handler, CancellationToken cancellationToken) =>
            {
                await handler.HandleAsync(folderPath, principalKind, principalId, cancellationToken);
                return Results.NoContent();
            })
            .AddEndpointFilter(new TenantAdministratorFilter(AuditActions.GrantRevoked, "folder"));

        access.MapPost("/inheritance/break", async (string? folderPath, BreakInheritanceHandler handler, CancellationToken cancellationToken) =>
            {
                await handler.HandleAsync(folderPath, cancellationToken);
                return Results.NoContent();
            })
            .AddEndpointFilter(new TenantAdministratorFilter(AuditActions.InheritanceBroken, "folder"));

        access.MapPost("/inheritance/restore", async (string? folderPath, RestoreInheritanceHandler handler, CancellationToken cancellationToken) =>
            {
                await handler.HandleAsync(folderPath, cancellationToken);
                return Results.NoContent();
            })
            .AddEndpointFilter(new TenantAdministratorFilter(AuditActions.InheritanceRestored, "folder"));
    }
}
