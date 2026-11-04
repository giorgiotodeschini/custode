// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Custode.Infrastructure.Audit;
using Custode.Infrastructure.Vault;
using Custode.Server.Features.SecretManagement.Secrets;
using Custode.Server.Tests.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Custode.Server.Tests.Vault;

public sealed class SecretManagementEndpointTests : IAsyncLifetime
{
    private const string Plaintext = "plaintext-secret-VALUE-7731";

    private VaultWorld _world = null!;
    private Guid _group;

    public async Task InitializeAsync()
    {
        _world = await VaultWorld.CreateAsync(await CustodeTestHost.StartAsync());
        await VaultHttpSteps.CreateFolderAsync(_world.Administrator, "/production");
        await VaultHttpSteps.CreateFolderAsync(_world.Administrator, "/production/aws");
        _group = await VaultHttpSteps.CreateGroupAsync(_world.Administrator, "ops");
        await VaultHttpSteps.AddMemberAsync(_world.Administrator, _group, _world.MemberA.Id);
    }

    public async Task DisposeAsync()
    {
        await _world.DisposeAsync();
    }

    [Fact]
    public async Task UsernamePassword_WithoutPassword_IsRejected()
    {
        var response = await _world.Administrator.SendAsync(HttpMethod.Post, "/api/secrets", new
        {
            name = "db",
            type = "username-password",
            folderPath = "/production",
            username = "svc",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PatAndApiKey_RequireAValue()
    {
        var pat = await _world.Administrator.SendAsync(HttpMethod.Post, "/api/secrets", new { name = "github", type = "pat", folderPath = "/production", value = "" });
        var apiKey = await _world.Administrator.SendAsync(HttpMethod.Post, "/api/secrets", new { name = "stripe", type = "api-key", folderPath = "/production", value = "sk_live_1" });

        Assert.Equal(HttpStatusCode.BadRequest, pat.StatusCode);
        Assert.Equal(HttpStatusCode.OK, apiKey.StatusCode);
    }

    [Theory]
    [InlineData("server.exe")]
    [InlineData("server.txt")]
    [InlineData("noextension")]
    public async Task FileSecret_WithUnsupportedExtension_IsRejected(string fileName)
    {
        var response = await _world.Administrator.SendAsync(HttpMethod.Post, "/api/secrets", new
        {
            name = "cert",
            type = "file",
            folderPath = "/production",
            fileName,
            contentBase64 = Convert.ToBase64String(new byte[] { 1, 2, 3 }),
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("cert.crt")]
    [InlineData("cert.cer")]
    [InlineData("cert.pem")]
    [InlineData("cert.pfx")]
    [InlineData("cert.p12")]
    [InlineData("cert.key")]
    public async Task FileSecret_WithSupportedExtension_IsAccepted(string fileName)
    {
        var response = await _world.Administrator.SendAsync(HttpMethod.Post, "/api/secrets", new
        {
            name = fileName,
            type = "file",
            folderPath = "/production",
            fileName,
            contentBase64 = Convert.ToBase64String(new byte[] { 1, 2, 3 }),
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task FileSecret_ExceedingTheMaximumSize_IsRejected()
    {
        var oversized = new byte[(int)SecretStorageOptions.DefaultMaxFileSizeBytes + 1];

        var response = await _world.Administrator.SendAsync(HttpMethod.Post, "/api/secrets", new
        {
            name = "big.pfx",
            type = "file",
            folderPath = "/production",
            fileName = "big.pfx",
            contentBase64 = Convert.ToBase64String(oversized),
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task StoredPayload_IsEncrypted_AndReadsBackAsThePlaintext()
    {
        var created = await CreateApiKeyAsync(_world.Administrator, "/production", "stripe", Plaintext);

        await using var db = _world.Host.CreateTenantContext(_world.TenantA);
        var stored = await db.Secrets.SingleAsync(secret => secret.Id == created.Id);
        var value = await _world.Administrator.GetAsync<SecretValueResponse>($"/api/secrets/{created.Id}/value");

        Assert.DoesNotContain(Plaintext, stored.ProtectedPayload);
        Assert.Equal(Plaintext, value.Value);
    }

    [Fact]
    public async Task FolderCreation_NormalizesPath_RejectsDuplicatesAndRelativePaths()
    {
        var normalized = await _world.Administrator.SendAsync(HttpMethod.Post, "/api/folders", new { path = "/production//aws/" });
        var duplicate = await _world.Administrator.SendAsync(HttpMethod.Post, "/api/folders", new { path = "/production" });
        var relative = await _world.Administrator.SendAsync(HttpMethod.Post, "/api/folders", new { path = "production/aws" });
        var created = await _world.Administrator.SendAsync(HttpMethod.Post, "/api/folders", new { path = "/production/eu" });

        var body = await created.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.Conflict, normalized.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, relative.StatusCode);
        Assert.Equal("/production/eu", body.GetProperty("path").GetString());
    }

    [Fact]
    public async Task FolderDeletion_IsRejectedWhileTheFolderIsNotEmpty()
    {
        var notEmpty = await _world.Administrator.SendAsync(HttpMethod.Delete, "/api/folders?path=/production");
        var empty = await _world.Administrator.SendAsync(HttpMethod.Delete, "/api/folders?path=/production/aws");
        var nowEmpty = await _world.Administrator.SendAsync(HttpMethod.Delete, "/api/folders?path=/production");

        Assert.Equal(HttpStatusCode.Conflict, notEmpty.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, empty.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, nowEmpty.StatusCode);
    }

    [Fact]
    public async Task CreateSecret_WithCreatePermission_Succeeds_AndAuditHasNoPlaintext()
    {
        await GrantGroupAsync("/production", "read", "create");

        var response = await _world.Member.SendAsync(HttpMethod.Post, "/api/secrets", new { name = "stripe", type = "api-key", folderPath = "/production", value = Plaintext });
        var entries = await VaultHttpSteps.AuditAsync(_world.Administrator);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(entries, entry => entry.Action == AuditActions.SecretCreated && entry.Outcome == "succeeded");
        Assert.DoesNotContain(Plaintext, System.Text.Json.JsonSerializer.Serialize(entries));
    }

    [Fact]
    public async Task CreateSecret_WithReadButNoCreate_IsForbidden_AndDenialIsAudited()
    {
        await GrantGroupAsync("/production", "read");

        var response = await _world.Member.SendAsync(HttpMethod.Post, "/api/secrets", UsernamePasswordBody("db", "/production"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains(await AuditAsync(AuditActions.SecretCreated, "denied"), _ => true);
    }

    [Fact]
    public async Task CreateSecret_WithoutAnyPermission_IsNotFound_AndDenialIsAudited()
    {
        var response = await _world.Outsider.SendAsync(HttpMethod.Post, "/api/secrets", UsernamePasswordBody("db", "/production"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains(await AuditAsync(AuditActions.SecretCreated, "denied"), _ => true);
    }

    [Fact]
    public async Task CreateSecret_InMissingFolder_IsNotFound_AndNothingIsStored()
    {
        var response = await _world.Administrator.SendAsync(HttpMethod.Post, "/api/secrets", UsernamePasswordBody("db", "/missing"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        await using var db = _world.Host.CreateTenantContext(_world.TenantA);
        Assert.False(await db.Secrets.AnyAsync());
    }

    [Fact]
    public async Task ListSecrets_ReturnsMetadataOnly()
    {
        await CreateApiKeyAsync(_world.Administrator, "/production", "stripe", Plaintext);
        await GrantGroupAsync("/production", "read");

        var response = await _world.Member.SendAsync(HttpMethod.Get, "/api/secrets?folderPath=/production");
        var json = await response.Content.ReadAsStringAsync();
        var items = JsonDocument.Parse(json).RootElement.EnumerateArray().ToList();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(items);
        Assert.Equal("stripe", items[0].GetProperty("name").GetString());
        Assert.Equal("api-key", items[0].GetProperty("type").GetString());
        Assert.Equal("/production", items[0].GetProperty("folderPath").GetString());
        Assert.DoesNotContain(Plaintext, json);
    }

    [Fact]
    public async Task ReadValue_WithReadPermission_ReturnsValue_AndIsAudited()
    {
        var created = await CreateApiKeyAsync(_world.Administrator, "/production", "stripe", Plaintext);
        await GrantGroupAsync("/production", "read");

        var value = await _world.Member.GetAsync<SecretValueResponse>($"/api/secrets/{created.Id}/value");
        var entries = await AuditAsync(AuditActions.SecretValueRead, "succeeded");

        Assert.Equal(Plaintext, value.Value);
        Assert.Contains(entries, entry => entry.TargetId == created.Id.ToString("D") && entry.ActorUserId == _world.MemberA.Id);
    }

    [Fact]
    public async Task ReadValue_WithWriteButNoRead_IsForbidden_AndDenialIsAudited()
    {
        var created = await CreateApiKeyAsync(_world.Administrator, "/production", "stripe", Plaintext);
        await GrantGroupAsync("/production", "write");

        var response = await _world.Member.SendAsync(HttpMethod.Get, $"/api/secrets/{created.Id}/value");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains(await AuditAsync(AuditActions.SecretValueRead, "denied"), entry => entry.TargetId == created.Id.ToString("D"));
    }

    [Fact]
    public async Task ReadValue_WithoutAnyPermission_IsNotFound_AndDenialIsAudited()
    {
        var created = await CreateApiKeyAsync(_world.Administrator, "/production", "stripe", Plaintext);

        var response = await _world.Outsider.SendAsync(HttpMethod.Get, $"/api/secrets/{created.Id}/value");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains(await AuditAsync(AuditActions.SecretValueRead, "denied"), entry => entry.TargetId == created.Id.ToString("D"));
    }

    [Fact]
    public async Task TenantAdministrator_WithoutGrant_CannotReadSecretValue()
    {
        var created = await CreateApiKeyAsync(_world.Administrator, "/production", "stripe", Plaintext);
        var administratorWithoutGrant = _world.As(_world.OutsiderA, _world.TenantA, VaultWorld.AdministratorRole);

        var response = await administratorWithoutGrant.SendAsync(HttpMethod.Get, $"/api/secrets/{created.Id}/value");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains(await AuditAsync(AuditActions.SecretValueRead, "denied"), entry => entry.TargetId == created.Id.ToString("D"));
    }

    [Fact]
    public async Task UpdateSecret_WithWritePermission_Succeeds()
    {
        var created = await CreateApiKeyAsync(_world.Administrator, "/production", "stripe", "old-value");
        await GrantGroupAsync("/production", "write");

        var response = await _world.Member.SendAsync(HttpMethod.Put, $"/api/secrets/{created.Id}", new
        {
            name = "stripe",
            type = "api-key",
            value = "new-value",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("new-value", (await _world.Administrator.GetAsync<SecretValueResponse>($"/api/secrets/{created.Id}/value")).Value);
        Assert.Contains(await VaultHttpSteps.AuditAsync(_world.Administrator), entry =>
            entry.Action == AuditActions.SecretUpdated && entry.Outcome == "succeeded" && entry.TargetId == created.Id.ToString("D"));
    }

    [Fact]
    public async Task UpdateSecret_WithoutWritePermission_IsForbidden_AndValueIsUnchanged()
    {
        var created = await CreateApiKeyAsync(_world.Administrator, "/production", "stripe", "old-value");
        await GrantGroupAsync("/production", "read");

        var response = await _world.Member.SendAsync(HttpMethod.Put, $"/api/secrets/{created.Id}", new
        {
            name = "stripe",
            type = "api-key",
            value = "new-value",
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("old-value", (await _world.Administrator.GetAsync<SecretValueResponse>($"/api/secrets/{created.Id}/value")).Value);
    }

    [Fact]
    public async Task DeleteSecret_RequiresDeletePermission()
    {
        var created = await CreateApiKeyAsync(_world.Administrator, "/production", "stripe", "value");
        await GrantGroupAsync("/production", "read", "write");

        var denied = await _world.Member.SendAsync(HttpMethod.Delete, $"/api/secrets/{created.Id}");
        await GrantGroupAsync("/production", "delete");
        var allowed = await _world.Member.SendAsync(HttpMethod.Delete, $"/api/secrets/{created.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, allowed.StatusCode);
        Assert.Contains(await VaultHttpSteps.AuditAsync(_world.Administrator), entry =>
            entry.Action == AuditActions.SecretDeleted && entry.Outcome == "succeeded" && entry.TargetId == created.Id.ToString("D"));
    }

    [Fact]
    public async Task MoveSecret_WithDeleteOnSourceAndCreateOnDestination_Succeeds()
    {
        var created = await CreateApiKeyAsync(_world.Administrator, "/production", "stripe", "value");
        await GrantGroupAsync("/production", "read", "delete");
        await GrantGroupAsync("/production/aws", "read", "create");

        var response = await _world.Member.SendAsync(HttpMethod.Post, $"/api/secrets/{created.Id}/move", new { folderPath = "/production/aws" });
        var moved = await response.Content.ReadFromJsonAsync<SecretMetadataResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("/production/aws", moved!.FolderPath);
    }

    [Fact]
    public async Task MoveSecret_WithoutCreateOnDestination_IsForbidden_AndSecretStaysPut()
    {
        var created = await CreateApiKeyAsync(_world.Administrator, "/production", "stripe", "value");
        await GrantGroupAsync("/production", "read", "delete");
        await GrantGroupAsync("/production/aws", "read");

        var response = await _world.Member.SendAsync(HttpMethod.Post, $"/api/secrets/{created.Id}/move", new { folderPath = "/production/aws" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        await using var db = _world.Host.CreateTenantContext(_world.TenantA);
        var folderPath = await db.Secrets.Where(secret => secret.Id == created.Id)
            .Join(db.Folders, secret => secret.FolderId, folder => folder.Id, (_, folder) => folder.Path)
            .SingleAsync();
        Assert.Equal("/production", folderPath);
    }

    [Fact]
    public async Task LogsAndAuditDetails_NeverContainPlaintextSecretValues()
    {
        var logs = new CapturingLoggerProvider();
        _world.Host.Services.GetRequiredService<ILoggerFactory>().AddProvider(logs);

        var created = await CreateApiKeyAsync(_world.Administrator, "/production", "stripe", Plaintext);
        await GrantGroupAsync("/production", "read", "write", "delete");
        await _world.Administrator.GetAsync<SecretValueResponse>($"/api/secrets/{created.Id}/value");
        await _world.Member.SendAsync(HttpMethod.Put, $"/api/secrets/{created.Id}", new { name = "stripe", type = "api-key", value = "rotated-" + Plaintext });
        await GrantGroupAsync("/production/aws", "create");
        await _world.Member.SendAsync(HttpMethod.Post, $"/api/secrets/{created.Id}/move", new { folderPath = "/production/aws" });

        Assert.DoesNotContain(logs.Messages, message => message.Contains(Plaintext));
    }

    private static object UsernamePasswordBody(string name, string folderPath) => new
    {
        name,
        type = "username-password",
        folderPath,
        username = "svc",
        password = "Correct-Horse-42!",
    };

    private async Task<SecretMetadataResponse> CreateApiKeyAsync(ApiClient api, string folderPath, string name, string value)
    {
        var response = await api.SendAsync(HttpMethod.Post, "/api/secrets", new { name, type = "api-key", folderPath, value });
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<SecretMetadataResponse>())!;
    }

    private async Task GrantGroupAsync(string folderPath, params string[] permissions)
    {
        await VaultHttpSteps.SetGrantAsync(_world.Administrator, folderPath, "group", _group, permissions);
    }

    private async Task<IReadOnlyList<Custode.Server.Features.SecurityAudit.AuditEntryResponse>> AuditAsync(string action, string outcome)
    {
        var entries = await VaultHttpSteps.AuditAsync(_world.Administrator, $"?action={Uri.EscapeDataString(action)}&outcome={outcome}");

        return entries;
    }
}
