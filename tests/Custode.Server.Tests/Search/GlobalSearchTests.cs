// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using System.Net;
using System.Net.Http.Json;
using Custode.Infrastructure.Persistence;
using Custode.Infrastructure.Vault;
using Custode.Server.Tests.Hosting;
using Microsoft.EntityFrameworkCore;

namespace Custode.Server.Tests.Search;

public sealed class GlobalSearchTests : IAsyncLifetime
{
    private VaultWorld _world = null!;

    public async Task InitializeAsync()
    {
        _world = await VaultWorld.CreateAsync(await CustodeTestHost.StartAsync());
        await VaultHttpSteps.CreateFolderAsync(_world.Administrator, "/production");
        await VaultHttpSteps.CreateFolderAsync(_world.Administrator, "/private");
        await CreateSecretAsync("/production", "aws-root", "AKIA-PLAIN-VALUE");
        await CreateSecretAsync("/private", "aws-private", "hidden-value");
    }

    public async Task DisposeAsync()
    {
        await _world.DisposeAsync();
    }

    [Fact]
    public async Task Search_FindsASecretByName_WithItsFolderPath_AndNoValue()
    {
        var group = await VaultHttpSteps.CreateGroupAsync(_world.Administrator, "prod-readers");
        await VaultHttpSteps.AddMemberAsync(_world.Administrator, group, _world.MemberA.Id);
        await VaultHttpSteps.SetGrantAsync(_world.Administrator, "/production", "group", group, "read");

        var response = await _world.Member.SendAsync(HttpMethod.Get, "/api/search?query=AWS");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("aws-root", body);
        Assert.Contains("/production", body);
        Assert.DoesNotContain("AKIA-PLAIN-VALUE", body);
        Assert.DoesNotContain("aws-private", body);
    }

    [Fact]
    public async Task Search_DoesNotReveal_SecretsTheCallerCannotRead()
    {
        var response = await _world.Outsider.SendAsync(HttpMethod.Get, "/api/search?query=aws");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("aws-root", body);
        Assert.DoesNotContain("aws-private", body);
    }

    [Fact]
    public async Task Search_ReturnsAFolderForWriteOnly_ButNotASecretWithoutRead()
    {
        var group = await VaultHttpSteps.CreateGroupAsync(_world.Administrator, "prod-writers");
        await VaultHttpSteps.AddMemberAsync(_world.Administrator, group, _world.MemberA.Id);
        await VaultHttpSteps.SetGrantAsync(_world.Administrator, "/production", "group", group, "write");

        var body = await (await _world.Member.SendAsync(HttpMethod.Get, "/api/search?query=production")).Content.ReadAsStringAsync();
        var secrets = await (await _world.Member.SendAsync(HttpMethod.Get, "/api/search?query=aws-root")).Content.ReadAsStringAsync();

        Assert.Contains("/production", body);
        Assert.DoesNotContain("aws-root", secrets);
    }

    [Fact]
    public async Task Search_WildcardCharactersMatchLiterally()
    {
        var body = await (await _world.Administrator.SendAsync(HttpMethod.Get, "/api/search?query=%25")).Content.ReadAsStringAsync();

        Assert.DoesNotContain("aws-root", body);
        Assert.Contains("\"secrets\":[]", body.Replace(" ", string.Empty));
    }

    [Fact]
    public async Task Search_RejectsAnEmptyQuery_AndALimitAboveTheMaximum()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await _world.Administrator.SendAsync(HttpMethod.Get, "/api/search?query=%20%20")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _world.Administrator.SendAsync(HttpMethod.Get, "/api/search?query=a&limit=51")).StatusCode);
    }

    [Fact]
    public async Task Search_ReportsTruncation_WhenTheCandidateLimitIsReached()
    {
        await using (var db = _world.Host.CreateTenantContext(_world.TenantA))
        {
            var folder = await db.Folders.SingleAsync(entity => entity.Path == "/production");
            for (var index = 0; index < 5001; index++)
            {
                db.Secrets.Add(new Secret
                {
                    Id = Guid.NewGuid(),
                    TenantId = _world.TenantA,
                    FolderId = folder.Id,
                    Name = "bulk-" + index.ToString("D5"),
                    Type = SecretType.ApiKey,
                    ProtectedPayload = "payload",
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                    ConcurrencyStamp = Guid.NewGuid(),
                });
            }
            await db.SaveChangesAsync();
        }

        var body = await (await _world.Administrator.SendAsync(HttpMethod.Get, "/api/search?query=no-such-name")).Content.ReadAsStringAsync();

        Assert.Contains("\"truncated\":true", body.Replace(" ", string.Empty));
    }

    private async Task CreateSecretAsync(string folderPath, string name, string value)
    {
        var response = await _world.Administrator.SendAsync(HttpMethod.Post, "/api/secrets", new { name, type = "api-key", folderPath, value });
        response.EnsureSuccessStatusCode();
    }
}
