using Microsoft.Extensions.DependencyInjection;

namespace Custode.Infrastructure.Tenancy;

public static class TenancyRegistration
{
    public static IServiceCollection AddCustodeTenancy(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ITenantContext, HttpTenantContext>();

        return services;
    }
}
