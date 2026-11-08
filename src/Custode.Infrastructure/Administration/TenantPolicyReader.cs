// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Custode.Infrastructure.Authentication;
using Custode.Infrastructure.Persistence;
using Custode.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Custode.Infrastructure.Administration;

public sealed class TenantPolicyReader(DbContextOptions<AppDbContext> options, IOptions<JwtOptions> jwtOptions)
{
    public async Task<TenantPolicy> GetAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        await using var db = new AppDbContext(options, new FixedTenantContext(tenantId));

        var policy = await db.TenantPolicies
            .AsNoTracking()
            .FirstOrDefaultAsync(entity => entity.TenantId == tenantId, cancellationToken);

        return policy ?? new TenantPolicy
        {
            TenantId = tenantId,
            AccessTokenLifetimeMinutes = jwtOptions.Value.LifetimeMinutes,
        };
    }
}
