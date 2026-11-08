// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Custode.Infrastructure.Persistence;
using Custode.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Custode.Server.Features.TenantAdministration.Members;

public sealed class ListMembersHandler(AppDbContext dbContext, ITenantContext tenantContext)
{
    public async Task<IReadOnlyList<MemberResponse>> HandleAsync(CancellationToken cancellationToken)
    {
        var tenantId = tenantContext.RequireTenantId();

        // Identity users are not covered by the tenant query filter, so the tenant is applied explicitly.
        var users = await dbContext.Users
            .AsNoTracking()
            .Where(user => user.TenantId == tenantId)
            .OrderBy(user => user.UserName)
            .Select(user => new { user.Id, user.UserName, user.IsEnabled, user.MustChangePassword })
            .ToListAsync(cancellationToken);

        var userIds = users.Select(user => user.Id).ToList();

        var roles = await (
            from link in dbContext.UserRoles.AsNoTracking()
            join role in dbContext.Roles.AsNoTracking() on link.RoleId equals role.Id
            where userIds.Contains(link.UserId)
            select new { link.UserId, role.Name }).ToListAsync(cancellationToken);

        var groupCounts = await dbContext.GroupMemberships
            .AsNoTracking()
            .Where(membership => membership.TenantId == tenantId && userIds.Contains(membership.UserId))
            .GroupBy(membership => membership.UserId)
            .Select(group => new { UserId = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);

        return users.Select(user => new MemberResponse(
            user.Id,
            user.UserName!,
            roles.Where(role => role.UserId == user.Id).Select(role => role.Name!).OrderBy(name => name).ToList(),
            groupCounts.FirstOrDefault(count => count.UserId == user.Id)?.Count ?? 0,
            user.IsEnabled,
            user.MustChangePassword)).ToList();
    }
}
