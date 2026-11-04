// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;

namespace Custode.Server.Tests.Hosting;

public sealed class ApiClient(HttpClient httpClient, string token)
{
    public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await httpClient.SendAsync(request);
    }

    public async Task<T> GetAsync<T>(string url)
    {
        var response = await SendAsync(HttpMethod.Get, url);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
}

public static class VaultSeeder
{
    public static async Task<Guid> FolderIdAsync(CustodeTestHost host, Guid tenantId, string path)
    {
        await using var db = host.CreateTenantContext(tenantId);

        return await db.Folders.Where(folder => folder.Path == path).Select(folder => folder.Id).SingleAsync();
    }
}
