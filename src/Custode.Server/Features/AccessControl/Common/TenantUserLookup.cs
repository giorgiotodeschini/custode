// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Custode.Infrastructure.Identity;
using Custode.Infrastructure.Tenancy;
using Custode.Infrastructure.Vault;
using Microsoft.AspNetCore.Identity;

namespace Custode.Server.Features.AccessControl.Common;

internal sealed class TenantUserLookup(UserManager<ApplicationUser> userManager, ITenantContext tenantContext)
{
    public async Task<ApplicationUser> RequireUserAsync(Guid userId)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());

        if (user is null || user.TenantId != tenantContext.RequireTenantId())
        {
            throw new VaultNotFoundException("The user does not exist in this tenant.");
        }

        return user;
    }
}
