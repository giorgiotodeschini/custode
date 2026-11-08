// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using System.Net;
using System.Text;
using System.Net.Http.Json;
using Custode.Infrastructure.Vault;
using Custode.Server.Tests.Hosting;
using Microsoft.EntityFrameworkCore;

namespace Custode.Server.Tests.Vault;

public sealed class StructuredAndKeyValueSecretTests : IAsyncLifetime
{
    private VaultWorld _world = null!;

    public async Task InitializeAsync()
    {
        _world = await VaultWorld.CreateAsync(await CustodeTestHost.StartAsync());
        await VaultHttpSteps.CreateFolderAsync(_world.Administrator, "/apps");
    }

    public async Task DisposeAsync()
    {
        await _world.DisposeAsync();
    }

    [Fact]
    public async Task Structured_StoresNamedFields_ReturnsThemInOrder_AndHidesThemFromListings()
    {
        var created = await _world.Administrator.SendRawJsonAsync(HttpMethod.Post, "/api/secrets",
            """{"name":"db","type":"structured","folderPath":"/apps","fields":{"host":"db.internal","password":"s3cret!"}}""");

        Assert.True(created.IsSuccessStatusCode);
        var id = (await created.Content.ReadFromJsonAsync<SecretMetadataBody>())!.Id;

        var value = await _world.Administrator.GetAsync<ValueBody>($"/api/secrets/{id}/value");
        Assert.Equal(["host", "password"], value.Fields!.Select(field => field.Key));
        Assert.Equal("s3cret!", value.Fields![1].Value);

        var listing = await _world.Administrator.SendAsync(HttpMethod.Get, "/api/secrets?folderPath=/apps");
        var listed = await listing.Content.ReadAsStringAsync();
        Assert.Contains("structured", listed);
        Assert.DoesNotContain("s3cret!", listed);
    }

    [Fact]
    public async Task Structured_RepeatedFieldName_IsRejected()
    {
        var response = await _world.Administrator.SendRawJsonAsync(HttpMethod.Post, "/api/secrets",
            """{"name":"dup","type":"structured","folderPath":"/apps","fields":{"host":"a","host":"b"}}""");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Structured_NonStringValue_IsRejected()
    {
        var response = await _world.Administrator.SendRawJsonAsync(HttpMethod.Post, "/api/secrets",
            """{"name":"num","type":"structured","folderPath":"/apps","fields":{"port":5432}}""");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Structured_LimitsAreEnforced()
    {
        var tooManyFields = "{" + string.Join(",", Enumerable.Range(0, 101).Select(index => $"\"f{index}\":\"v\"")) + "}";
        var longName = "{\"" + new string('n', 129) + "\":\"v\"}";
        var longValue = "{\"k\":\"" + new string('v', 4097) + "\"}";

        Assert.Equal(HttpStatusCode.BadRequest, (await SendStructuredAsync("many", tooManyFields)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendStructuredAsync("long-name", longName)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendStructuredAsync("long-value", longValue)).StatusCode);
    }

    [Fact]
    public async Task KeyValue_KeepsTheSubmittedOrder()
    {
        var created = await _world.Administrator.SendAsync(HttpMethod.Post, "/api/secrets", new
        {
            name = "env",
            type = "key-value",
            folderPath = "/apps",
            pairs = new[] { new { key = "ZETA", value = "1" }, new { key = "ALPHA", value = "" }, new { key = "MID", value = "3" } },
        });

        Assert.True(created.IsSuccessStatusCode);
        var id = (await created.Content.ReadFromJsonAsync<SecretMetadataBody>())!.Id;

        var value = await _world.Administrator.GetAsync<ValueBody>($"/api/secrets/{id}/value");
        Assert.Equal(["ZETA", "ALPHA", "MID"], value.Pairs!.Select(pair => pair.Key));
        Assert.Equal("", value.Pairs![1].Value);
    }

    [Fact]
    public async Task KeyValue_DuplicateKey_EmptyKey_AndTooManyPairs_AreRejected()
    {
        var duplicate = await _world.Administrator.SendAsync(HttpMethod.Post, "/api/secrets", new
        {
            name = "dup", type = "key-value", folderPath = "/apps",
            pairs = new[] { new { key = "A", value = "1" }, new { key = "A", value = "2" } },
        });
        var empty = await _world.Administrator.SendAsync(HttpMethod.Post, "/api/secrets", new
        {
            name = "empty", type = "key-value", folderPath = "/apps",
            pairs = new[] { new { key = "", value = "1" } },
        });
        var tooMany = await _world.Administrator.SendAsync(HttpMethod.Post, "/api/secrets", new
        {
            name = "many", type = "key-value", folderPath = "/apps",
            pairs = Enumerable.Range(0, 201).Select(index => new { key = $"k{index}", value = "v" }).ToArray(),
        });

        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooMany.StatusCode);
    }

    [Fact]
    public async Task StructuredAndKeyValue_AreStoredProtected_NotAsPlaintext()
    {
        await _world.Administrator.SendRawJsonAsync(HttpMethod.Post, "/api/secrets",
            """{"name":"sealed","type":"structured","folderPath":"/apps","fields":{"token":"plain-token-value"}}""");

        await using var db = _world.Host.CreateTenantContext(_world.TenantA);
        var stored = await db.Secrets.SingleAsync(secret => secret.Name == "sealed");

        Assert.DoesNotContain("plain-token-value", stored.ProtectedPayload);
        Assert.Equal(SecretType.Structured, stored.Type);
    }

    [Fact]
    public async Task Structured_CanBeUpdated_WithItsVersionToken()
    {
        var created = await _world.Administrator.SendRawJsonAsync(HttpMethod.Post, "/api/secrets",
            """{"name":"edit","type":"structured","folderPath":"/apps","fields":{"host":"old"}}""");
        var id = (await created.Content.ReadFromJsonAsync<SecretMetadataBody>())!.Id;

        var update = await _world.Administrator.SendRawJsonAsync(HttpMethod.Put, $"/api/secrets/{id}",
            """{"name":"edit","type":"structured","fields":{"host":"new","port":"5432"}}""", ifMatch: "\"1\"");
        var value = await _world.Administrator.GetAsync<ValueBody>($"/api/secrets/{id}/value");

        Assert.True(update.IsSuccessStatusCode);
        Assert.Equal(["host", "port"], value.Fields!.Select(field => field.Key));
        Assert.Equal("new", value.Fields![0].Value);
    }

    private Task<HttpResponseMessage> SendStructuredAsync(string name, string fieldsJson)
    {
        return _world.Administrator.SendRawJsonAsync(HttpMethod.Post, "/api/secrets",
            $"{{\"name\":\"{name}\",\"type\":\"structured\",\"folderPath\":\"/apps\",\"fields\":{fieldsJson}}}");
    }

    private sealed record SecretMetadataBody(Guid Id, string Name, string Type);

    private sealed record ValueBody(Guid Id, string Name, string Type, List<PairBody>? Fields, List<PairBody>? Pairs);

    private sealed record PairBody(string Key, string Value);
}
