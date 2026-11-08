// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

namespace Custode.Infrastructure.Access;

public interface IFolderAccess
{
    Task<FolderPermissions> GetEffectivePermissionsAsync(Guid folderId, Guid userId, CancellationToken cancellationToken);

    // Computes the user's effective permissions on every folder of the tenant with a fixed number of queries.
    Task<IReadOnlyDictionary<Guid, FolderPermissions>> GetEffectivePermissionsForAllFoldersAsync(Guid userId, CancellationToken cancellationToken);
}
