// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Custode.Infrastructure.Audit;
using Custode.Infrastructure.Persistence;
using Custode.Infrastructure.Vault;
using Custode.Server.Tests.Hosting;
using Custode.Server.Tests.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Custode.Server.Tests.Audit;

public sealed class AuditTrailTests : IAsyncLifetime
{
    private readonly SqliteVaultDatabase _database = new();
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _actor = Guid.NewGuid();

    public Task InitializeAsync() => _database.InitializeAsync();

    public Task DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task Record_AssignsConsecutiveSequencesAndChainsEachEntryToItsPredecessor()
    {
        await using var context = _database.CreateContext(_tenant);
        var trail = new AuditTrail(context, new TestTenantContext { TenantId = _tenant });

        await RecordAndSaveAsync(context, trail, "first");
        await RecordAndSaveAsync(context, trail, "second");
        await RecordAndSaveAsync(context, trail, "third");

        var entries = await context.AuditEntries.OrderBy(entry => entry.Sequence).ToListAsync();

        Assert.Equal([1L, 2L, 3L], entries.Select(entry => entry.Sequence));
        Assert.Equal(string.Empty, entries[0].PreviousHash);
        Assert.Equal(entries[0].Hash, entries[1].PreviousHash);
        Assert.Equal(entries[1].Hash, entries[2].PreviousHash);
    }

    [Fact]
    public async Task FailedAuditWrite_RollsBackTheAction()
    {
        var folderId = Guid.NewGuid();

        await using var context = _database.CreateContextWith(_tenant, new FailingAuditInterceptor());
        var trail = new AuditTrail(context, new TestTenantContext { TenantId = _tenant });

        await Assert.ThrowsAsync<InvalidOperationException>(() => AuditedWork.CommitAsync(context, async () =>
        {
            context.Folders.Add(new Folder { Id = folderId, TenantId = _tenant, Path = "/rolled-back", CreatedAtUtc = DateTimeOffset.UtcNow });
            await trail.RecordAsync(new AuditRecord(_actor, AuditActions.FolderCreated, "folder", folderId.ToString("D")), CancellationToken.None);
            return true;
        }, CancellationToken.None));

        await using var check = _database.CreateContext(_tenant);
        Assert.False(await check.Folders.AnyAsync(folder => folder.Id == folderId));
        Assert.False(await check.AuditEntries.AnyAsync());
    }

    [Fact]
    public async Task RecordDenied_PersistsEntryWithDeniedOutcome()
    {
        var secretId = Guid.NewGuid();

        await using (var context = _database.CreateContext(_tenant))
        {
            var trail = new AuditTrail(context, new TestTenantContext { TenantId = _tenant });
            await trail.RecordDeniedAsync(new AuditRecord(_actor, AuditActions.SecretValueRead, "secret", secretId.ToString("D")), CancellationToken.None);
        }

        await using var check = _database.CreateContext(_tenant);
        var entry = await check.AuditEntries.SingleAsync(candidate => candidate.TargetId == secretId.ToString("D"));
        Assert.Equal("denied", entry.Outcome);
        Assert.Equal(AuditActions.SecretValueRead, entry.Action);
    }

    [Fact]
    public async Task Verify_IntactLog_ReportsEntryCount()
    {
        await using var context = _database.CreateContext(_tenant);
        var trail = new AuditTrail(context, new TestTenantContext { TenantId = _tenant });
        await RecordAndSaveAsync(context, trail, "one");
        await RecordAndSaveAsync(context, trail, "two");

        var verification = await trail.VerifyAsync(CancellationToken.None);

        Assert.True(verification.IsIntact);
        Assert.Equal(2, verification.EntriesVerified);
        Assert.Null(verification.FirstAlteredSequence);
    }

    [Fact]
    public async Task Verify_AlteredEntry_ReportsFirstAlteredSequence()
    {
        await using (var context = _database.CreateContext(_tenant))
        {
            var trail = new AuditTrail(context, new TestTenantContext { TenantId = _tenant });
            await RecordAndSaveAsync(context, trail, "one");
            await RecordAndSaveAsync(context, trail, "two");
            await RecordAndSaveAsync(context, trail, "three");
        }

        // Bypass the immutability trigger the way a database administrator could, then tamper with entry 2.
        await _database.ExecuteAsync("DROP TRIGGER audit_entries_reject_update;");

        await using (var tamper = _database.CreateContext(_tenant))
        {
            await tamper.Database.ExecuteSqlRawAsync(
                "UPDATE \"AuditEntries\" SET \"Action\" = 'folder deleted' WHERE \"TenantId\" = {0} AND \"Sequence\" = 2",
                _tenant);
        }

        await using var verifier = _database.CreateContext(_tenant);
        var verification = await new AuditTrail(verifier, new TestTenantContext { TenantId = _tenant }).VerifyAsync(CancellationToken.None);

        Assert.False(verification.IsIntact);
        Assert.Equal(2L, verification.FirstAlteredSequence);
    }

