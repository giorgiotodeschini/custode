// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using System.Net;
using System.Net.Http.Json;
using Custode.Infrastructure.Audit;
using Custode.Infrastructure.Persistence;
using Custode.Server.Tests.Hosting;
using Microsoft.EntityFrameworkCore;

namespace Custode.Server.Tests.Import;

public sealed class BulkImportTests : IAsyncLifetime
{
    private VaultWorld _world = null!;

    public async Task InitializeAsync()
    {
        _world = await VaultWorld.CreateAsync(await CustodeTestHost.StartAsync());
        await VaultHttpSteps.CreateFolderAsync(_world.Administrator, "/bulk");
    }

    public async Task DisposeAsync()
    {
        await _world.DisposeAsync();
    }

    [Fact]
    public async Task DryRun_ReportsEveryRow_AndWritesNothing()
    {
        var before = await CountsAsync();

        var response = await PostImportAsync(dryRun: true, new object[]
        {
            Row("ok", "api-key", "/bulk", value: "v"),
            Row("bad-type", "magic", "/bulk", value: "v"),
        });
        var report = await response.Content.ReadFromJsonAsync<ReportBody>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(report!.DryRun);
        Assert.Equal(["valid", "invalid"], report.Rows.Select(row => row.Status));
        Assert.Equal(before, await CountsAsync());
    }

    [Fact]
    public async Task Commit_WithOnlyValidRows_StoresEverySecret_AndAuditsEachOne_AndTheBatch()
    {
        var response = await PostImportAsync(dryRun: false, new object[]
        {
            Row("alpha", "api-key", "/bulk", value: "a"),
            Row("beta", "pat", "/bulk", value: "b"),
            Row("gamma", "structured", "/bulk", fields: """{"host":"h"}"""),
        });
        var report = await response.Content.ReadFromJsonAsync<ReportBody>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(report!.Committed);
        Assert.Equal(3, report.Created);
        Assert.NotNull(report.BatchId);
        var entries = await VaultHttpSteps.AuditAsync(_world.Administrator);
        Assert.Equal(3, entries.Count(entry => entry.Action == AuditActions.SecretCreated && entry.Outcome == "succeeded" && entry.SecondaryTargetType == "folder" && entry.TargetType == "secret"));
        Assert.Contains(entries, entry => entry.Action == AuditActions.SecretsImported && entry.TargetId == report.BatchId!.Value.ToString("D"));
    }

