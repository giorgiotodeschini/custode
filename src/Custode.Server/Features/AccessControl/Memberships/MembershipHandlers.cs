// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Custode.Infrastructure.Audit;
using Custode.Infrastructure.Identity;
using Custode.Infrastructure.Persistence;
using Custode.Infrastructure.Tenancy;
using Custode.Infrastructure.Vault;
using Custode.Server.Features.AccessControl.Common;
using Microsoft.EntityFrameworkCore;

namespace Custode.Server.Features.AccessControl.Memberships;

internal sealed class AddMemberHandler(
    AppDbContext dbContext,
    ITenantContext tenantContext,
    TenantUserLookup userLookup,
    ICurrentUser currentUser,
    IAuditTrail auditTrail)
{
    public async Task HandleAsync(Guid groupId, Guid userId, CancellationToken cancellationToken)
    {
        await GroupLookup.RequireGroupAsync(dbContext, groupId, cancellationToken);
        await userLookup.RequireUserAsync(userId);

        if (await dbContext.GroupMemberships.AnyAsync(membership => membership.GroupId == groupId && membership.UserId == userId, cancellationToken))
        {
            throw new VaultConflictException("The user is already a member of this group.");
        }

        var membership = new GroupMembership { TenantId = tenantContext.RequireTenantId(), GroupId = groupId, UserId = userId };

        await AuditedWork.CommitAsync(dbContext, async () =>
        {
            dbContext.GroupMemberships.Add(membership);
            await auditTrail.RecordAsync(
                new AuditRecord(currentUser.Id, AuditActions.MembershipAdded, "group", groupId.ToString("D"), "user", userId.ToString("D")),
                cancellationToken);
            return true;
        }, cancellationToken);
    }
}

internal sealed class RemoveMemberHandler(AppDbContext dbContext, ICurrentUser currentUser, IAuditTrail auditTrail)
{
    public async Task HandleAsync(Guid groupId, Guid userId, CancellationToken cancellationToken)
    {
        var membership = await dbContext.GroupMemberships
            .FirstOrDefaultAsync(entry => entry.GroupId == groupId && entry.UserId == userId, cancellationToken)
            ?? throw new VaultNotFoundException("The user is not a member of this group.");

        await AuditedWork.CommitAsync(dbContext, async () =>
        {
            dbContext.GroupMemberships.Remove(membership);
            await auditTrail.RecordAsync(
                new AuditRecord(currentUser.Id, AuditActions.MembershipRemoved, "group", groupId.ToString("D"), "user", userId.ToString("D")),
                cancellationToken);
            return true;
        }, cancellationToken);
    }
}
