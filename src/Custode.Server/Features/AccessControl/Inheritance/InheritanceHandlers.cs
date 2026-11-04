// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Custode.Infrastructure.Audit;
using Custode.Infrastructure.Identity;
using Custode.Infrastructure.Persistence;
using Custode.Infrastructure.Vault;
using Custode.Server.Features.AccessControl.Common;

namespace Custode.Server.Features.AccessControl.Inheritance;

internal sealed class BreakInheritanceHandler(AppDbContext dbContext, ICurrentUser currentUser, IAuditTrail auditTrail)
{
    public async Task HandleAsync(string? folderPath, CancellationToken cancellationToken)
    {
        var folder = await FolderLookup.RequireFolderAsync(dbContext, folderPath, cancellationToken);

        if (!folder.InheritsPermissions)
        {
            throw new VaultConflictException("Inheritance is already broken on this folder.");
        }

        await AuditedWork.CommitAsync(dbContext, async () =>
        {
            folder.InheritsPermissions = false;
            await auditTrail.RecordAsync(
                new AuditRecord(currentUser.Id, AuditActions.InheritanceBroken, "folder", folder.Id.ToString("D")),
                cancellationToken);
            return true;
        }, cancellationToken);
    }
}

internal sealed class RestoreInheritanceHandler(AppDbContext dbContext, ICurrentUser currentUser, IAuditTrail auditTrail)
{
    public async Task HandleAsync(string? folderPath, CancellationToken cancellationToken)
    {
        var folder = await FolderLookup.RequireFolderAsync(dbContext, folderPath, cancellationToken);

        if (folder.InheritsPermissions)
        {
            throw new VaultConflictException("Inheritance is not broken on this folder.");
        }

        await AuditedWork.CommitAsync(dbContext, async () =>
        {
            folder.InheritsPermissions = true;
            await auditTrail.RecordAsync(
                new AuditRecord(currentUser.Id, AuditActions.InheritanceRestored, "folder", folder.Id.ToString("D")),
                cancellationToken);
            return true;
        }, cancellationToken);
    }
}
