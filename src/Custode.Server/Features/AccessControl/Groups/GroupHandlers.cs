// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Custode.Infrastructure.Audit;
using Custode.Infrastructure.Identity;
using Custode.Infrastructure.Persistence;
using Custode.Infrastructure.Tenancy;
using Custode.Infrastructure.Vault;
using Custode.Server.Features.AccessControl.Common;
using Microsoft.EntityFrameworkCore;

namespace Custode.Server.Features.AccessControl.Groups;

internal static class GroupName
{
    private const int MaxLength = 256;

    public static string Normalize(string? name)
    {
        var trimmed = name?.Trim();

        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > MaxLength)
        {
            throw new InvalidVaultRequestException($"A group name is required and must be at most {MaxLength} characters.");
        }

        return trimmed;
    }
}

internal sealed class CreateGroupHandler(AppDbContext dbContext, ITenantContext tenantContext, ICurrentUser currentUser, IAuditTrail auditTrail)
{
    public async Task<GroupResponse> HandleAsync(GroupNameRequest request, CancellationToken cancellationToken)
    {
        var name = GroupName.Normalize(request.Name);
        var tenantId = tenantContext.RequireTenantId();

        if (await dbContext.Groups.AnyAsync(group => group.Name == name, cancellationToken))
        {
            throw new VaultConflictException("A group with this name already exists.");
        }

        var group = new Group { Id = Guid.NewGuid(), TenantId = tenantId, Name = name };

        await AuditedWork.CommitAsync(dbContext, async () =>
        {
            dbContext.Groups.Add(group);
            await auditTrail.RecordAsync(
                new AuditRecord(currentUser.Id, AuditActions.GroupCreated, "group", group.Id.ToString("D")),
                cancellationToken);
            return true;
        }, cancellationToken);

        return new GroupResponse(group.Id, group.Name);
    }
}

internal sealed class RenameGroupHandler(AppDbContext dbContext, ICurrentUser currentUser, IAuditTrail auditTrail)
{
    public async Task<GroupResponse> HandleAsync(Guid groupId, GroupNameRequest request, CancellationToken cancellationToken)
    {
        var name = GroupName.Normalize(request.Name);
        var group = await GroupLookup.RequireGroupAsync(dbContext, groupId, cancellationToken);

        if (await dbContext.Groups.AnyAsync(other => other.Name == name && other.Id != groupId, cancellationToken))
        {
            throw new VaultConflictException("A group with this name already exists.");
        }

        await AuditedWork.CommitAsync(dbContext, async () =>
        {
            group.Name = name;
            await auditTrail.RecordAsync(
                new AuditRecord(currentUser.Id, AuditActions.GroupRenamed, "group", groupId.ToString("D")),
                cancellationToken);
            return true;
        }, cancellationToken);

        return new GroupResponse(group.Id, group.Name);
    }
}

internal sealed class DeleteGroupHandler(AppDbContext dbContext, ICurrentUser currentUser, IAuditTrail auditTrail)
{
    public async Task HandleAsync(Guid groupId, CancellationToken cancellationToken)
    {
        var group = await GroupLookup.RequireGroupAsync(dbContext, groupId, cancellationToken);

        var memberships = await dbContext.GroupMemberships
            .Where(membership => membership.GroupId == groupId)
            .ToListAsync(cancellationToken);

        var grants = await dbContext.FolderGrants
            .Where(grant => grant.PrincipalKind == PrincipalKind.Group && grant.PrincipalId == groupId)
            .ToListAsync(cancellationToken);

        await AuditedWork.CommitAsync(dbContext, async () =>
        {
            dbContext.GroupMemberships.RemoveRange(memberships);
            dbContext.FolderGrants.RemoveRange(grants);
            dbContext.Groups.Remove(group);
            await auditTrail.RecordAsync(
                new AuditRecord(currentUser.Id, AuditActions.GroupDeleted, "group", groupId.ToString("D")),
                cancellationToken);
            return true;
        }, cancellationToken);
    }
}
