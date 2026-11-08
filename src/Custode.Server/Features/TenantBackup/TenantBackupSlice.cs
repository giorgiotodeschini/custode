// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Custode.Infrastructure.Audit;
using Custode.Server.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Custode.Server.Features.TenantBackup;

public sealed class TenantBackupSlice : IFeatureSlice
{
    public void RegisterServices(IServiceCollection services)
    {
        services.AddScoped<CreateBackupHandler>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/backups", async (BackupRequest request, HttpContext context, CreateBackupHandler handler, CancellationToken cancellationToken) =>
            {
                var file = await handler.HandleAsync(request, cancellationToken);

                context.Response.Headers.CacheControl = "no-store";

                return Results.File(file.Content, "application/octet-stream", file.FileName);
            })
            // Authentication only: the filter enforces the administrator role and records a refused attempt, which a
            // policy-level rejection would skip.
            .RequireAuthorization()
            .AddEndpointFilter(new TenantAdministratorFilter(AuditActions.TenantBackupExported, "tenant"));
    }
}