    [Fact]
    public async Task Commit_WithOneInvalidRow_StoresNothing()
    {
        var before = await CountsAsync();

        var response = await PostImportAsync(dryRun: false, new object[]
        {
            Row("first", "api-key", "/bulk", value: "a"),
            Row("second", "api-key", "/bulk", value: "b"),
            Row("last", "api-key", "/bulk", value: null),
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(before, await CountsAsync());
    }

    [Fact]
    public async Task Commit_WithAForbiddenRow_IsForbidden_StoresNothing_AndTheDenialIsAudited()
    {
        var group = await VaultHttpSteps.CreateGroupAsync(_world.Administrator, "bulk-readers");
        await VaultHttpSteps.AddMemberAsync(_world.Administrator, group, _world.MemberA.Id);
        await VaultHttpSteps.SetGrantAsync(_world.Administrator, "/bulk", "group", group, "read");
        var before = await CountsAsync();

        var response = await PostImportAsync(_world.Member, dryRun: false, new object[] { Row("denied", "api-key", "/bulk", value: "v") });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(before.Secrets, (await CountsAsync()).Secrets);
        Assert.Contains(await VaultHttpSteps.AuditAsync(_world.Administrator), entry =>
            entry.Action == AuditActions.SecretsImported && entry.Outcome == "denied");
    }

    [Fact]
    public async Task TargetFolderWithoutAnyPermission_IsFolderNotFound_LikeAMissingOne()
    {
        var response = await PostImportAsync(_world.Outsider, dryRun: true, new object[]
        {
            Row("unseen", "api-key", "/bulk", value: "v"),
            Row("missing", "api-key", "/nowhere", value: "v"),
        });
        var report = await response.Content.ReadFromJsonAsync<ReportBody>();

        Assert.Equal(["folder-not-found", "folder-not-found"], report!.Rows.Select(row => row.Status));
    }

    [Fact]
    public async Task MissingFolders_AreCreatedOnlyWhenRequested_AndAudited()
    {
        var refused = await PostImportAsync(dryRun: true, new object[] { Row("deep", "api-key", "/bulk/a/b", value: "v") }, createMissingFolders: false);
        Assert.Equal("folder-not-found", (await refused.Content.ReadFromJsonAsync<ReportBody>())!.Rows[0].Status);

        var created = await PostImportAsync(dryRun: false, new object[] { Row("deep", "api-key", "/bulk/a/b", value: "v") }, createMissingFolders: true);

        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var folders = await _world.Administrator.GetAsync<List<FolderBody>>("/api/access/folders");
        Assert.Contains(folders, folder => folder.Path == "/bulk/a/b");
        Assert.Contains(await VaultHttpSteps.AuditAsync(_world.Administrator), entry => entry.Action == AuditActions.FolderCreated);
    }

    [Fact]
    public async Task DuplicateNames_InTheBatchOrInTheFolder_AreConflicts()
    {
        await _world.Administrator.SendAsync(HttpMethod.Post, "/api/secrets", new { name = "taken", type = "api-key", folderPath = "/bulk", value = "v" });

        var response = await PostImportAsync(dryRun: true, new object[]
        {
            Row("taken", "api-key", "/bulk", value: "v"),
            Row("twin", "api-key", "/bulk", value: "v"),
            Row("twin", "api-key", "/bulk", value: "w"),
        });
        var report = await response.Content.ReadFromJsonAsync<ReportBody>();

        Assert.Equal(["conflict", "conflict", "conflict"], report!.Rows.Select(row => row.Status));
    }

    [Fact]
    public async Task ReportsNeverContainAValue_EvenForARowThatCarriesOne()
    {
        var response = await PostImportAsync(dryRun: true, new object[]
        {
            new { name = "leaky", type = "magic", folderPath = "/bulk", password = "PLAIN-SECRET-PASSWORD", value = "PLAIN-SECRET-VALUE" },
        });

        Assert.DoesNotContain("PLAIN-SECRET", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task BatchesOverTheRowLimit_AreRejected()
    {
        var rows = Enumerable.Range(0, 501).Select(index => Row("r" + index, "api-key", "/bulk", value: "v")).ToArray();

        var response = await PostImportAsync(dryRun: true, rows);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task BodyOverTheConfiguredMaximum_IsPayloadTooLarge()
    {
        var padding = new string('x', 6 * 1024 * 1024);

        var response = await _world.Administrator.SendRawJsonAsync(HttpMethod.Post, "/api/secrets/imports?dryRun=true",
            "{\"items\":[],\"pad\":\"" + padding + "\"}");

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    private static object Row(string name, string type, string folderPath, string? value = null, string? fields = null)
    {
        return new
        {
            name,
            type,
            folderPath,
            value,
            fields = fields is null ? (object?)null : System.Text.Json.JsonDocument.Parse(fields).RootElement.Clone(),
        };
    }

    private Task<HttpResponseMessage> PostImportAsync(bool dryRun, object[] rows, bool createMissingFolders = false)
    {
        return PostImportAsync(_world.Administrator, dryRun, rows, createMissingFolders);
    }

    private static Task<HttpResponseMessage> PostImportAsync(ApiClient client, bool dryRun, object[] rows, bool createMissingFolders = false)
    {
        return client.SendAsync(HttpMethod.Post, $"/api/secrets/imports?dryRun={(dryRun ? "true" : "false")}", new
        {
            createMissingFolders,
            items = rows,
        });
    }

    private async Task<(int Secrets, int Audit)> CountsAsync()
    {
        await using var db = _world.Host.CreateTenantContext(_world.TenantA);

        return (await db.Secrets.CountAsync(), await db.AuditEntries.CountAsync());
    }

    private sealed record ReportBody(Guid? BatchId, bool DryRun, bool Committed, int Created, List<RowBody> Rows);

    private sealed record RowBody(int Index, string Status, string? Message);

    private sealed record FolderBody(string Path, string? ParentPath, bool InheritsPermissions);
}
