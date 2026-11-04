// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using System.Text;
using Custode.Infrastructure.Authentication;
using Custode.Server.Tests.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Custode.Server.Tests.Identity;

public sealed class JwtTokenIssuerTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly ServiceProvider _services;

    public JwtTokenIssuerTests()
    {
        _connection.Open();
        _services = IdentityTestServices.CreateServices(_connection, new TestTenantContext());
    }

    public void Dispose()
    {
        _services.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task IssuedToken_CarriesSubjectTenantAndRoles()
    {
        var tenantId = Guid.NewGuid();
        using var scope = _services.CreateScope();
        var user = await IdentityTestServices.CreateUserAsync(scope.ServiceProvider, "alice", tenantId, "Operator", "Auditor");
        var issuer = scope.ServiceProvider.GetRequiredService<ITokenIssuer>();

        var issued = await issuer.IssueAsync(user);
        var token = new JsonWebTokenHandler().ReadJsonWebToken(issued.AccessToken);

        Assert.Equal(user.Id.ToString(), token.GetClaim("sub")?.Value);
        Assert.Equal(tenantId.ToString(), token.GetClaim("tenant_id")?.Value);
        Assert.Equal(["Auditor", "Operator"], token.Claims
            .Where(claim => claim.Type == "role")
            .Select(claim => claim.Value)
            .OrderBy(role => role));
    }

    [Fact]
    public async Task IssuedToken_ExpiresAfterConfiguredLifetime()
    {
        using var scope = _services.CreateScope();
        var user = await IdentityTestServices.CreateUserAsync(scope.ServiceProvider, "bob", Guid.NewGuid());
        var issuer = scope.ServiceProvider.GetRequiredService<ITokenIssuer>();

        var before = DateTimeOffset.UtcNow;
        var issued = await issuer.IssueAsync(user);
        var after = DateTimeOffset.UtcNow;

        var expectedMinimum = before.AddMinutes(15).AddSeconds(-5);
        var expectedMaximum = after.AddMinutes(15).AddSeconds(5);
        Assert.InRange(issued.ExpiresAt, expectedMinimum, expectedMaximum);
    }

    [Fact]
    public async Task IssuedToken_IsSignedWithConfiguredKeyIssuerAndAudience()
    {
        using var scope = _services.CreateScope();
        var user = await IdentityTestServices.CreateUserAsync(scope.ServiceProvider, "carol", Guid.NewGuid());
        var issuer = scope.ServiceProvider.GetRequiredService<ITokenIssuer>();
        var issued = await issuer.IssueAsync(user);

        var parameters = new TokenValidationParameters
        {
            ValidIssuer = IdentityTestServices.Issuer,
            ValidAudience = IdentityTestServices.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(IdentityTestServices.SigningKey)),
        };

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(issued.AccessToken, parameters);

        Assert.True(result.IsValid);
    }
}
