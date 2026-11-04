// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Custode.Infrastructure.Access;
using Custode.Infrastructure.Audit;
using Custode.Infrastructure.Identity;
using Custode.Infrastructure.Persistence;
using Custode.Infrastructure.Tenancy;
using Custode.Infrastructure.Vault;
using Microsoft.EntityFrameworkCore;

namespace Custode.Server.Features.SecretManagement.Common;

internal sealed class FolderGuard(
    AppDbContext dbContext,
    IFolderAccess folderAccess,
    IAuditTrail auditTrail,
    ICurrentUser currentUser,
    ITenantContext tenantContext)
{
    private const string NotVisibleMessage = "The requested folder or item does not exist.";

    public Guid TenantId => tenantContext.RequireTenantId();

    public Guid UserId => currentUser.Id;

    public async Task<Folder> GetFolderAsync(string normalizedPath, CancellationToken cancellationToken)
    {
        return await dbContext.Folders
                   .FirstOrDefaultAsync(folder => folder.Path == normalizedPath, cancellationToken)
               ?? throw new VaultNotFoundException(NotVisibleMessage);
    }

    public async Task<FolderPermissions> AuthorizeAsync(
        Guid folderId,
        FolderPermissions required,
        string? deniedAction = null,
        string? targetType = null,
        string? targetId = null,
        string? secondaryTargetType = null,
        string? secondaryTargetId = null,
        CancellationToken cancellationToken = default)
    {
        var permissions = await folderAccess.GetEffectivePermissionsAsync(folderId, UserId, cancellationToken);

        if (permissions == FolderPermissions.None)
        {
            await RecordDenialAsync(deniedAction, targetType, targetId, secondaryTargetType, secondaryTargetId, cancellationToken);
            throw new VaultNotFoundException(NotVisibleMessage);
        }

        if (!permissions.HasFlag(required))
        {
            await RecordDenialAsync(deniedAction, targetType, targetId, secondaryTargetType, secondaryTargetId, cancellationToken);
            throw new VaultForbiddenException($"The operation requires {required} permission on this folder.");
        }

        return permissions;
    }

    private async Task RecordDenialAsync(
        string? action,
        string? targetType,
        string? targetId,
        string? secondaryTargetType,
        string? secondaryTargetId,
        CancellationToken cancellationToken)
    {
        if (action is null)
        {
            return;
        }

        await auditTrail.RecordDeniedAsync(
            new AuditRecord(UserId, action, targetType, targetId, secondaryTargetType, secondaryTargetId),
            cancellationToken);
    }
}