    [Fact]
    public async Task RawUpdateOfAuditEntry_IsRejectedByTheDatabase()
    {
        await using (var context = _database.CreateContext(_tenant))
        {
            await RecordAndSaveAsync(context, new AuditTrail(context, new TestTenantContext { TenantId = _tenant }), "immutable");
        }

        await using var attempt = _database.CreateContext(_tenant);
        await Assert.ThrowsAsync<SqliteException>(() => attempt.Database.ExecuteSqlRawAsync(
            "UPDATE \"AuditEntries\" SET \"Action\" = 'changed' WHERE \"TenantId\" = {0}",
            _tenant));
    }

    [Fact]
    public async Task RawDeleteOfAuditEntry_IsRejectedByTheDatabase()
    {
        await using (var context = _database.CreateContext(_tenant))
        {
            await RecordAndSaveAsync(context, new AuditTrail(context, new TestTenantContext { TenantId = _tenant }), "undeletable");
        }

        await using var attempt = _database.CreateContext(_tenant);
        await Assert.ThrowsAsync<SqliteException>(() => attempt.Database.ExecuteSqlRawAsync(
            "DELETE FROM \"AuditEntries\" WHERE \"TenantId\" = {0}",
            _tenant));
    }

    [Fact]
    public async Task ModifyingAuditEntry_IsRejected_AndTheAttemptIsRecordedBeforeBlocking()
    {
        await using var context = _database.CreateContext(_tenant);
        await RecordAndSaveAsync(context, new AuditTrail(context, new TestTenantContext { TenantId = _tenant }), "guarded");

        var entry = await context.AuditEntries.SingleAsync();
        entry.Outcome = "tampered";

        await Assert.ThrowsAsync<AuditEntryImmutableException>(() => context.SaveChangesAsync());

        await AssertTamperingAttemptRecordedAsync(entry.Id, "update");
    }

    [Fact]
    public async Task DeletingAuditEntry_IsRejected_AndTheAttemptIsRecordedBeforeBlocking()
    {
        await using var context = _database.CreateContext(_tenant);
        await RecordAndSaveAsync(context, new AuditTrail(context, new TestTenantContext { TenantId = _tenant }), "undeletable");

        var entry = await context.AuditEntries.SingleAsync();
        context.AuditEntries.Remove(entry);

        await Assert.ThrowsAsync<AuditEntryImmutableException>(() => context.SaveChangesAsync());

        await AssertTamperingAttemptRecordedAsync(entry.Id, "delete");
    }

    [Fact]
    public async Task ModifyingAuditEntry_ThroughSynchronousSave_IsRejected_AndRecorded()
    {
        await using var context = _database.CreateContext(_tenant);
        await RecordAndSaveAsync(context, new AuditTrail(context, new TestTenantContext { TenantId = _tenant }), "sync");

        var entry = await context.AuditEntries.SingleAsync();
        entry.Outcome = "tampered";

        Assert.Throws<AuditEntryImmutableException>(() => context.SaveChanges());

        await AssertTamperingAttemptRecordedAsync(entry.Id, "update");
    }

    private async Task AssertTamperingAttemptRecordedAsync(Guid entryId, string operation)
    {
        await using var check = _database.CreateContext(_tenant);

        var attempt = await check.AuditEntries.SingleAsync(candidate => candidate.Action == AuditActions.AuditTamperingAttempt);
        Assert.Equal("denied", attempt.Outcome);
        Assert.Equal("audit entry", attempt.TargetType);
        Assert.Equal(entryId.ToString("D"), attempt.TargetId);
        Assert.Equal(operation, attempt.SecondaryTargetId);

        var original = await check.AuditEntries.SingleAsync(candidate => candidate.Id == entryId);
        Assert.Equal("succeeded", original.Outcome);
    }

    private async Task RecordAndSaveAsync(AppDbContext context, AuditTrail trail, string target)
    {
        await trail.RecordAsync(new AuditRecord(_actor, AuditActions.SecretCreated, "secret", target), CancellationToken.None);
        await context.SaveChangesAsync();
    }

    private sealed class FailingAuditInterceptor : SaveChangesInterceptor
    {
        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            return Check(eventData, result);
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(Check(eventData, result));
        }

        private static InterceptionResult<int> Check(DbContextEventData eventData, InterceptionResult<int> result)
        {
            if (eventData.Context!.ChangeTracker.Entries<AuditEntry>().Any(entry => entry.State == EntityState.Added))
            {
                throw new InvalidOperationException("The audit store is unavailable.");
            }

            return result;
        }
    }
}
