// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using System.Net;
using System.Net.Http.Json;
using Custode.Infrastructure.Audit;
using Custode.Server.Features.SecretManagement.Secrets;
using Custode.Server.Features.SecurityAudit;
using Custode.Server.Tests.Hosting;
using Microsoft.EntityFrameworkCore;

namespace Custode.Server.Tests.Vault;

public static class VaultScenarios
{
    public static async Task CrossTenantUserCannotReachAnotherTenantsVault(VaultWorld world)
    {
        await VaultHttpSteps.CreateFolderAsync(world.Administrator, "/production");
        var created = await CreateSecretAsync(world.Administrator, "/production", "stripe", "tenant-a-value");
        var tenantBAdministrator = world.As(world.UserB, world.TenantB, VaultWorld.AdministratorRole);

        var list = await tenantBAdministrator.SendAsync(HttpMethod.Get, "/api/secrets?folderPath=/production");
        var read = await tenantBAdministrator.SendAsync(HttpMethod.Get, $"/api/secrets/{created.Id}/value");
        var update = await tenantBAdministrator.SendAsync(HttpMethod.Put, $"/api/secrets/{created.Id}", new { name = "stripe", type = "api-key", value = "hijacked" });
        var move = await tenantBAdministrator.SendAsync(HttpMethod.Post, $"/api/secrets/{created.Id}/move", new { folderPath = "/" });
        var delete = await tenantBAdministrator.SendAsync(HttpMethod.Delete, $"/api/secrets/{created.Id}");
        var grant = await tenantBAdministrator.SendAsync(HttpMethod.Put, "/api/access/grants",
            new { folderPath = "/production", principalKind = "user", principalId = world.UserB.Id, permissions = new[] { "read" } });
        var audit = await tenantBAdministrator.SendAsync(HttpMethod.Get, "/api/audit");

        Assert.Equal(HttpStatusCode.NotFound, list.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, update.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, move.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, grant.StatusCode);

        var tenantBEntries = (await audit.Content.ReadFromJsonAsync<List<AuditEntryResponse>>())!;
        Assert.DoesNotContain(tenantBEntries, entry => entry.TargetId == created.Id.ToString("D"));

        var stillThere = await world.Administrator.GetAsync<SecretValueResponse>($"/api/secrets/{created.Id}/value");
        Assert.Equal("tenant-a-value", stillThere.Value);
    }

    public static async Task NonAdministratorAccessChanges_AreForbiddenAndDenialsAreAudited(VaultWorld world)
    {
        await VaultHttpSteps.CreateFolderAsync(world.Administrator, "/production");

        var createGroup = await world.Member.SendAsync(HttpMethod.Post, "/api/access/groups", new { name = "rogue" });
        var grant = await world.Member.SendAsync(HttpMethod.Put, "/api/access/grants",
            new { folderPath = "/production", principalKind = "user", principalId = world.MemberA.Id, permissions = new[] { "delete" } });

        Assert.Equal(HttpStatusCode.Forbidden, createGroup.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, grant.StatusCode);

        var denied = await world.Administrator.GetAsync<List<AuditEntryResponse>>("/api/audit?outcome=denied");
        Assert.Contains(denied, entry => entry.Action == AuditActions.GroupCreated && entry.ActorUserId == world.MemberA.Id);
        Assert.Contains(denied, entry => entry.Action == AuditActions.GrantSet && entry.ActorUserId == world.MemberA.Id);
    }

    public static async Task AlteredAuditEntry_IsDetectedByVerification(VaultWorld world)
    {
        await world.Administrator.SendAsync(HttpMethod.Post, "/api/access/groups", new { name = "first" });
        await world.Administrator.SendAsync(HttpMethod.Post, "/api/access/groups", new { name = "second" });
        await world.Administrator.SendAsync(HttpMethod.Post, "/api/access/groups", new { name = "third" });

        var intact = await world.Administrator.GetAsync<AuditVerificationResponse>("/api/audit/verify");
        var total = (await world.Administrator.GetAsync<List<AuditEntryResponse>>("/api/audit")).Count;
        Assert.True(intact.IsIntact);
        Assert.Equal(total, intact.EntriesVerified);

        // Bypass the immutability trigger the way a database administrator could, then tamper with entry 2.
        await using (var db = world.Host.CreateTenantContext(world.TenantA))
        {
            await db.Database.ExecuteSqlRawAsync(DropRejectUpdateTriggerSql(db.Database.ProviderName!));
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE \"AuditEntries\" SET \"Action\" = 'folder deleted' WHERE \"TenantId\" = {0} AND \"Sequence\" = 2",
                world.TenantA);
        }

        var altered = await world.Administrator.GetAsync<AuditVerificationResponse>("/api/audit/verify");

        Assert.False(altered.IsIntact);
        Assert.Equal(2L, altered.FirstAlteredSequence);
    }

    private static string DropRejectUpdateTriggerSql(string databaseProvider)
    {
        return databaseProvider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase)
            ? "DROP TRIGGER audit_entries_reject_update ON \"AuditEntries\";"
            : "DROP TRIGGER audit_entries_reject_update;";
    }

    private static async Task<SecretMetadataResponse> CreateSecretAsync(ApiClient api, string folderPath, string name, string value)
    {
        var response = await api.SendAsync(HttpMethod.Post, "/api/secrets", new { name, type = "api-key", folderPath, value });
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<SecretMetadataResponse>())!;
    }
}
