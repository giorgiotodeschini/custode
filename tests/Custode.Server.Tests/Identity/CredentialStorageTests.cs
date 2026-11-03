using Custode.Infrastructure.Identity;
using Custode.Infrastructure.Persistence;
using Custode.Infrastructure.Tenancy;
using Custode.Server.Tests.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Custode.Server.Tests.Identity;

public sealed class CredentialStorageTests : IDisposable
{
    private const string Password = "Correct-Horse-42!";

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly ServiceProvider _services;

    public CredentialStorageTests()
    {
        _connection.Open();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITenantContext>(new TestTenantContext());
        services.AddDbContext<AppDbContext>(options => options.UseSqlite(_connection));
        services.AddCustodeIdentity();

        _services = services.BuildServiceProvider();

        using var scope = _services.CreateScope();
        scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();
    }

    public void Dispose()
    {
        _services.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task CreatedUser_StoresPasswordOnlyAsHash()
    {
        using var scope = _services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var tenantId = Guid.NewGuid();

        var result = await users.CreateAsync(new ApplicationUser { UserName = "alice", TenantId = tenantId }, Password);
        Assert.True(result.Succeeded);

        var stored = await users.FindByNameAsync("alice");
        Assert.NotNull(stored);
        Assert.NotNull(stored.PasswordHash);
        Assert.NotEqual(Password, stored.PasswordHash);
        Assert.DoesNotContain(Password, stored.PasswordHash);
        Assert.Equal(tenantId, stored.TenantId);
    }

    [Fact]
    public async Task CreatedUser_PasswordVerifiesAgainstHash()
    {
        using var scope = _services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        await users.CreateAsync(new ApplicationUser { UserName = "bob", TenantId = Guid.NewGuid() }, Password);
        var user = await users.FindByNameAsync("bob");

        Assert.True(await users.CheckPasswordAsync(user!, Password));
        Assert.False(await users.CheckPasswordAsync(user!, "wrong-password"));
    }
}
