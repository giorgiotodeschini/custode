// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Custode.Infrastructure.Audit;
using Custode.Infrastructure.Identity;
using Custode.Infrastructure.Persistence;
using Custode.Infrastructure.Sessions;
using Custode.Infrastructure.Tenancy;
using Custode.Infrastructure.Vault;
using Microsoft.EntityFrameworkCore;

namespace Custode.Server.Features.TenantAdministration.Members;

public sealed class SetMemberEnabledHandler(
    AppDbContext dbContext,
    ITenantContext tenantContext,
    ICurrentUser currentUser,
    IAuditTrail auditTrail,
    ISessionRevoker sessions)
{
    public async Task HandleAsync(Guid userId, bool enabled, CancellationToken cancellationToken)
    {
        var tenantId = tenantContext.RequireTenantId();
        var actorId = currentUser.Id;

        // The last-administrator check and the change commit together with the audit entry. The audit chain head
        // is a concurrency token, so two concurrent changes in the same tenant cannot both pass the check.
        var changed = await AuditedWork.CommitAsync(dbContext, async () =>
        {
            var user = await dbContext.Users.FirstOrDefaultAsync(entity => entity.Id == userId && entity.TenantId == tenantId, cancellationToken)
                ?? throw new VaultNotFoundException("The member does not exist in this tenant.");

            if (user.IsEnabled == enabled)
            {
                return false;
            }

            if (!enabled && await AdministratorRoles.IsLastEnabledAdministratorAsync(dbContext, user, tenantId, cancellationToken))
            {
                throw new VaultConflictException("The last enabled tenant administrator cannot be disabled.");
            }

            user.IsEnabled = enabled;

            await auditTrail.RecordAsync(
                new AuditRecord(actorId, enabled ? AuditActions.MemberEnabled : AuditActions.MemberDisabled, "user", userId.ToString("D")),
                cancellationToken);
            return true;
        }, cancellationToken);

        if (changed && !enabled)
        {
            await sessions.RevokeUserSessionsAsync(tenantId, userId, SessionService.MemberDisabledReason, actorId, null, cancellationToken);
        }
    }
}
