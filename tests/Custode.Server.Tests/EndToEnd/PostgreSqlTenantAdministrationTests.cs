// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using System.Net;
using Custode.Server.Tests.Hosting;
using Custode.Server.Tests.SecretProtection;
using Custode.Server.Tests.Sessions;
using Custode.Server.Tests.TenantAdministration;
using Microsoft.EntityFrameworkCore;

namespace Custode.Server.Tests.EndToEnd;

public sealed class PostgreSqlTenantAdministrationTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>
{
    [Fact]
    public async Task ConcurrentWithdrawalsOfTheLastTwoAdministrators_LeaveOneAdministrator_OnPostgreSql()
    {
        var connectionString = await fixture.CreateCleanDatabaseAsync();
        await using var host = await CustodeTestHost.StartPostgreSqlAsync(connectionString, TestKeyEncryptionCertificate.Shared);

        var tenantId = Guid.NewGuid();
        var (first, firstToken) = await TenantAdministrationTestSupport.SignedInAsync(host, tenantId, TenantAdministrationTestSupport.AdministratorRole);
        var (second, secondToken) = await TenantAdministrationTestSupport.SignedInAsync(host, tenantId, TenantAdministrationTestSupport.AdministratorRole);

        var responses = await Task.WhenAll(
            TenantAdministrationTestSupport.SendAsync(host, HttpMethod.Put, $"/api/tenant/members/{second.Id}/administrator", firstToken, new { isAdministrator = false }),
            TenantAdministrationTestSupport.SendAsync(host, HttpMethod.Put, $"/api/tenant/members/{first.Id}/administrator", secondToken, new { isAdministrator = false }));

        var statuses = responses.Select(response => response.StatusCode).OrderBy(status => status).ToArray();
        Assert.Equal([HttpStatusCode.NoContent, HttpStatusCode.Conflict], statuses);

        await using var db = host.CreateTenantContext(tenantId);
        var roleId = await db.Roles.Where(role => role.Name == TenantAdministrationTestSupport.AdministratorRole).Select(role => role.Id).SingleAsync();
        var remaining = await db.UserRoles.CountAsync(link => link.RoleId == roleId);
        Assert.Equal(1, remaining);
    }
}
