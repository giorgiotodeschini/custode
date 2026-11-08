// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Custode.Infrastructure.Persistence;
using Custode.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Custode.Server.Tests.EndToEnd;

public sealed class PostgreSqlMigrationTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>
{
    private const string PreviousMigration = "20261104212841_SecretsAndAcl";

    [Fact]
    public async Task ServerPrerequisitesMigration_BackfillsVersionOne_AndKeepsExistingUsersEnabled()
    {
        var connectionString = await fixture.CreateCleanDatabaseAsync();
        var options = new DbContextOptionsBuilder().UseNpgsql(connectionString).Options;
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var folderId = Guid.NewGuid();
        var secretId = Guid.NewGuid();

        await using (var before = new AppDbContext(options, new UnscopedTenantContext()))
        {
            await before.GetService<IMigrator>().MigrateAsync(PreviousMigration);

            await before.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "AspNetUsers" ("Id", "UserName", "NormalizedUserName", "EmailConfirmed", "PhoneNumberConfirmed", "TwoFactorEnabled", "LockoutEnabled", "AccessFailedCount", "TenantId")
                VALUES ({userId}, 'existing', 'EXISTING', false, false, false, false, 0, {tenantId});
                """);
            await before.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "Folders" ("Id", "TenantId", "Path", "InheritsPermissions", "CreatedAtUtc")
                VALUES ({folderId}, {tenantId}, '/legacy', true, now());
                """);
            await before.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "Secrets" ("Id", "TenantId", "FolderId", "Name", "Type", "ProtectedPayload", "CreatedAtUtc", "UpdatedAtUtc")
                VALUES ({secretId}, {tenantId}, {folderId}, 'legacy', 'api-key', 'protected-value', now(), now());
                """);
        }

        await using var after = new AppDbContext(options, new UnscopedTenantContext());
        await after.Database.MigrateAsync();

        var version = await after.Database.SqlQuery<VersionRow>($"""
                SELECT "Number" AS "Number", "ProtectedPayload" AS "Payload", "ChangeKind" AS "ChangeKind"
                FROM "SecretVersions" WHERE "SecretId" = {secretId}
                """)
            .SingleAsync();

        Assert.Equal(1, version.Number);
        Assert.Equal("protected-value", version.Payload);
        Assert.Equal("Created", version.ChangeKind);

        var user = await after.Database.SqlQuery<UserRow>($"""
                SELECT "IsEnabled" AS "IsEnabled", "MustChangePassword" AS "MustChangePassword"
                FROM "AspNetUsers" WHERE "Id" = {userId}
                """)
            .SingleAsync();

        Assert.True(user.IsEnabled);
        Assert.False(user.MustChangePassword);
    }

    private sealed record VersionRow(int Number, string Payload, string ChangeKind);

    private sealed record UserRow(bool IsEnabled, bool MustChangePassword);
}
