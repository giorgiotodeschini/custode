// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Custode.Infrastructure.Audit;
using Custode.Infrastructure.Access;
using Custode.Infrastructure.Identity;
using Custode.Infrastructure.Persistence;
using Custode.Infrastructure.Tenancy;
using Custode.Infrastructure.Vault;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Custode.Server.Features.TenantProvisioning;

internal sealed class InitializeVaultHandler(
    DbContextOptions<AppDbContext> options,
    UserManager<ApplicationUser> userManager,
    ICurrentUser currentUser)
{
    public async Task<InitializeVaultResponse> HandleAsync(Guid tenantId, Guid administratorUserId, CancellationToken cancellationToken)
    {
        var administrator = await userManager.FindByIdAsync(administratorUserId.ToString());

        if (administrator is null || administrator.TenantId != tenantId)
        {
            throw new VaultNotFoundException("The administrator does not exist in the requested tenant.");
        }

        // The operator belongs to another tenant, so the vault is written under the target tenant's scope.
        var tenantScope = new FixedTenantContext(tenantId);
        await using var db = new AppDbContext(options, tenantScope);
        var trail = new AuditTrail(db, tenantScope);

        if (await db.Folders.AnyAsync(folder => folder.Path == FolderPath.Root, cancellationToken))
        {
            throw new VaultConflictException("The vault of this tenant is already initialized.");
        }

        var root = new Folder
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Path = FolderPath.Root,
            ParentId = null,
            InheritsPermissions = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };

        var bootstrapGrant = new FolderGrant
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            FolderId = root.Id,
            PrincipalKind = PrincipalKind.User,
            PrincipalId = administrator.Id,
            Permissions = FolderPermissions.Read | FolderPermissions.Write | FolderPermissions.Create | FolderPermissions.Delete,
        };

        var operatorId = currentUser.Id;

        await AuditedWork.CommitAsync(db, async () =>
        {
            db.Folders.Add(root);
            db.FolderGrants.Add(bootstrapGrant);

            await trail.RecordAsync(
                new AuditRecord(operatorId, AuditActions.VaultInitialized, "folder", root.Id.ToString("D"), "user", administrator.Id.ToString("D")),
                cancellationToken);
            await trail.RecordAsync(
                new AuditRecord(operatorId, AuditActions.GrantSet, "folder", root.Id.ToString("D"), "user", administrator.Id.ToString("D")),
                cancellationToken);
            return true;
        }, cancellationToken);

        return new InitializeVaultResponse(root.Id, root.Path, administrator.Id);
    }
}
