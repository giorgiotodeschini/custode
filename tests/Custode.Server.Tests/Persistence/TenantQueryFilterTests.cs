using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Custode.Server.Tests.Persistence;

public sealed class TenantQueryFilterTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly DbContextOptions _options;
    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();

    public TenantQueryFilterTests()
    {
        _options = new DbContextOptionsBuilder().UseSqlite(_connection).Options;
    }

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();

        var tenantA = new TestTenantContext { TenantId = _tenantA };
        await using (var seedA = new TenantScopedTestContext(_options, tenantA))
        {
            await seedA.Database.EnsureCreatedAsync();
            seedA.Notes.AddRange(
                new Note { Id = Guid.NewGuid(), TenantId = _tenantA, Title = "A1" },
                new Note { Id = Guid.NewGuid(), TenantId = _tenantA, Title = "A2" });
            await seedA.SaveChangesAsync();
        }

        var tenantB = new TestTenantContext { TenantId = _tenantB };
        await using var seedB = new TenantScopedTestContext(_options, tenantB);
        seedB.Notes.Add(new Note { Id = Guid.NewGuid(), TenantId = _tenantB, Title = "B1" });
        await seedB.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task Query_ReturnsOnlyRowsOfCurrentTenant()
    {
        await using var context = new TenantScopedTestContext(_options, new TestTenantContext { TenantId = _tenantA });

        var titles = await context.Notes.Select(note => note.Title).OrderBy(title => title).ToListAsync();

        Assert.Equal(["A1", "A2"], titles);
    }

    [Fact]
    public async Task Query_ForOtherTenant_ReturnsOnlyThatTenantsRows()
    {
        await using var context = new TenantScopedTestContext(_options, new TestTenantContext { TenantId = _tenantB });

        var titles = await context.Notes.Select(note => note.Title).ToListAsync();

        Assert.Equal(["B1"], titles);
    }

    [Fact]
    public async Task Query_WithoutTenant_ReturnsNoRows()
    {
        await using var context = new TenantScopedTestContext(_options, new TestTenantContext { TenantId = null });

        var count = await context.Notes.CountAsync();

        Assert.Equal(0, count);
    }
}
