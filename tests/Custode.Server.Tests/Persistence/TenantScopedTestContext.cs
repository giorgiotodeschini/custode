// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Custode.Infrastructure.Persistence;
using Custode.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Custode.Server.Tests.Persistence;

public sealed class TestTenantContext : ITenantContext
{
    public Guid? TenantId { get; set; }
}

public sealed class Note : ITenantOwned
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public string Title { get; set; } = string.Empty;
}

public sealed class TenantScopedTestContext(DbContextOptions options, ITenantContext tenantContext)
    : AppDbContext(options, tenantContext)
{
    public DbSet<Note> Notes => Set<Note>();
}
