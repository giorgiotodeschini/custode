// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

namespace Custode.Infrastructure.Tenancy;

// Used only by credential resolution, which runs before any tenant is known and selects tenant identifiers explicitly.
public sealed class UnscopedTenantContext : ITenantContext
{
    public Guid? TenantId => null;
}
