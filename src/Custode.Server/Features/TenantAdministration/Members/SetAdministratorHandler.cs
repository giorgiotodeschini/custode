// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Custode.Infrastructure.Audit;
using Custode.Infrastructure.Identity;
using Custode.Infrastructure.Persistence;
using Custode.Infrastructure.Sessions;
using Custode.Infrastructure.Tenancy;
using Custode.Infrastructure.Vault;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Custode.Server.Features.TenantAdministration.Members;

public sealed class SetAdministratorHandler(
    AppDbContext dbContext,
    ITenantContext tenantContext,
    ICurrentUser currentUser,
    IAuditTrail auditTrail,
    ISessionRevoker sessions)
{
    public async Task HandleAsync(Guid userId, SetAdministratorRequest request, CancellationToken cancellationToken)
    {
        if (request.IsAdministrator is not { } desired)
        {
            throw new InvalidVaultRequestException("The field isAdministrator is required.");
        }

        var tenantId = tenantContext.RequireTenantId();
        var actorId = currentUser.Id;

        var changed = await AuditedWork.CommitAsync(dbContext, async () =>
        {
            var user = await dbContext.Users.FirstOrDefaultAsync(entity => entity.Id == userId && entity.TenantId == tenantId, cancellationToken)
                ?? throw new VaultNotFoundException("The member does not exist in this tenant.");

            var roleId = await AdministratorRoles.RoleIdAsync(dbContext, cancellationToken);

            if (roleId == Guid.Empty)
            {
                roleId = Guid.NewGuid();
                dbContext.Roles.Add(new IdentityRole<Guid>
                {
                    Id = roleId,
                    Name = Custode.Infrastructure.Tenancy.TenantPolicies.TenantAdministrator,
                    NormalizedName = Custode.Infrastructure.Tenancy.TenantPolicies.TenantAdministrator.ToUpperInvariant(),
                });
            }

            var linked = await dbContext.UserRoles.AnyAsync(link => link.UserId == userId && link.RoleId == roleId, cancellationToken);

            if (linked == desired)
            {
                return false;
            }

            if (desired)
            {
                dbContext.UserRoles.Add(new IdentityUserRole<Guid> { UserId = userId, RoleId = roleId });
            }
            else
            {
                if (await AdministratorRoles.IsLastEnabledAdministratorAsync(dbContext, user, tenantId, cancellationToken))
                {
                    throw new VaultConflictException("The last enabled tenant administrator cannot lose the role.");
                }

                var link = await dbContext.UserRoles.SingleAsync(entry => entry.UserId == userId && entry.RoleId == roleId, cancellationToken);
                dbContext.UserRoles.Remove(link);
            }

            await auditTrail.RecordAsync(
                new AuditRecord(actorId, desired ? AuditActions.AdministratorRoleGranted : AuditActions.AdministratorRoleWithdrawn, "user", userId.ToString("D")),
                cancellationToken);
            return true;
        }, cancellationToken);

        if (changed && !desired)
        {
            await sessions.RevokeUserSessionsAsync(tenantId, userId, SessionService.AdministratorWithdrawnReason, actorId, null, cancellationToken);
        }
    }
}
