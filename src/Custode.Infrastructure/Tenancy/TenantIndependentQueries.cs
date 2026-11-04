// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using System.Security.Claims;
using Custode.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Custode.Infrastructure.Tenancy;

public sealed class TenantIndependentQueries(AppDbContext dbContext, IAuthorizationService authorizationService)
{
    public async Task<IQueryable<TEntity>> QueryAllTenantsAsync<TEntity>(ClaimsPrincipal principal)
        where TEntity : class, ITenantOwned
    {
        var authorization = await authorizationService.AuthorizeAsync(principal, null, TenantPolicies.Operator);

        if (!authorization.Succeeded)
        {
            throw new OperatorAccessDeniedException();
        }

        return dbContext.Set<TEntity>().IgnoreQueryFilters();
    }
}
