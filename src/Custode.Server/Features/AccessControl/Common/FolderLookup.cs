// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Custode.Infrastructure.Persistence;
using Custode.Infrastructure.Vault;
using Microsoft.EntityFrameworkCore;

namespace Custode.Server.Features.AccessControl.Common;

internal static class FolderLookup
{
    public static async Task<Folder> RequireFolderAsync(AppDbContext dbContext, string? path, CancellationToken cancellationToken)
    {
        var normalizedPath = FolderPath.Normalize(path);

        return await dbContext.Folders.FirstOrDefaultAsync(folder => folder.Path == normalizedPath, cancellationToken)
            ?? throw new VaultNotFoundException("The folder does not exist.");
    }
}
