using System.Security.Claims;
using Custode.Infrastructure.Persistence;
using Custode.Infrastructure.Tenancy;
using Custode.Server.Tests.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Custode.Server.Tests.Tenancy;

public sealed class TenantIndependentQueriesTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly DbContextOptions _options;
    private readonly ServiceProvider _services;
    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();

    public TenantIndependentQueriesTests()
    {
        _connection.Open();
        _options = new DbContextOptionsBuilder().UseSqlite(_connection).Options;

        using (var seedA = new TenantScopedTestContext(_options, new TestTenantContext { TenantId = _tenantA }))
        {
            seedA.Database.EnsureCreated();
            seedA.Notes.AddRange(
                new Note { Id = Guid.NewGuid(), TenantId = _tenantA, Title = "A1" },
                new Note { Id = Guid.NewGuid(), TenantId = _tenantA, Title = "A2" });
            seedA.SaveChanges();
        }

        using (var seedB = new TenantScopedTestContext(_options, new TestTenantContext { TenantId = _tenantB }))
        {
            seedB.Notes.Add(new Note { Id = Guid.NewGuid(), TenantId = _tenantB, Title = "B1" });
            seedB.SaveChanges();
        }

        _services = new ServiceCollection()
            .AddAuthorization(options =>
                options.AddPolicy(TenantPolicies.Operator, policy => policy.RequireRole("Operator")))
            .AddLogging()
            .BuildServiceProvider();
    }

    public void Dispose()
    {
        _services.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task QueryAllTenants_ForNonOperator_IsForbidden()
    {
        await using var context = new TenantScopedTestContext(_options, new TestTenantContext { TenantId = _tenantA });
        var queries = CreateQueries(context);
        var principal = CreatePrincipal(roles: ["Member"]);

        await Assert.ThrowsAsync<OperatorAccessDeniedException>(
            () => queries.QueryAllTenantsAsync<Note>(principal));
    }

    [Fact]
    public async Task QueryAllTenants_ForUserWithoutRoles_IsForbidden()
    {
        await using var context = new TenantScopedTestContext(_options, new TestTenantContext { TenantId = _tenantA });
        var queries = CreateQueries(context);
        var principal = CreatePrincipal(roles: []);

        await Assert.ThrowsAsync<OperatorAccessDeniedException>(
            () => queries.QueryAllTenantsAsync<Note>(principal));
    }

    [Fact]
    public async Task QueryAllTenants_ForOperator_ReturnsRowsOfEveryTenant()
    {
        await using var context = new TenantScopedTestContext(_options, new TestTenantContext { TenantId = _tenantA });
        var queries = CreateQueries(context);
        var principal = CreatePrincipal(roles: ["Operator"]);

        var titles = await (await queries.QueryAllTenantsAsync<Note>(principal))
            .Select(note => note.Title)
            .OrderBy(title => title)
            .ToListAsync();

        Assert.Equal(["A1", "A2", "B1"], titles);
    }

    [Fact]
    public async Task RegularQuery_StillScopedToCurrentTenant_AfterOperatorPath()
    {
        await using var context = new TenantScopedTestContext(_options, new TestTenantContext { TenantId = _tenantA });
        var queries = CreateQueries(context);
        var principal = CreatePrincipal(roles: ["Operator"]);

        await (await queries.QueryAllTenantsAsync<Note>(principal)).ToListAsync();
        var regularCount = await context.Notes.CountAsync();

        Assert.Equal(2, regularCount);
    }

    private TenantIndependentQueries CreateQueries(AppDbContext context)
    {
        var authorization = _services.GetRequiredService<IAuthorizationService>();
        return new TenantIndependentQueries(context, authorization);
    }

    private static ClaimsPrincipal CreatePrincipal(string[] roles)
    {
        var claims = roles.Select(role => new Claim(ClaimTypes.Role, role));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }
}
