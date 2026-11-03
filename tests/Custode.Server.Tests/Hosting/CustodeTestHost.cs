using Custode.Infrastructure.Persistence;
using Custode.Infrastructure.SecretProtection;
using Custode.Infrastructure.Tenancy;
using Custode.Server;
using Custode.Server.Tests.Identity;
using Custode.Server.Tests.SecretProtection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Custode.Server.Tests.Hosting;

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

    public static Task<CustodeTestHost> StartAsync()
    {
        return StartAsync(sqliteFilePath: null, keyEncryptionCertificatePath: TestKeyEncryptionCertificate.Shared);
    }

    public static async Task<CustodeTestHost> StartAsync(string? sqliteFilePath, string keyEncryptionCertificatePath)
    {
        var dataSource = sqliteFilePath is null ? "DataSource=:memory:" : $"DataSource={sqliteFilePath}";
        var connection = new SqliteConnection(dataSource);
        connection.Open();

        return await StartWithDatabaseAsync(
            options => options.UseSqlite(connection),
            applyMigrations: false,
            keyEncryptionCertificatePath,
            connection);
    }

    public static Task<CustodeTestHost> StartPostgreSqlAsync(string connectionString, string keyEncryptionCertificatePath)
    {
        return StartWithDatabaseAsync(
            options => options.UseNpgsql(connectionString),
            applyMigrations: true,
            keyEncryptionCertificatePath);
    }

    private static async Task<CustodeTestHost> StartWithDatabaseAsync(
        Action<DbContextOptionsBuilder> useDatabase,
        bool applyMigrations,
        string keyEncryptionCertificatePath,
        IAsyncDisposable? databaseResource = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
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
            }
        }

        await app.StartAsync();

        return new CustodeTestHost(app, databaseResource);
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
