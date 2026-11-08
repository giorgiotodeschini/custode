// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using System.Net;
using Custode.Server.Features.SecurityAudit;
using Custode.Server.Tests.Hosting;

namespace Custode.Server.Tests.Audit;

public sealed class AuditPagingTests : IAsyncLifetime
{
    private VaultWorld _world = null!;

    public async Task InitializeAsync()
    {
        _world = await VaultWorld.CreateAsync(await CustodeTestHost.StartAsync());
        await VaultHttpSteps.CreateFolderAsync(_world.Administrator, "/paged");
        for (var index = 0; index < 4; index++)
        {
            await VaultHttpSteps.CreateGroupAsync(_world.Administrator, $"group-{index}");
        }
    }

    public async Task DisposeAsync()
    {
        await _world.DisposeAsync();
    }

    [Fact]
    public async Task HashLinks_AreExposed_AndEachEntryPointsAtItsPredecessor()
    {
        var entries = (await _world.Administrator.GetAsync<AuditPageResponse>("/api/audit?limit=500")).Entries;

        Assert.Equal("", entries[0].PreviousHash);
        for (var index = 1; index < entries.Count; index++)
        {
            Assert.Equal(entries[index - 1].Hash, entries[index].PreviousHash);
            Assert.Matches("^[0-9a-f]{64}$", entries[index].Hash);
        }
    }

    [Fact]
    public async Task Pages_NeitherSkipNorRepeat_AndTheLastPageHasNoCursor()
    {
        var everything = await VaultHttpSteps.AuditAsync(_world.Administrator);
        var collected = new List<long>();
        long? cursor = null;
        var pages = 0;

        do
        {
            var page = await _world.Administrator.GetAsync<AuditPageResponse>(
                "/api/audit?limit=3" + (cursor is null ? string.Empty : $"&afterSequence={cursor}"));
            collected.AddRange(page.Entries.Select(entry => entry.Sequence));
            cursor = page.NextAfterSequence;
            pages++;
        }
        while (cursor is not null);

        Assert.True(pages > 1);
        Assert.Equal(everything.Select(entry => entry.Sequence), collected);
        Assert.Equal(collected.OrderBy(sequence => sequence), collected);
    }

    [Fact]
    public async Task DefaultPageSize_IsOneHundred()
    {
        var page = await _world.Administrator.GetAsync<AuditPageResponse>("/api/audit");

        Assert.True(page.Entries.Count <= 100);
    }

    [Fact]
    public async Task LimitOutOfRange_IsBadRequest()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await _world.Administrator.SendAsync(HttpMethod.Get, "/api/audit?limit=501")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _world.Administrator.SendAsync(HttpMethod.Get, "/api/audit?limit=0")).StatusCode);
    }

    [Fact]
    public async Task NegativeCursor_IsBadRequest()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await _world.Administrator.SendAsync(HttpMethod.Get, "/api/audit?afterSequence=-1")).StatusCode);
    }

    [Fact]
    public async Task Filters_CombineWithTheCursor()
    {
        var denied = await VaultHttpSteps.AuditAsync(_world.Administrator, "?outcome=succeeded");
        var page = await _world.Administrator.GetAsync<AuditPageResponse>(
            $"/api/audit?outcome=succeeded&limit=2&afterSequence={denied[0].Sequence}");

        Assert.All(page.Entries, entry => Assert.Equal("succeeded", entry.Outcome));
        Assert.All(page.Entries, entry => Assert.True(entry.Sequence > denied[0].Sequence));
    }
}
