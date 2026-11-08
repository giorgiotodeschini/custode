// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Custode.Infrastructure.Administration;
using Custode.Infrastructure.Tenancy;

namespace Custode.Server.Features.TenantAdministration.Policies;

public sealed class GetPolicyHandler(TenantPolicyReader policies, ITenantContext tenantContext)
{
    public async Task<PolicyResponse> HandleAsync(CancellationToken cancellationToken)
    {
        var policy = await policies.GetAsync(tenantContext.RequireTenantId(), cancellationToken);

        return new PolicyResponse(
            policy.IdleTimeoutMinutes,
            policy.AccessTokenLifetimeMinutes,
            policy.RefreshTokenLifetimeDays,
            policy.ClipboardClearSeconds);
    }
}
