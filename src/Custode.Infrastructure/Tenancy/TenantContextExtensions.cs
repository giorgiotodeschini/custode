// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

namespace Custode.Infrastructure.Tenancy;

public static class TenantContextExtensions
{
    public static Guid RequireTenantId(this ITenantContext tenantContext)
    {
        return tenantContext.TenantId
            ?? throw new TenantScopeViolationException("The operation requires a tenant scope.");
    }
}
