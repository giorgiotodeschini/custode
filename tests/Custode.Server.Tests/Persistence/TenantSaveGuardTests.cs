using Custode.Infrastructure.Tenancy;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Custode.Server.Tests.Persistence;

public sealed class TenantSaveGuardTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly DbContextOptions _options;
    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();

    public TenantSaveGuardTests()
    {
        _connection.Open();
        _options = new DbContextOptionsBuilder().UseSqlite(_connection).Options;

        using var context = new TenantScopedTestContext(_options, new TestTenantContext { TenantId = _tenantA });
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _connection.Dispose();
    }

    [Fact]
    public async Task SaveAsync_EntityWithoutTenantId_IsRejectedAndNothingIsStored()
    {
        await using var context = new TenantScopedTestContext(_options, new TestTenantContext { TenantId = _tenantA });
        context.Notes.Add(new Note { Id = Guid.NewGuid(), Title = "orphan" });

        await Assert.ThrowsAsync<TenantScopeViolationException>(() => context.SaveChangesAsync());

        Assert.Equal(0, await CountStoredNotesAsync());
    }

    [Fact]
    public void Save_EntityWithoutTenantId_IsRejectedAndNothingIsStored()
    {
        using var context = new TenantScopedTestContext(_options, new TestTenantContext { TenantId = _tenantA });
        context.Notes.Add(new Note { Id = Guid.NewGuid(), Title = "orphan" });

        Assert.Throws<TenantScopeViolationException>(() => context.SaveChanges());

        Assert.Equal(0, CountStoredNotes());
    }

    [Fact]
    public async Task SaveAsync_EntityForAnotherTenant_IsRejectedAndNothingIsStored()
    {
        await using var context = new TenantScopedTestContext(_options, new TestTenantContext { TenantId = _tenantA });
        context.Notes.Add(new Note { Id = Guid.NewGuid(), TenantId = _tenantB, Title = "foreign" });

        await Assert.ThrowsAsync<TenantScopeViolationException>(() => context.SaveChangesAsync());

        Assert.Equal(0, await CountStoredNotesAsync());
    }

    [Fact]
    public async Task SaveAsync_ExistingEntityMovedToAnotherTenant_IsRejected()
    {
        var noteId = Guid.NewGuid();
        await using (var seed = new TenantScopedTestContext(_options, new TestTenantContext { TenantId = _tenantA }))
        {
            seed.Notes.Add(new Note { Id = noteId, TenantId = _tenantA, Title = "A-note" });
            await seed.SaveChangesAsync();
        }

        await using var context = new TenantScopedTestContext(_options, new TestTenantContext { TenantId = _tenantA });
        var note = await context.Notes.SingleAsync(entity => entity.Id == noteId);
        note.TenantId = _tenantB;

        await Assert.ThrowsAsync<TenantScopeViolationException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task SaveAsync_WithoutCurrentTenant_IsRejected()
    {
        await using var context = new TenantScopedTestContext(_options, new TestTenantContext { TenantId = null });
        context.Notes.Add(new Note { Id = Guid.NewGuid(), TenantId = _tenantA, Title = "no-scope" });

        await Assert.ThrowsAsync<TenantScopeViolationException>(() => context.SaveChangesAsync());

        Assert.Equal(0, await CountStoredNotesAsync());
    }

    [Fact]
    public async Task SaveAsync_EntityForCurrentTenant_IsStored()
    {
        await using var context = new TenantScopedTestContext(_options, new TestTenantContext { TenantId = _tenantA });
        context.Notes.Add(new Note { Id = Guid.NewGuid(), TenantId = _tenantA, Title = "valid" });

        await context.SaveChangesAsync();

        Assert.Equal(1, await CountStoredNotesAsync());
    }

    private async Task<int> CountStoredNotesAsync()
    {
        await using var context = new TenantScopedTestContext(_options, new TestTenantContext { TenantId = _tenantA });
        return await context.Notes.IgnoreQueryFilters().CountAsync();
    }

    private int CountStoredNotes()
    {
        using var context = new TenantScopedTestContext(_options, new TestTenantContext { TenantId = _tenantA });
        return context.Notes.IgnoreQueryFilters().Count();
    }
}
