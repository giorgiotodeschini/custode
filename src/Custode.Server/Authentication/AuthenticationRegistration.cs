using Custode.Infrastructure.Authentication;
using Custode.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Custode.Server.Authentication;

public static class AuthenticationRegistration
{
    public static IServiceCollection AddCustodeAuthentication(this IServiceCollection services)
    {
        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();

        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((options, jwtOptions) => ConfigureJwtBearerOptions.Apply(options, jwtOptions.Value));

        services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();

            options.AddPolicy(TenantPolicies.Operator, policy => policy.RequireRole("Operator"));
        });

        return services;
    }
}
