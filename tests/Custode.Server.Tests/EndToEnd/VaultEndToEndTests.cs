// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Custode.Server.Tests.Hosting;
using Custode.Server.Tests.SecretProtection;
using Custode.Server.Tests.Vault;

namespace Custode.Server.Tests.EndToEnd;

public sealed class VaultEndToEndTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>
{
    [Fact]
    public async Task CrossTenantIsolation_OnSqlite()
    {
        await using var world = await VaultWorld.CreateAsync(await CustodeTestHost.StartAsync());

        await VaultScenarios.CrossTenantUserCannotReachAnotherTenantsVault(world);
    }

    [Fact]
    public async Task CrossTenantIsolation_OnPostgreSql()
    {
        var connectionString = await fixture.CreateCleanDatabaseAsync();
        await using var world = await VaultWorld.CreateAsync(
            await CustodeTestHost.StartPostgreSqlAsync(connectionString, TestKeyEncryptionCertificate.Shared));

        await VaultScenarios.CrossTenantUserCannotReachAnotherTenantsVault(world);
    }

    [Fact]
    public async Task DeniedAccessChanges_AreAudited_OnSqlite()
    {
        await using var world = await VaultWorld.CreateAsync(await CustodeTestHost.StartAsync());

        await VaultScenarios.NonAdministratorAccessChanges_AreForbiddenAndDenialsAreAudited(world);
    }

    [Fact]
    public async Task DeniedAccessChanges_AreAudited_OnPostgreSql()
    {
        var connectionString = await fixture.CreateCleanDatabaseAsync();
        await using var world = await VaultWorld.CreateAsync(
            await CustodeTestHost.StartPostgreSqlAsync(connectionString, TestKeyEncryptionCertificate.Shared));

        await VaultScenarios.NonAdministratorAccessChanges_AreForbiddenAndDenialsAreAudited(world);
    }

    [Fact]
    public async Task AuditVerification_DetectsAlteration_OnSqlite()
    {
        await using var world = await VaultWorld.CreateAsync(await CustodeTestHost.StartAsync());

        await VaultScenarios.AlteredAuditEntry_IsDetectedByVerification(world);
    }

    [Fact]
    public async Task AuditVerification_DetectsAlteration_OnPostgreSql()
    {
        var connectionString = await fixture.CreateCleanDatabaseAsync();
        await using var world = await VaultWorld.CreateAsync(
            await CustodeTestHost.StartPostgreSqlAsync(connectionString, TestKeyEncryptionCertificate.Shared));

        await VaultScenarios.AlteredAuditEntry_IsDetectedByVerification(world);
    }
}
