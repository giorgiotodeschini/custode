// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Custode.Infrastructure.Tenancy;

namespace Custode.Server.Tests.Tenancy;

public class TenantOwnedContractTests
{
    private sealed class SampleEntity : ITenantOwned
    {
        public Guid Id { get; set; }

        public Guid TenantId { get; set; }
    }

    [Fact]
    public void TenantOwnedEntity_ExposesTenantIdAsGuid()
    {
        var property = typeof(ITenantOwned).GetProperty(nameof(ITenantOwned.TenantId));

        Assert.NotNull(property);
        Assert.Equal(typeof(Guid), property.PropertyType);
    }

    [Fact]
    public void TenantOwnedEntity_KeepsAssignedTenantId()
    {
        var tenantId = Guid.NewGuid();
        ITenantOwned entity = new SampleEntity { TenantId = tenantId };

        Assert.Equal(tenantId, entity.TenantId);
    }
}
