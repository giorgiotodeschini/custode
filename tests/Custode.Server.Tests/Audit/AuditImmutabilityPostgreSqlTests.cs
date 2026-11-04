// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Custode.Infrastructure.Audit;
using Custode.Server.Tests.EndToEnd;
using Custode.Server.Tests.Hosting;
using Custode.Server.Tests.Persistence;
using Custode.Server.Tests.SecretProtection;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Custode.Server.Tests.Audit;

public sealed class AuditImmutabilityPostgreSqlTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>
{
    [Fact]
    public async Task MigratedDatabase_RejectsRawUpdateOfAuditEntries()
    {
        var connectionString = await fixture.CreateCleanDatabaseAsync();
        await using var host = await CustodeTestHost.StartPostgreSqlAsync(connectionString, TestKeyEncryptionCertificate.Shared);
        var tenant = await RecordOneEntryAsync(host);

        await using var attempt = host.CreateTenantContext(tenant);
        var exception = await Assert.ThrowsAsync<PostgresException>(() => attempt.Database.ExecuteSqlRawAsync(
            "UPDATE \"AuditEntries\" SET \"Action\" = 'changed'"));

        Assert.Contains("audit entries are immutable", exception.MessageText);
    }

    [Fact]
    public async Task MigratedDatabase_RejectsRawDeleteOfAuditEntries()
    {
        var connectionString = await fixture.CreateCleanDatabaseAsync();
        await using var host = await CustodeTestHost.StartPostgreSqlAsync(connectionString, TestKeyEncryptionCertificate.Shared);
        var tenant = await RecordOneEntryAsync(host);

        await using var attempt = host.CreateTenantContext(tenant);
        var exception = await Assert.ThrowsAsync<PostgresException>(() => attempt.Database.ExecuteSqlRawAsync(
            "DELETE FROM \"AuditEntries\""));

        Assert.Contains("audit entries are immutable", exception.MessageText);
    }

    [Fact]
    public async Task ModifyingAuditEntryThroughTheContext_IsRejected_AndRecordedOnPostgreSql()
    {
        var connectionString = await fixture.CreateCleanDatabaseAsync();
        await using var host = await CustodeTestHost.StartPostgreSqlAsync(connectionString, TestKeyEncryptionCertificate.Shared);
        var tenant = await RecordOneEntryAsync(host);

        await using var db = host.CreateTenantContext(tenant);
        var entry = await db.AuditEntries.SingleAsync();
        entry.Outcome = "tampered";

        await Assert.ThrowsAsync<AuditEntryImmutableException>(() => db.SaveChangesAsync());

        await using var check = host.CreateTenantContext(tenant);
        var attempt = await check.AuditEntries.SingleAsync(candidate => candidate.Action == AuditActions.AuditTamperingAttempt);
        Assert.Equal("denied", attempt.Outcome);
        Assert.Equal(entry.Id.ToString("D"), attempt.TargetId);
        Assert.Equal("update", attempt.SecondaryTargetId);
    }

    private static async Task<Guid> RecordOneEntryAsync(CustodeTestHost host)
    {
        var tenant = Guid.NewGuid();

        await using var db = host.CreateTenantContext(tenant);
        var trail = new AuditTrail(db, new TestTenantContext { TenantId = tenant });
        await trail.RecordAsync(new AuditRecord(Guid.NewGuid(), AuditActions.SecretCreated, "secret", Guid.NewGuid().ToString("D")), CancellationToken.None);
        await db.SaveChangesAsync();

        return tenant;
    }
}
