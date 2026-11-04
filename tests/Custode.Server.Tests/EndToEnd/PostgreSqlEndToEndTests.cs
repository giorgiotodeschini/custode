// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Custode.Server.Tests.Hosting;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Custode.Server.Tests.EndToEnd;

public sealed class PostgreSqlFixture : IAsyncLifetime
{
    public PostgreSqlContainer Container { get; } = new PostgreSqlBuilder("postgres:18").Build();

    public Task InitializeAsync()
    {
        return Container.StartAsync();
    }

    public Task DisposeAsync()
    {
        return Container.DisposeAsync().AsTask();
    }

    public async Task<string> CreateCleanDatabaseAsync()
    {
        var adminConnectionString = Container.GetConnectionString();
        var databaseName = "custode_test_" + Guid.NewGuid().ToString("N");

        await using (var admin = new NpgsqlConnection(adminConnectionString))
        {
            await admin.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", admin);
            await command.ExecuteNonQueryAsync();
        }

        return new NpgsqlConnectionStringBuilder(adminConnectionString) { Database = databaseName }.ConnectionString;
    }
}

public sealed class PostgreSqlEndToEndTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>
{
    [Fact]
    public async Task TwoTenantsAndOneOperator_OnCleanPostgreSqlDatabase_BehaveAsSpecified()
    {
        var connectionString = await fixture.CreateCleanDatabaseAsync();

        await EndToEndScenarios.TwoTenantsAndOneOperator(
            certificatePath => CustodeTestHost.StartPostgreSqlAsync(connectionString, certificatePath));
    }

    [Fact]
    public async Task SecretsProtectedBeforeRestart_DecryptAfterRestart_OnPostgreSql()
    {
        var connectionString = await fixture.CreateCleanDatabaseAsync();

        await EndToEndScenarios.SecretsProtectedBeforeRestart(
            certificatePath => CustodeTestHost.StartPostgreSqlAsync(connectionString, certificatePath));
    }
}
