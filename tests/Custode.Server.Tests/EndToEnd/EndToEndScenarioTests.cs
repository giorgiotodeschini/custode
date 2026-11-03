using Custode.Server.Tests.Hosting;
using Custode.Server.Tests.SecretProtection;

namespace Custode.Server.Tests.EndToEnd;

public sealed class EndToEndScenarioTests
{
    [Fact]
    public async Task TwoTenantsAndOneOperator_OnCleanSqliteDatabase_BehaveAsSpecified()
    {
        var databasePath = SecretAvailabilityTests.NewDatabasePath();

        await EndToEndScenarios.TwoTenantsAndOneOperator(
            certificatePath => CustodeTestHost.StartAsync(databasePath, certificatePath));
    }

    [Fact]
    public async Task SecretsProtectedBeforeRestart_DecryptAfterRestart_OnSqlite()
    {
        var databasePath = SecretAvailabilityTests.NewDatabasePath();

        await EndToEndScenarios.SecretsProtectedBeforeRestart(
            certificatePath => CustodeTestHost.StartAsync(databasePath, certificatePath));
    }
}
