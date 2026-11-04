// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Custode.Infrastructure.Audit;
using Custode.Infrastructure.Persistence;
using Custode.Server.Tests.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Custode.Server.Tests.Hosting;

// One fresh in-memory database per test instance, because some tests drop the immutability triggers.
public sealed class SqliteVaultDatabase : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public DbContextOptions<AppDbContext> Options { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        Options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;

        await using var context = new AppDbContext(Options, new TestTenantContext());
        await context.Database.EnsureCreatedAsync();

        foreach (var statement in AuditImmutabilityTriggers.SqliteUp)
        {
            await context.Database.ExecuteSqlRawAsync(statement);
        }
    }

    public async Task DisposeAsync()
    {
        await _connection.DisposeAsync();
    }

    public AppDbContext CreateContext(Guid tenantId)
    {
        return new AppDbContext(Options, new TestTenantContext { TenantId = tenantId });
    }

    public AppDbContext CreateContextWith(Guid tenantId, IInterceptor interceptor)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>(Options).AddInterceptors(interceptor).Options;

        return new AppDbContext(options, new TestTenantContext { TenantId = tenantId });
    }

    public async Task ExecuteAsync(string sql)
    {
        await using var context = new AppDbContext(Options, new TestTenantContext());
        await context.Database.ExecuteSqlRawAsync(sql);
    }
}
