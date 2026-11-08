// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using System.Net;
using System.Net.Http.Json;
using Custode.Server.Tests.Hosting;
using Custode.Server.Tests.SecretProtection;

namespace Custode.Server.Tests.EndToEnd;

public sealed class PostgreSqlVersioningTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>
{
    [Fact]
    public async Task ConcurrentUpdatesWithTheSameVersionToken_OneSucceeds_TheOtherIsPreconditionFailed_OnPostgreSql()
    {
        var connectionString = await fixture.CreateCleanDatabaseAsync();
        await using var host = await CustodeTestHost.StartPostgreSqlAsync(connectionString, TestKeyEncryptionCertificate.Shared);
        await using var world = await VaultWorld.CreateAsync(host);
        await VaultHttpSteps.CreateFolderAsync(world.Administrator, "/race");

        var created = await world.Administrator.SendAsync(HttpMethod.Post, "/api/secrets", new { name = "contended", type = "api-key", folderPath = "/race", value = "v1" });
        created.EnsureSuccessStatusCode();

        var id = (await created.Content.ReadFromJsonAsync<IdBody>())!.Id;

        var responses = await Task.WhenAll(
            world.Administrator.SendAsync(HttpMethod.Put, $"/api/secrets/{id}", new { name = "contended", type = "api-key", value = "from-a" }, ifMatch: "\"1\""),
            world.Administrator.SendAsync(HttpMethod.Put, $"/api/secrets/{id}", new { name = "contended", type = "api-key", value = "from-b" }, ifMatch: "\"1\""),
            world.Administrator.SendAsync(HttpMethod.Put, $"/api/secrets/{id}", new { name = "contended", type = "api-key", value = "from-c" }, ifMatch: "\"1\""));

        var statuses = responses.Select(response => response.StatusCode).OrderBy(status => status).ToArray();
        Assert.Equal(HttpStatusCode.OK, statuses[0]);
        Assert.All(statuses.Skip(1), status => Assert.Equal(HttpStatusCode.PreconditionFailed, status));

        var versions = await world.Administrator.GetAsync<List<VersionNumber>>($"/api/secrets/{id}/versions");
        Assert.Equal([2, 1], versions.Select(version => version.Number));
    }

    private sealed record IdBody(Guid Id);

    private sealed record VersionNumber(int Number);
}
