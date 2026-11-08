// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Custode.Infrastructure.Access;
using Custode.Infrastructure.Identity;
using Custode.Infrastructure.Persistence;
using Custode.Infrastructure.Vault;
using Custode.Server.Features.SecretManagement.Secrets;
using Microsoft.EntityFrameworkCore;

namespace Custode.Server.Features.SecretManagement.Search;

public sealed record SearchFolderHit(string Path);

public sealed record SearchSecretHit(Guid Id, string Name, string Type, string FolderPath);

public sealed record SearchResponse(IReadOnlyList<SearchFolderHit> Folders, IReadOnlyList<SearchSecretHit> Secrets, bool Truncated);

internal sealed class SearchHandler(AppDbContext dbContext, IFolderAccess folderAccess, ICurrentUser currentUser)
{
    public const int DefaultLimit = 20;
    public const int MaxLimit = 50;
    public const int MaxQueryLength = 100;
    public const int MaxCandidates = 5000;

    public async Task<SearchResponse> HandleAsync(string? rawQuery, int? limit, CancellationToken cancellationToken)
    {
        var query = rawQuery?.Trim() ?? string.Empty;

        if (query.Length is 0 or > MaxQueryLength)
        {
            throw new InvalidVaultRequestException($"The search query must be between 1 and {MaxQueryLength} characters.");
        }

        var max = limit ?? DefaultLimit;

        if (max is < 1 or > MaxLimit)
        {
            throw new InvalidVaultRequestException($"The limit must be between 1 and {MaxLimit}.");
        }

        var userId = currentUser.Id;
        var permissions = await folderAccess.GetEffectivePermissionsForAllFoldersAsync(userId, cancellationToken);

        // Folders are visible with any permission; the caller learns only that a folder they can see exists.
        var folders = await dbContext.Folders
            .AsNoTracking()
            .OrderBy(folder => folder.Path)
            .Select(folder => new { folder.Id, folder.Path })
            .ToListAsync(cancellationToken);

        var folderHits = folders
            .Where(folder => permissions.TryGetValue(folder.Id, out var level) && level != FolderPermissions.None)
            .Where(folder => folder.Path.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Take(max)
            .Select(folder => new SearchFolderHit(folder.Path))
            .ToList();

        // At most MaxCandidates secrets are examined; one extra row reveals that the scan was cut short.
        var candidates = await (
            from secret in dbContext.Secrets.AsNoTracking()
            join folder in dbContext.Folders.AsNoTracking() on secret.FolderId equals folder.Id
            orderby secret.Name, folder.Path
            select new { secret.Id, secret.Name, secret.Type, secret.FolderId, FolderPath = folder.Path })
            .Take(MaxCandidates + 1)
            .ToListAsync(cancellationToken);

        var truncated = candidates.Count > MaxCandidates;

        var secretHits = candidates
            .Take(MaxCandidates)
            .Where(secret => secret.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Where(secret => permissions.TryGetValue(secret.FolderId, out var level) && level.HasFlag(FolderPermissions.Read))
            .Take(max)
            .Select(secret => new SearchSecretHit(secret.Id, secret.Name, SecretValueCodec.FormatType(secret.Type), secret.FolderPath))
            .ToList();

        return new SearchResponse(folderHits, secretHits, truncated);
    }
}
