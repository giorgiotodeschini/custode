// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Custode.Infrastructure.Administration;
using Custode.Infrastructure.Sessions;
using Custode.Infrastructure.Vault;
using Custode.Server.Tests.Hosting;
using Microsoft.EntityFrameworkCore;

namespace Custode.Server.Tests.Persistence;

public sealed class ServerPrerequisitesTenantFilterTests : IAsyncLifetime
{
    private CustodeTestHost _host = null!;
    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        _host = await CustodeTestHost.StartAsync();

        await using (var db = _host.CreateTenantContext(_tenantA))
        {
            db.Sessions.Add(new Session { Id = Guid.NewGuid(), TenantId = _tenantA, UserId = Guid.NewGuid(), StartedAtUtc = DateTimeOffset.UtcNow, LastActivityAtUtc = DateTimeOffset.UtcNow });
            db.TenantPolicies.Add(new TenantPolicy { TenantId = _tenantA });
            await db.SaveChangesAsync();
        }

        await using (var db = _host.CreateTenantContext(_tenantB))
        {
            db.Sessions.Add(new Session { Id = Guid.NewGuid(), TenantId = _tenantB, UserId = Guid.NewGuid(), StartedAtUtc = DateTimeOffset.UtcNow, LastActivityAtUtc = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }
    }

    public async Task DisposeAsync()
    {
        await _host.DisposeAsync();
    }

    [Fact]
    public async Task Sessions_AreReadOnlyWithinTheirOwnTenant()
    {
        await using var db = _host.CreateTenantContext(_tenantB);

        Assert.Equal(1, await db.Sessions.CountAsync());
        Assert.All(await db.Sessions.ToListAsync(), session => Assert.Equal(_tenantB, session.TenantId));
    }

    [Fact]
    public async Task TenantPolicies_AreInvisibleToAnotherTenant()
    {
        await using var db = _host.CreateTenantContext(_tenantB);

        Assert.False(await db.TenantPolicies.AnyAsync());
    }

    [Fact]
    public async Task SecretVersions_AreInvisibleToAnotherTenant()
    {
        var secretId = Guid.NewGuid();

        await using (var owner = _host.CreateTenantContext(_tenantA))
        {
            var folder = new Folder { Id = Guid.NewGuid(), TenantId = _tenantA, Path = "/v", CreatedAtUtc = DateTimeOffset.UtcNow };
            owner.Folders.Add(folder);
            owner.Secrets.Add(new Secret
            {
                Id = secretId,
                TenantId = _tenantA,
                FolderId = folder.Id,
                Name = "versioned",
                Type = SecretType.ApiKey,
                ProtectedPayload = "payload",
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
                ConcurrencyStamp = Guid.NewGuid(),
            });
            owner.SecretVersions.Add(new SecretVersion
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantA,
                SecretId = secretId,
                Number = 1,
                Name = "versioned",
                ProtectedPayload = "payload",
                ChangeKind = SecretVersionChangeKind.Created,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                CreatedByUserId = Guid.Empty,
            });
            await owner.SaveChangesAsync();
        }

        await using var other = _host.CreateTenantContext(_tenantB);

        Assert.False(await other.SecretVersions.AnyAsync());
    }
}
