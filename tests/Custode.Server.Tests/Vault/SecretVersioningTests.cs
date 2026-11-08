// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using System.Net;
using System.Net.Http.Json;
using Custode.Infrastructure.Audit;
using Custode.Infrastructure.Vault;
using Custode.Server.Tests.Hosting;
using Microsoft.EntityFrameworkCore;

namespace Custode.Server.Tests.Vault;

public sealed class SecretVersioningTests : IAsyncLifetime
{
    private VaultWorld _world = null!;

    public async Task InitializeAsync()
    {
        _world = await VaultWorld.CreateAsync(await CustodeTestHost.StartAsync());
        await VaultHttpSteps.CreateFolderAsync(_world.Administrator, "/vault");
        await VaultHttpSteps.CreateFolderAsync(_world.Administrator, "/other");
    }

    public async Task DisposeAsync()
    {
        await _world.DisposeAsync();
    }

    [Fact]
    public async Task Create_WritesVersionOne_AndTheValueCarriesItsVersionToken()
    {
        var id = await CreateSecretAsync("token", "v1-value");

        var versions = await _world.Administrator.GetAsync<List<VersionBody>>($"/api/secrets/{id}/versions");
        var value = await _world.Administrator.SendAsync(HttpMethod.Get, $"/api/secrets/{id}/value");

        Assert.Single(versions);
        Assert.Equal(1, versions[0].Number);
        Assert.Equal("created", versions[0].ChangeKind);
        Assert.Equal("\"1\"", value.Headers.ETag?.ToString());
    }

    [Fact]
    public async Task Update_WithTheCurrentVersionToken_CreatesTheNextVersion_AndReturnsTheNewToken()
    {
        var id = await CreateSecretAsync("token", "v1-value");

        var update = await _world.Administrator.SendAsync(HttpMethod.Put, $"/api/secrets/{id}", new { name = "token", type = "api-key", value = "v2-value" }, ifMatch: "\"1\"");

        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.Equal("\"2\"", update.Headers.ETag?.ToString());
        var versions = await _world.Administrator.GetAsync<List<VersionBody>>($"/api/secrets/{id}/versions");
        Assert.Equal([2, 1], versions.Select(version => version.Number));
        Assert.Equal("updated", versions[0].ChangeKind);
    }

    [Fact]
    public async Task Update_WithoutIfMatch_IsPreconditionRequired_AndNothingChanges()
    {
        var id = await CreateSecretAsync("token", "v1-value");

        var response = await _world.Administrator.SendAsync(HttpMethod.Put, $"/api/secrets/{id}", new { name = "token", type = "api-key", value = "changed" });

        Assert.Equal(HttpStatusCode.PreconditionRequired, response.StatusCode);
        Assert.Single(await _world.Administrator.GetAsync<List<VersionBody>>($"/api/secrets/{id}/versions"));
    }

    [Theory]
    [InlineData("\"5\"")]
    [InlineData("*")]
    [InlineData("W/\"1\"")]
    [InlineData("1")]
    public async Task Update_WithAStaleOrNonMatchingToken_IsPreconditionFailed_AndNothingChanges(string ifMatch)
    {
        var id = await CreateSecretAsync("token", "v1-value");

        var response = await _world.Administrator.SendAsync(HttpMethod.Put, $"/api/secrets/{id}", new { name = "token", type = "api-key", value = "changed" }, ifMatch: ifMatch);

        Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
        Assert.Single(await _world.Administrator.GetAsync<List<VersionBody>>($"/api/secrets/{id}/versions"));
    }

    [Fact]
    public async Task Move_CreatesNoVersion_AndKeepsTheVersionToken()
    {
        var id = await CreateSecretAsync("token", "v1-value");

        var move = await _world.Administrator.SendAsync(HttpMethod.Post, $"/api/secrets/{id}/move", new { folderPath = "/other" });
        var value = await _world.Administrator.SendAsync(HttpMethod.Get, $"/api/secrets/{id}/value");

        Assert.True(move.IsSuccessStatusCode);
        Assert.Single(await _world.Administrator.GetAsync<List<VersionBody>>($"/api/secrets/{id}/versions"));
        Assert.Equal("\"1\"", value.Headers.ETag?.ToString());
    }

