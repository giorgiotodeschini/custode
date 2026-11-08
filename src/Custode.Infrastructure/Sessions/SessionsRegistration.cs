// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Custode.Infrastructure.Sessions;

public static class SessionsRegistration
{
    public static IServiceCollection AddCustodeSessions(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddOptions<SessionOptions>()
            .Bind(configuration.GetSection(SessionOptions.SectionName))
            .Validate(options => options.ReuseGraceSeconds is >= 0 and <= 60, "Sessions:ReuseGraceSeconds must be between 0 and 60.")
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<SessionCredentialResolver>();
        services.AddScoped<SessionService>();
        services.AddScoped<ISessionRevoker>(provider => provider.GetRequiredService<SessionService>());

        return services;
    }
}
