// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Custode.Server.Features.SecretManagement.Common;
using Custode.Server.Features.SecretManagement.Folders;
using Custode.Server.Features.SecretManagement.Secrets;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Custode.Server.Features.SecretManagement;

public sealed class SecretManagementSlice : IFeatureSlice
{
    public void RegisterServices(IServiceCollection services)
    {
        services.AddScoped<FolderGuard>();
        services.AddScoped<SecretValueCodec>();
        services.AddScoped<CreateFolderHandler>();
        services.AddScoped<ListFoldersHandler>();
        services.AddScoped<DeleteFolderHandler>();
        services.AddScoped<CreateSecretHandler>();
        services.AddScoped<ListSecretsHandler>();
        services.AddScoped<ReadSecretValueHandler>();
        services.AddScoped<UpdateSecretHandler>();
        services.AddScoped<DeleteSecretHandler>();
        services.AddScoped<MoveSecretHandler>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var folders = endpoints.MapGroup("/api/folders").RequireAuthorization();

        folders.MapPost("/", async (CreateFolderRequest request, CreateFolderHandler handler, CancellationToken cancellationToken) =>
            Results.Ok(await handler.HandleAsync(request, cancellationToken)));

        folders.MapGet("/", async (string? path, ListFoldersHandler handler, CancellationToken cancellationToken) =>
            Results.Ok(await handler.HandleAsync(path, cancellationToken)));

        folders.MapDelete("/", async (string? path, DeleteFolderHandler handler, CancellationToken cancellationToken) =>
        {
            await handler.HandleAsync(path, cancellationToken);
            return Results.NoContent();
        });

        var secrets = endpoints.MapGroup("/api/secrets").RequireAuthorization();

        secrets.MapPost("/", async (CreateSecretRequest request, CreateSecretHandler handler, CancellationToken cancellationToken) =>
            Results.Ok(await handler.HandleAsync(request, cancellationToken)));

        secrets.MapGet("/", async (string? folderPath, ListSecretsHandler handler, CancellationToken cancellationToken) =>
            Results.Ok(await handler.HandleAsync(folderPath, cancellationToken)));

        secrets.MapGet("/{id:guid}/value", async (Guid id, ReadSecretValueHandler handler, CancellationToken cancellationToken) =>
            Results.Ok(await handler.HandleAsync(id, cancellationToken)));

        secrets.MapPut("/{id:guid}", async (Guid id, SecretWriteRequest request, UpdateSecretHandler handler, CancellationToken cancellationToken) =>
            Results.Ok(await handler.HandleAsync(id, request, cancellationToken)));

        secrets.MapDelete("/{id:guid}", async (Guid id, DeleteSecretHandler handler, CancellationToken cancellationToken) =>
        {
            await handler.HandleAsync(id, cancellationToken);
            return Results.NoContent();
        });

        secrets.MapPost("/{id:guid}/move", async (Guid id, MoveSecretRequest request, MoveSecretHandler handler, CancellationToken cancellationToken) =>
            Results.Ok(await handler.HandleAsync(id, request, cancellationToken)));
    }
}
