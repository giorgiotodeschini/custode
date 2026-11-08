// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Custode.Infrastructure.Audit;
using Custode.Infrastructure.Persistence;
using Custode.Infrastructure.SecretProtection;
using Custode.Infrastructure.Tenancy;
using Custode.Server;
using Custode.Server.Tests.Identity;
using Custode.Server.Tests.Persistence;
using Custode.Server.Tests.SecretProtection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Custode.Server.Tests.Hosting;

public sealed record TestHostSettings(
    TimeProvider? Clock = null,
    Action<DbContextOptionsBuilder>? ConfigureDatabase = null,
    IReadOnlyDictionary<string, string?>? Configuration = null,
    ILoggerProvider? LoggerProvider = null);

public sealed class CustodeTestHost : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly IAsyncDisposable? _databaseResource;

    private CustodeTestHost(WebApplication app, IAsyncDisposable? databaseResource)
    {
        _app = app;
        _databaseResource = databaseResource;
    }

    public HttpClient Client => _app.GetTestClient();

    public IServiceProvider Services => _app.Services;

    public static Task<CustodeTestHost> StartAsync(TimeProvider? clock = null)
    {
        return StartAsync(new TestHostSettings(Clock: clock));
    }

    public static async Task<CustodeTestHost> StartAsync(string? sqliteFilePath, string keyEncryptionCertificatePath, TimeProvider? clock = null)
    {
        var dataSource = sqliteFilePath is null ? "DataSource=:memory:" : $"DataSource={sqliteFilePath}";
        var connection = new SqliteConnection(dataSource);
        connection.Open();

        return await StartWithDatabaseAsync(
            options => options.UseSqlite(connection),
            applyMigrations: false,
            keyEncryptionCertificatePath,
            new TestHostSettings(Clock: clock),
            connection);
    }

    public static async Task<CustodeTestHost> StartAsync(TestHostSettings settings)
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        return await StartWithDatabaseAsync(
            options =>
            {
                options.UseSqlite(connection);
                settings.ConfigureDatabase?.Invoke(options);
            },
            applyMigrations: false,
            TestKeyEncryptionCertificate.Shared,
            settings,
            connection);
    }

    public static Task<CustodeTestHost> StartPostgreSqlAsync(string connectionString, string keyEncryptionCertificatePath)
    {
        return StartWithDatabaseAsync(
            options => options.UseNpgsql(connectionString),
            applyMigrations: true,
            keyEncryptionCertificatePath,
            new TestHostSettings());
    }

    private static async Task<CustodeTestHost> StartWithDatabaseAsync(
        Action<DbContextOptionsBuilder> useDatabase,
        bool applyMigrations,
        string keyEncryptionCertificatePath,
        TestHostSettings settings,
        IAsyncDisposable? databaseResource = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();

        if (settings.Clock is not null)
        {
            builder.Services.AddSingleton(settings.Clock);
        }

        if (settings.LoggerProvider is not null)
        {
            builder.Logging.AddProvider(settings.LoggerProvider);
        }

        if (settings.Configuration is not null)
        {
            builder.Configuration.AddInMemoryCollection(settings.Configuration);
        }

        builder.Configuration.AddInMemoryCollection(IdentityTestServices.JwtSettings());
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [SecretProtectionRegistration.CertificatePathKey] = keyEncryptionCertificatePath,
            [SecretProtectionRegistration.CertificatePasswordKey] = TestKeyEncryptionCertificate.Password,
        });
        builder.Services.AddDbContext<AppDbContext>(useDatabase);
        builder.Services.AddCustodeServer(builder.Configuration);

        var app = builder.Build();
        app.UseCustodeServer();

        app.MapGet("/tests/protected", () => Results.Ok("ok"));
        app.MapGet("/tests/tenant", (ITenantContext tenant) => Results.Ok(new { tenantId = tenant.TenantId }));
        app.MapPost("/tests/tenant", (ITenantContext tenant, TenantBody body) => Results.Ok(new { tenantId = tenant.TenantId }));

        using (var scope = app.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            if (applyMigrations)
            {
                await context.Database.MigrateAsync();
            }
            else
            {
                await context.Database.EnsureCreatedAsync();

                foreach (var statement in AuditImmutabilityTriggers.SqliteUp)
                {
                    await context.Database.ExecuteSqlRawAsync(statement);
                }
            }
        }

        await app.StartAsync();

        return new CustodeTestHost(app, databaseResource);
    }

    public AppDbContext CreateTenantContext(Guid tenantId)
    {
        return new AppDbContext(
            Services.GetRequiredService<DbContextOptions<AppDbContext>>(),
            new TestTenantContext { TenantId = tenantId });
    }

    public async ValueTask DisposeAsync()
    {
        await _app.DisposeAsync();

        if (_databaseResource is not null)
        {
            await _databaseResource.DisposeAsync();
        }
    }

    public sealed record TenantBody(Guid? TenantId);
}
