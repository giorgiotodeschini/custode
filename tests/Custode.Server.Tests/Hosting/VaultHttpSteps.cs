// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using System.Net.Http.Json;
using Custode.Server.Features.AccessControl.Groups;
using Custode.Server.Features.SecurityAudit;
using Custode.Server.Features.SecretManagement.Folders;

namespace Custode.Server.Tests.Hosting;

public static class VaultHttpSteps
{
    public static async Task CreateFolderAsync(ApiClient api, string path)
    {
        var response = await api.SendAsync(HttpMethod.Post, "/api/folders", new { path });
        response.EnsureSuccessStatusCode();
    }

    public static async Task<Guid> CreateGroupAsync(ApiClient api, string name)
    {
        var response = await api.SendAsync(HttpMethod.Post, "/api/access/groups", new { name });
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<GroupResponse>())!.Id;
    }

    public static async Task AddMemberAsync(ApiClient api, Guid groupId, Guid userId)
    {
        var response = await api.SendAsync(HttpMethod.Post, $"/api/access/groups/{groupId}/members", new { userId });
        response.EnsureSuccessStatusCode();
    }

    public static async Task SetGrantAsync(ApiClient api, string folderPath, string principalKind, Guid principalId, params string[] permissions)
    {
        var response = await api.SendAsync(HttpMethod.Put, "/api/access/grants", new { folderPath, principalKind, principalId, permissions });
        response.EnsureSuccessStatusCode();
    }

    public static async Task InitializeVaultAsync(ApiClient operatorApi, Guid tenantId, Guid administratorUserId)
    {
        var response = await operatorApi.SendAsync(HttpMethod.Post, $"/api/operator/tenants/{tenantId}/vault", new { administratorUserId });
        response.EnsureSuccessStatusCode();
    }

    // Follows the cursor until the last page, so callers see the whole matching log.
    public static async Task<IReadOnlyList<AuditEntryResponse>> AuditAsync(ApiClient administrator, string query = "")
    {
        var entries = new List<AuditEntryResponse>();
        long? cursor = null;

        do
        {
            var separator = query.Length == 0 ? "?" : "&";
            var cursorPart = cursor is null ? string.Empty : $"&afterSequence={cursor}";
            var page = await administrator.GetAsync<AuditPageResponse>($"/api/audit{query}{separator}limit=500{cursorPart}");

            entries.AddRange(page.Entries);
            cursor = page.NextAfterSequence;
        }
        while (cursor is not null);

        return entries;
    }
}
