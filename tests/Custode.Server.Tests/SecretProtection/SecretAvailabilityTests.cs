// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Custode.Infrastructure.Persistence;
using Custode.Infrastructure.SecretProtection;
using Custode.Server.Tests.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Custode.Server.Tests.SecretProtection;

public sealed class SecretAvailabilityTests
{
    [Fact]
    public async Task MissingKeyEncryptionCertificate_RefusesSecretOperations()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), "custode-tests", "does-not-exist.pfx");

        await using var host = await CustodeTestHost.StartAsync(sqliteFilePath: null, keyEncryptionCertificatePath: missingPath);
        var protector = host.Services.GetRequiredService<ISecretProtector>();

        var exception = Assert.Throws<SecretProtectionUnavailableException>(
            () => protector.Protect(Guid.NewGuid(), "value"));
        Assert.Contains("not found", exception.Message);
    }

    [Fact]
    public async Task MissingKeyEncryptionCertificate_DoesNotStoreKeyRingInDatabase()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), "custode-tests", "does-not-exist.pfx");

        await using var host = await CustodeTestHost.StartAsync(sqliteFilePath: null, keyEncryptionCertificatePath: missingPath);
        using var scope = host.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.Empty(await context.DataProtectionKeys.ToListAsync());
    }

    [Fact]
    public async Task UnreadableKeyRing_RefusesSecretOperations()
    {
        var databasePath = NewDatabasePath();
        var originalCertificate = Custode.Server.Tests.SecretProtection.TestKeyEncryptionCertificate.Create("kek-original");
        var replacementCertificate = Custode.Server.Tests.SecretProtection.TestKeyEncryptionCertificate.Create("kek-replacement");

        await using (var first = await CustodeTestHost.StartAsync(databasePath, originalCertificate))
        {
            first.Services.GetRequiredService<ISecretProtector>().Protect(Guid.NewGuid(), "value");
        }

        await using var second = await CustodeTestHost.StartAsync(databasePath, replacementCertificate);
        var protector = second.Services.GetRequiredService<ISecretProtector>();

        Assert.Throws<SecretProtectionUnavailableException>(() => protector.Protect(Guid.NewGuid(), "value"));
    }

    internal static string NewDatabasePath()
    {
        var directory = Path.Combine(Path.GetTempPath(), "custode-tests");
        Directory.CreateDirectory(directory);

        return Path.Combine(directory, $"custode-{Guid.NewGuid():N}.db");
    }
}
