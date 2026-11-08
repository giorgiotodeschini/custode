// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Custode.Infrastructure.Identity;
using Custode.Infrastructure.Persistence;
using Custode.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Custode.Server.Features.TenantAdministration.Members;

internal static class AdministratorRoles
{
    public static async Task<Guid> RoleIdAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        return await db.Roles
            .Where(role => role.Name == TenantPolicies.TenantAdministrator)
            .Select(role => role.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    // True when the user is an enabled administrator and no other enabled administrator exists in the tenant.
    public static async Task<bool> IsLastEnabledAdministratorAsync(AppDbContext db, ApplicationUser user, Guid tenantId, CancellationToken cancellationToken)
    {
        if (!user.IsEnabled)
        {
            return false;
        }

        var roleId = await RoleIdAsync(db, cancellationToken);

        if (roleId == Guid.Empty || !await db.UserRoles.AnyAsync(link => link.UserId == user.Id && link.RoleId == roleId, cancellationToken))
        {
            return false;
        }

        var otherEnabledAdministrator = await (
            from link in db.UserRoles
            join other in db.Users on link.UserId equals other.Id
            where link.RoleId == roleId && other.TenantId == tenantId && other.IsEnabled && other.Id != user.Id
            select other.Id).AnyAsync(cancellationToken);

        return !otherEnabledAdministrator;
    }
}
