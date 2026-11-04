// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Custode.Infrastructure.Access;
using Custode.Infrastructure.Persistence;
using Custode.Infrastructure.Vault;
using Custode.Server.Features.SecretManagement.Common;
using Microsoft.EntityFrameworkCore;

namespace Custode.Server.Features.SecretManagement.Secrets;

internal sealed class ListSecretsHandler(AppDbContext dbContext, FolderGuard folderGuard)
{
    public async Task<IReadOnlyList<SecretMetadataResponse>> HandleAsync(string? folderPath, CancellationToken cancellationToken)
    {
        var normalizedPath = FolderPath.Normalize(folderPath);
        var folder = await folderGuard.GetFolderAsync(normalizedPath, cancellationToken);

        await folderGuard.AuthorizeAsync(folder.Id, FolderPermissions.Read, cancellationToken: cancellationToken);

        var secrets = await dbContext.Secrets
            .AsNoTracking()
            .Where(secret => secret.FolderId == folder.Id)
            .OrderBy(secret => secret.Name)
            .ToListAsync(cancellationToken);

        return secrets.Select(secret => SecretMetadataMapper.ToResponse(secret, normalizedPath)).ToList();
    }
}
