// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Custode.Infrastructure.Persistence;
using Custode.Infrastructure.Sessions;
using Custode.Server.Tests.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Custode.Server.Tests.Sessions;

public sealed class SessionCredentialResolverTests : IAsyncLifetime
{
    private CustodeTestHost _host = null!;

    public async Task InitializeAsync()
    {
        _host = await CustodeTestHost.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _host.DisposeAsync();
    }

    [Fact]
    public void CredentialQuery_SelectsOnlyTheIdentityAndSessionColumns()
    {
        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var sql = SessionCredentialResolver.CredentialQuery(db, RefreshTokens.Hash("probe")).ToQueryString();
        var selectList = sql[..sql.IndexOf("FROM", StringComparison.Ordinal)];

        Assert.Contains("\"TenantId\"", selectList);
        Assert.Contains("\"SessionId\"", selectList);
        Assert.Contains("\"ReplacedAtUtc\"", selectList);
        Assert.DoesNotContain("TokenHash", selectList);
        Assert.DoesNotContain("IssuedAtUtc", selectList);
        Assert.DoesNotContain("ConcurrencyStamp", selectList);
    }

    [Fact]
    public async Task Resolve_ReturnsTheSessionTenant_ForAKnownToken_AndNothingForAnUnknownOne()
    {
        var tenantId = Guid.NewGuid();
        var user = await SessionTestSupport.CreateUserAsync(_host, tenantId);
        var (_, _, cookie) = await SessionTestSupport.SignInTokensAsync(_host, user.UserName!);
        using var scope = _host.Services.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<SessionCredentialResolver>();

        Assert.Equal(tenantId, (await resolver.ResolveAsync(cookie!, CancellationToken.None))!.TenantId);
        Assert.Null(await resolver.ResolveAsync("never-issued", CancellationToken.None));
    }
}
