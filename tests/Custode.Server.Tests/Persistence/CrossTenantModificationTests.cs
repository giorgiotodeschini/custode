// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Custode.Infrastructure.Tenancy;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Custode.Server.Tests.Persistence;

public sealed class CrossTenantModificationTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly DbContextOptions _options;
    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();
    private readonly Guid _noteOfB = Guid.NewGuid();

    public CrossTenantModificationTests()
    {
        _connection.Open();
        _options = new DbContextOptionsBuilder().UseSqlite(_connection).Options;

        using var seedB = new TenantScopedTestContext(_options, new TestTenantContext { TenantId = _tenantB });
        seedB.Database.EnsureCreated();
        seedB.Notes.Add(new Note { Id = _noteOfB, TenantId = _tenantB, Title = "original" });
        seedB.SaveChanges();
    }

    public void Dispose()
    {
        _connection.Dispose();
    }

    [Fact]
    public async Task Find_NoteOfAnotherTenant_ReturnsNull()
    {
        await using var context = CreateContextFor(_tenantA);

        var note = await context.Notes.FindAsync(_noteOfB);

        Assert.Null(note);
    }

    [Fact]
    public async Task ExecuteUpdate_NoteOfAnotherTenant_ChangesNothing()
    {
        await using var context = CreateContextFor(_tenantA);

        var affected = await context.Notes
            .Where(note => note.Id == _noteOfB)
            .ExecuteUpdateAsync(setters => setters.SetProperty(note => note.Title, "hacked"));

        Assert.Equal(0, affected);
        Assert.Equal("original", await StoredTitleAsync());
    }

    [Fact]
    public async Task Update_NoteOfAnotherTenantByIdentifier_IsRejectedAndChangesNothing()
    {
        await using var context = CreateContextFor(_tenantA);
        context.Notes.Update(new Note { Id = _noteOfB, TenantId = _tenantA, Title = "hacked" });

        await Assert.ThrowsAsync<TenantScopeViolationException>(() => context.SaveChangesAsync());

        Assert.Equal("original", await StoredTitleAsync());
    }

    [Fact]
    public async Task Update_NoteOfAnotherTenantKeepingItsTenantId_IsRejectedAndChangesNothing()
    {
        await using var context = CreateContextFor(_tenantA);
        context.Notes.Update(new Note { Id = _noteOfB, TenantId = _tenantB, Title = "hacked" });

        await Assert.ThrowsAsync<TenantScopeViolationException>(() => context.SaveChangesAsync());

        Assert.Equal("original", await StoredTitleAsync());
    }

    [Fact]
    public async Task Remove_NoteOfAnotherTenantByIdentifier_IsRejectedAndNoteIsKept()
    {
        await using var context = CreateContextFor(_tenantA);
        context.Notes.Remove(new Note { Id = _noteOfB, TenantId = _tenantA, Title = "original" });

        await Assert.ThrowsAsync<TenantScopeViolationException>(() => context.SaveChangesAsync());

        Assert.Equal("original", await StoredTitleAsync());
    }

    [Fact]
    public async Task Update_OwnNote_IsApplied()
    {
        var ownNoteId = Guid.NewGuid();
        await using (var seedA = CreateContextFor(_tenantA))
        {
            seedA.Notes.Add(new Note { Id = ownNoteId, TenantId = _tenantA, Title = "A-note" });
            await seedA.SaveChangesAsync();
        }

        await using (var context = CreateContextFor(_tenantA))
        {
            var note = await context.Notes.SingleAsync(entity => entity.Id == ownNoteId);
            note.Title = "updated";
            await context.SaveChangesAsync();
        }

        Assert.Equal("updated", await StoredTitleAsync(_tenantA, ownNoteId));
    }

    private TenantScopedTestContext CreateContextFor(Guid tenantId)
    {
        return new TenantScopedTestContext(_options, new TestTenantContext { TenantId = tenantId });
    }

    private Task<string?> StoredTitleAsync()
    {
        return StoredTitleAsync(_tenantB, _noteOfB);
    }

    private async Task<string?> StoredTitleAsync(Guid tenantId, Guid noteId)
    {
        await using var context = CreateContextFor(tenantId);
        return await context.Notes.Where(note => note.Id == noteId).Select(note => note.Title).SingleOrDefaultAsync();
    }
}
