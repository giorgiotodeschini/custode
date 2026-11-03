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
