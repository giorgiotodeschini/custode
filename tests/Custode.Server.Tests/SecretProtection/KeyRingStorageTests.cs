// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Custode.Infrastructure.Persistence;
using Custode.Server.Tests.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Custode.Server.Tests.SecretProtection;

public sealed class KeyRingStorageTests : IAsyncLifetime
{
    private CustodeTestHost _host = null!;

    public async Task InitializeAsync()
    {
        _host = await CustodeTestHost.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _host.DisposeAsync();
    }

    [Fact]
    public async Task KeyRing_IsPersistedInDatabase()
    {
        using var scope = _host.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var keys = await context.DataProtectionKeys.ToListAsync();

        Assert.NotEmpty(keys);
    }

    [Fact]
    public async Task KeyRing_StoredInDatabase_IsEncryptedWithKeyEncryptionKey()
    {
        using var scope = _host.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var keys = await context.DataProtectionKeys.ToListAsync();

        foreach (var key in keys)
        {
            Assert.Contains("encryptedSecret", key.Xml);
            Assert.DoesNotContain("<masterKey", key.Xml);
        }
    }
}
