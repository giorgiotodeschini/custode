// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using System.Net;
using System.Net.Http.Headers;
using Custode.Infrastructure.Audit;
using Custode.Server.Tests.Hosting;
using Custode.Server.Tests.SecretProtection;
using Custode.Server.Tests.Sessions;

namespace Custode.Server.Tests.EndToEnd;

// The session behaviors that depend on real concurrent writes, which the shared in-memory SQLite connection cannot run.
public sealed class PostgreSqlSessionTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>
{
    [Fact]
    public async Task ConcurrentRefreshes_WithTheSameToken_OnlyOneSucceeds_OnPostgreSql()
    {
        var connectionString = await fixture.CreateCleanDatabaseAsync();
        await using var host = await CustodeTestHost.StartPostgreSqlAsync(connectionString, TestKeyEncryptionCertificate.Shared);

        var tenantId = Guid.NewGuid();
        var user = await SessionTestSupport.CreateUserAsync(host, tenantId);
        var (_, _, cookie) = await SessionTestSupport.SignInTokensAsync(host, user.UserName!);

        var responses = await Task.WhenAll(
            SessionTestSupport.RefreshAsync(host, cookie),
            SessionTestSupport.RefreshAsync(host, cookie),
            SessionTestSupport.RefreshAsync(host, cookie));

        var statuses = responses.Select(response => response.StatusCode).OrderBy(status => status).ToArray();
        Assert.Equal(HttpStatusCode.OK, statuses[0]);
        Assert.All(statuses.Skip(1), status => Assert.Equal(HttpStatusCode.Unauthorized, status));

        var winner = SessionTestSupport.CookieValue(responses.First(response => response.StatusCode == HttpStatusCode.OK));
        Assert.Equal(HttpStatusCode.OK, (await SessionTestSupport.RefreshAsync(host, winner)).StatusCode);
        Assert.False(await SessionTestSupport.AuditContainsAsync(host, tenantId, AuditActions.RefreshTokenReuseDetected, "denied"));
    }

    [Fact]
    public async Task SignOut_RevokesTheSession_AndTheAccessTokenStopsWorking_OnPostgreSql()
    {
        var connectionString = await fixture.CreateCleanDatabaseAsync();
        await using var host = await CustodeTestHost.StartPostgreSqlAsync(connectionString, TestKeyEncryptionCertificate.Shared);

        var tenantId = Guid.NewGuid();
        var user = await SessionTestSupport.CreateUserAsync(host, tenantId);
        var (_, accessToken, cookie) = await SessionTestSupport.SignInTokensAsync(host, user.UserName!);

        Assert.Equal(HttpStatusCode.NoContent, (await SessionTestSupport.SignOutAsync(host, accessToken, cookie)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SessionTestSupport.GetProtectedAsync(host, accessToken!)).StatusCode);
        Assert.True(await SessionTestSupport.AuditContainsAsync(host, tenantId, AuditActions.SessionRevoked));
    }
}
