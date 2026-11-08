// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Custode.Server.Features.SecretManagement.Common;
using Custode.Server.Features.SecretManagement.Folders;
using Custode.Server.Features.SecretManagement.Import;
using Custode.Server.Features.SecretManagement.Search;
using Custode.Server.Features.SecretManagement.Secrets;
using Custode.Server.Features.SecretManagement.Versions;
using Microsoft.AspNetCore.Http;
using Custode.Infrastructure.Vault;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
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
        services.AddScoped<ListVersionsHandler>();
        services.AddScoped<ReadVersionValueHandler>();
        services.AddScoped<RollbackSecretHandler>();
        services.AddScoped<SearchHandler>();
        services.AddScoped<ImportHandler>();
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

        endpoints.MapGet("/api/search", async (string? query, int? limit, SearchHandler handler, CancellationToken cancellationToken) =>
                Results.Ok(await handler.HandleAsync(query, limit, cancellationToken)))
            .RequireAuthorization();

        var secrets = endpoints.MapGroup("/api/secrets").RequireAuthorization();

        secrets.MapPost("/", async (CreateSecretRequest request, CreateSecretHandler handler, CancellationToken cancellationToken) =>
            Results.Ok(await handler.HandleAsync(request, cancellationToken)));

        secrets.MapPost("/imports", async (bool? dryRun, ImportRequest request, HttpContext context, IOptions<SecretStorageOptions> storage, ImportHandler handler, CancellationToken cancellationToken) =>
        {
            if (context.Request.ContentLength is { } length && length > storage.Value.MaxImportBodyBytes)
            {
                return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            }

            var outcome = await handler.HandleAsync(request, dryRun ?? false, cancellationToken);

            return Results.Json(outcome.Body, statusCode: outcome.StatusCode);
        });

        secrets.MapGet("/", async (string? folderPath, ListSecretsHandler handler, CancellationToken cancellationToken) =>
            Results.Ok(await handler.HandleAsync(folderPath, cancellationToken)));

        secrets.MapGet("/{id:guid}/value", async (Guid id, HttpContext context, ReadSecretValueHandler handler, CancellationToken cancellationToken) =>
        {
            var value = await handler.HandleAsync(id, cancellationToken);
            context.Response.Headers.ETag = VersionTokens.Format(value.Version);

            return Results.Ok(value);
        });

        secrets.MapPut("/{id:guid}", async (Guid id, SecretWriteRequest request, HttpContext context, UpdateSecretHandler handler, CancellationToken cancellationToken) =>
        {
            var updated = await handler.HandleAsync(id, request, context.Request.Headers.IfMatch.ToString(), cancellationToken);
            context.Response.Headers.ETag = VersionTokens.Format(updated.Version);

            return Results.Ok(updated);
        });

        secrets.MapGet("/{id:guid}/versions", async (Guid id, ListVersionsHandler handler, CancellationToken cancellationToken) =>
            Results.Ok(await handler.HandleAsync(id, cancellationToken)));

        secrets.MapGet("/{id:guid}/versions/{number:int}/value", async (Guid id, int number, ReadVersionValueHandler handler, CancellationToken cancellationToken) =>
            Results.Ok(await handler.HandleAsync(id, number, cancellationToken)));

        secrets.MapPost("/{id:guid}/versions/{number:int}/rollback", async (Guid id, int number, HttpContext context, RollbackSecretHandler handler, CancellationToken cancellationToken) =>
        {
            var rolledBack = await handler.HandleAsync(id, number, context.Request.Headers.IfMatch.ToString(), cancellationToken);
            context.Response.Headers.ETag = VersionTokens.Format(rolledBack.Version);

            return Results.Ok(rolledBack);
        });

        secrets.MapDelete("/{id:guid}", async (Guid id, DeleteSecretHandler handler, CancellationToken cancellationToken) =>
        {
            await handler.HandleAsync(id, cancellationToken);
            return Results.NoContent();
        });

        secrets.MapPost("/{id:guid}/move", async (Guid id, MoveSecretRequest request, MoveSecretHandler handler, CancellationToken cancellationToken) =>
            Results.Ok(await handler.HandleAsync(id, request, cancellationToken)));
    }
}
