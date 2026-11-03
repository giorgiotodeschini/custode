using Custode.Infrastructure.Authentication;
using Custode.Infrastructure.Identity;
using Custode.Infrastructure.SecretProtection;
using Custode.Infrastructure.Tenancy;
using Custode.Server.Authentication;
using Custode.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Custode.Server;

public static class CustodeServerExtensions
{
    public static IServiceCollection AddCustodeServer(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddCustodeTenancy();
        services.AddCustodeIdentity();
        services.AddCustodeTokenIssuance(configuration);
        services.AddCustodeAuthentication();
        services.AddCustodeSecretProtection(configuration);

        FeatureSlices.RegisterServices(services);

        return services;
    }

    public static WebApplication UseCustodeServer(this WebApplication app)
    {
        app.UseAuthentication();
        app.UseAuthorization();

        FeatureSlices.MapEndpoints(app);

        return app;
    }
}
