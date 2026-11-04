// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Custode.Infrastructure.Authentication;
using Custode.Infrastructure.Identity;
using Custode.Infrastructure.Persistence;
using Custode.Infrastructure.Tenancy;
using Custode.Server.Tests.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Custode.Server.Tests.Identity;

public static class IdentityTestServices
{
    public const string Issuer = "custode-tests";
    public const string Audience = "custode-tests-clients";
    public const string SigningKey = "test-signing-key-that-is-long-enough-for-hs256";
    public const string Password = "Correct-Horse-42!";

    public static Dictionary<string, string?> JwtSettings(int lifetimeMinutes = 15)
    {
        return new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = Issuer,
            ["Jwt:Audience"] = Audience,
            ["Jwt:SigningKey"] = SigningKey,
            ["Jwt:LifetimeMinutes"] = lifetimeMinutes.ToString(),
        };
    }

    public static IConfiguration CreateJwtConfiguration(int lifetimeMinutes = 15)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(JwtSettings(lifetimeMinutes))
            .Build();
    }

    public static ServiceProvider CreateServices(SqliteConnection connection, ITenantContext tenantContext)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(tenantContext);
        services.AddDbContext<AppDbContext>(options => options.UseSqlite(connection));
        services.AddCustodeIdentity();
        services.AddCustodeTokenIssuance(CreateJwtConfiguration());

        var provider = services.BuildServiceProvider();

        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();

        return provider;
    }

    public static async Task<ApplicationUser> CreateUserAsync(
        IServiceProvider services,
        string userName,
        Guid tenantId,
        params string[] roles)
    {
        var users = services.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole<Guid>>>();

        foreach (var role in roles)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole<Guid>(role));
            }
        }

        var user = new ApplicationUser { UserName = userName, TenantId = tenantId };
        var created = await users.CreateAsync(user, Password);
        if (!created.Succeeded)
        {
            throw new InvalidOperationException(string.Join("; ", created.Errors.Select(error => error.Description)));
        }

        if (roles.Length > 0)
        {
            await users.AddToRolesAsync(user, roles);
        }

        return user;
    }
}
