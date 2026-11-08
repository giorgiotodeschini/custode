// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Custode.Infrastructure.Persistence;
using Custode.Server.Tests.Hosting;
using Microsoft.EntityFrameworkCore;

namespace Custode.Server.Tests.Import;

public sealed class ImportAtomicityTests
{
    [Fact]
    public async Task FailureWhileStoringTheLastRow_LeavesNoEarlierRowStored()
    {
        var host = await CustodeTestHost.StartAsync(new TestHostSettings(ConfigureDatabase: options => options.AddInterceptors(new PoisonRowInterceptor())));
        await using var world = await VaultWorld.CreateAsync(host);
        await VaultHttpSteps.CreateFolderAsync(world.Administrator, "/atomic");

        await Assert.ThrowsAnyAsync<Exception>(() => world.Administrator.SendAsync(HttpMethod.Post, "/api/secrets/imports?dryRun=false", new
        {
            createMissingFolders = false,
            items = new object[]
            {
                new { name = "alpha", type = "api-key", folderPath = "/atomic", value = "a" },
                new { name = "beta", type = "api-key", folderPath = "/atomic", value = "b" },
                new { name = PoisonRowInterceptor.PoisonName, type = "api-key", folderPath = "/atomic", value = "c" },
            },
        }));

        await using var db = host.CreateTenantContext(world.TenantA);
        Assert.False(await db.Secrets.AnyAsync(secret => secret.Name == "alpha" || secret.Name == "beta" || secret.Name == PoisonRowInterceptor.PoisonName));
        Assert.False(await db.SecretVersions.AnyAsync(version => version.Name == "alpha" || version.Name == "beta"));
    }
}
