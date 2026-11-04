// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using System.Net;
using Custode.Infrastructure.Audit;
using Custode.Server.Features.SecurityAudit;
using Custode.Server.Tests.Hosting;

namespace Custode.Server.Tests.Audit;

public sealed class SecurityAuditEndpointTests : IAsyncLifetime
{
    private VaultWorld _world = null!;

    public async Task InitializeAsync()
    {
        _world = await VaultWorld.CreateAsync(await CustodeTestHost.StartAsync());
    }

    public async Task DisposeAsync()
    {
        await _world.DisposeAsync();
    }

    [Fact]
    public async Task NonAdministrator_CannotReadOrVerifyTheAuditLog()
    {
        var query = await _world.Member.SendAsync(HttpMethod.Get, "/api/audit");
        var verify = await _world.Member.SendAsync(HttpMethod.Get, "/api/audit/verify");

        Assert.Equal(HttpStatusCode.Forbidden, query.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, verify.StatusCode);
    }

    [Fact]
    public async Task Administrator_SeesOnlyEntriesOfTheirOwnTenant()
    {
        var tenantBAdministrator = _world.As(_world.UserB, _world.TenantB, VaultWorld.AdministratorRole);
        var groupA = await VaultHttpSteps.CreateGroupAsync(_world.Administrator, "tenant-a-ops");
        var groupB = await VaultHttpSteps.CreateGroupAsync(tenantBAdministrator, "tenant-b-ops");

        var fromA = await VaultHttpSteps.AuditAsync(_world.Administrator);
        var fromB = await VaultHttpSteps.AuditAsync(tenantBAdministrator);

        Assert.Contains(fromA, entry => entry.TargetId == groupA.ToString("D"));
        Assert.DoesNotContain(fromA, entry => entry.TargetId == groupB.ToString("D"));
        Assert.Contains(fromB, entry => entry.TargetId == groupB.ToString("D"));
        Assert.DoesNotContain(fromB, entry => entry.TargetId == groupA.ToString("D"));
    }

    [Fact]
    public async Task Query_FiltersByActionAndOutcome_InSequenceOrder()
    {
        var groupId = await VaultHttpSteps.CreateGroupAsync(_world.Administrator, "ops");
        await VaultHttpSteps.CreateGroupAsync(_world.Administrator, "dev");
        await _world.Member.SendAsync(HttpMethod.Post, "/api/access/groups", new { name = "denied-attempt" });
        await _world.Outsider.SendAsync(HttpMethod.Get, $"/api/secrets/{Guid.NewGuid()}/value");

        var created = await VaultHttpSteps.AuditAsync(_world.Administrator, "?action=group%20created&outcome=succeeded");
        var denied = await VaultHttpSteps.AuditAsync(_world.Administrator, "?outcome=denied");

        Assert.Equal(2, created.Count);
        Assert.All(created, entry => Assert.Equal(AuditActions.GroupCreated, entry.Action));
        Assert.Equal(created.OrderBy(entry => entry.Sequence).Select(entry => entry.Sequence), created.Select(entry => entry.Sequence));
        Assert.Contains(created, entry => entry.TargetId == groupId.ToString("D"));
        Assert.All(denied, entry => Assert.Equal("denied", entry.Outcome));
    }

    [Fact]
    public async Task Query_FiltersByUser()
    {
        await VaultHttpSteps.CreateGroupAsync(_world.Administrator, "ops");
        await VaultHttpSteps.CreateGroupAsync(_world.Administrator, "dev");

        var entries = await VaultHttpSteps.AuditAsync(_world.Administrator, $"?userId={_world.AdministratorA.Id:D}");

        Assert.Equal(2, entries.Count);
        Assert.All(entries, entry => Assert.Equal(_world.AdministratorA.Id, entry.ActorUserId));
    }

    [Fact]
    public async Task Verify_IntactLog_ReportsEntryCount()
    {
        await VaultHttpSteps.CreateGroupAsync(_world.Administrator, "ops");
        var total = (await VaultHttpSteps.AuditAsync(_world.Administrator)).Count;

        var verification = await _world.Administrator.GetAsync<AuditVerificationResponse>("/api/audit/verify");

        Assert.True(verification.IsIntact);
        Assert.Equal(total, verification.EntriesVerified);
        Assert.Null(verification.FirstAlteredSequence);
    }
}