    [Fact]
    public async Task VersionList_HasMetadataOnly_AndRequiresRead()
    {
        var id = await CreateSecretAsync("token", "plain-secret-value");
        var group = await VaultHttpSteps.CreateGroupAsync(_world.Administrator, "writers");
        await VaultHttpSteps.AddMemberAsync(_world.Administrator, group, _world.MemberA.Id);
        await VaultHttpSteps.SetGrantAsync(_world.Administrator, "/vault", "group", group, "write");

        var asWriter = await _world.Member.SendAsync(HttpMethod.Get, $"/api/secrets/{id}/versions");
        var listed = await _world.Administrator.SendAsync(HttpMethod.Get, $"/api/secrets/{id}/versions");

        Assert.Equal(HttpStatusCode.Forbidden, asWriter.StatusCode);
        Assert.DoesNotContain("plain-secret-value", await listed.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task VersionList_ForAnOutsider_IsNotFound()
    {
        var id = await CreateSecretAsync("token", "v1-value");

        var response = await _world.Outsider.SendAsync(HttpMethod.Get, $"/api/secrets/{id}/versions");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task OldVersionValue_IsReturnedToAReader_AndAudited()
    {
        var id = await CreateSecretAsync("token", "v1-value");
        await _world.Administrator.SendAsync(HttpMethod.Put, $"/api/secrets/{id}", new { name = "token", type = "api-key", value = "v2-value" }, ifMatch: "\"1\"");

        var old = await _world.Administrator.GetAsync<SecretValueBody>($"/api/secrets/{id}/versions/1/value");
        var unknown = await _world.Administrator.SendAsync(HttpMethod.Get, $"/api/secrets/{id}/versions/9/value");

        Assert.Equal("v1-value", old.Value);
        Assert.Equal(1, old.Version);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Contains(await VaultHttpSteps.AuditAsync(_world.Administrator), entry =>
            entry.Action == AuditActions.SecretValueRead && entry.SecondaryTargetType == "secret version");
    }

    [Fact]
    public async Task Rollback_CreatesANewVersion_RestoresTheValue_AndKeepsTheHistory()
    {
        var id = await CreateSecretAsync("token", "v1-value");
        await _world.Administrator.SendAsync(HttpMethod.Put, $"/api/secrets/{id}", new { name = "token", type = "api-key", value = "v2-value" }, ifMatch: "\"1\"");

        var rollback = await _world.Administrator.SendAsync(HttpMethod.Post, $"/api/secrets/{id}/versions/1/rollback", null, ifMatch: "\"2\"");

        Assert.Equal(HttpStatusCode.OK, rollback.StatusCode);
        Assert.Equal("\"3\"", rollback.Headers.ETag?.ToString());
        var value = await _world.Administrator.GetAsync<SecretValueBody>($"/api/secrets/{id}/value");
        Assert.Equal("v1-value", value.Value);
        var versions = await _world.Administrator.GetAsync<List<VersionBody>>($"/api/secrets/{id}/versions");
        Assert.Equal([3, 2, 1], versions.Select(version => version.Number));
        Assert.Equal("rolled back", versions[0].ChangeKind);
        Assert.Equal(1, versions[0].SourceVersion);
        Assert.True(await TenantContainsAsync(id, AuditActions.SecretRolledBack));
    }

    [Fact]
    public async Task Rollback_ToTheCurrentVersion_IsConflict()
    {
        var id = await CreateSecretAsync("token", "v1-value");

        var response = await _world.Administrator.SendAsync(HttpMethod.Post, $"/api/secrets/{id}/versions/1/rollback", null, ifMatch: "\"1\"");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Rollback_NeedsOnlyWrite_AndCanRestoreAValueTheCallerCannotRead()
    {
        var id = await CreateSecretAsync("token", "v1-value");
        await _world.Administrator.SendAsync(HttpMethod.Put, $"/api/secrets/{id}", new { name = "token", type = "api-key", value = "v2-value" }, ifMatch: "\"1\"");
        var group = await VaultHttpSteps.CreateGroupAsync(_world.Administrator, "rollback-writers");
        await VaultHttpSteps.AddMemberAsync(_world.Administrator, group, _world.MemberA.Id);
        await VaultHttpSteps.SetGrantAsync(_world.Administrator, "/vault", "group", group, "write");

        var rollback = await _world.Member.SendAsync(HttpMethod.Post, $"/api/secrets/{id}/versions/1/rollback", null, ifMatch: "\"2\"");
        var read = await _world.Member.SendAsync(HttpMethod.Get, $"/api/secrets/{id}/value");

        Assert.Equal(HttpStatusCode.OK, rollback.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, read.StatusCode);
    }

    [Fact]
    public async Task Rollback_WithoutWrite_IsForbidden_AndTheDenialIsAudited()
    {
        var id = await CreateSecretAsync("token", "v1-value");
        await _world.Administrator.SendAsync(HttpMethod.Put, $"/api/secrets/{id}", new { name = "token", type = "api-key", value = "v2-value" }, ifMatch: "\"1\"");
        var group = await VaultHttpSteps.CreateGroupAsync(_world.Administrator, "readers");
        await VaultHttpSteps.AddMemberAsync(_world.Administrator, group, _world.MemberA.Id);
        await VaultHttpSteps.SetGrantAsync(_world.Administrator, "/vault", "group", group, "read");

        var response = await _world.Member.SendAsync(HttpMethod.Post, $"/api/secrets/{id}/versions/1/rollback", null, ifMatch: "\"2\"");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains(await VaultHttpSteps.AuditAsync(_world.Administrator), entry =>
            entry.Action == AuditActions.SecretRolledBack && entry.Outcome == "denied");
    }

    [Fact]
    public async Task Rollback_WhoseNameNowBelongsToAnotherSecret_IsConflict()
    {
        var id = await CreateSecretAsync("token", "v1-value");
        await _world.Administrator.SendAsync(HttpMethod.Put, $"/api/secrets/{id}", new { name = "renamed", type = "api-key", value = "v2-value" }, ifMatch: "\"1\"");
        await CreateSecretAsync("token", "other");

        var response = await _world.Administrator.SendAsync(HttpMethod.Post, $"/api/secrets/{id}/versions/1/rollback", null, ifMatch: "\"2\"");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Deleting_ASecret_RemovesItsVersions()
    {
        var id = await CreateSecretAsync("token", "v1-value");
        await _world.Administrator.SendAsync(HttpMethod.Put, $"/api/secrets/{id}", new { name = "token", type = "api-key", value = "v2-value" }, ifMatch: "\"1\"");

        await _world.Administrator.SendAsync(HttpMethod.Delete, $"/api/secrets/{id}");

        await using var db = _world.Host.CreateTenantContext(_world.TenantA);
        Assert.False(await db.SecretVersions.AnyAsync(version => version.SecretId == id));
    }

    private async Task<Guid> CreateSecretAsync(string name, string value)
    {
        var response = await _world.Administrator.SendAsync(HttpMethod.Post, "/api/secrets", new { name, type = "api-key", folderPath = "/vault", value });
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<IdBody>())!.Id;
    }

    private async Task<bool> TenantContainsAsync(Guid secretId, string action)
    {
        await using var db = _world.Host.CreateTenantContext(_world.TenantA);

        return await db.AuditEntries.AnyAsync(entry => entry.Action == action && entry.TargetId == secretId.ToString("D"));
    }

    private sealed record IdBody(Guid Id);

    private sealed record VersionBody(int Number, string Name, string? FileName, string ChangeKind, DateTimeOffset CreatedAtUtc, Guid CreatedByUserId, int? SourceVersion);

    private sealed record SecretValueBody(Guid Id, string Name, string Type, string FolderPath, string? Value, int Version);
}
