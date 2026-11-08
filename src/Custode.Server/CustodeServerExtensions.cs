// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Custode.Infrastructure.Authentication;
using Custode.Infrastructure.Identity;
using Custode.Infrastructure.SecretProtection;
using Custode.Infrastructure.Sessions;
using Custode.Infrastructure.Tenancy;
using Custode.Infrastructure.Vault;
using Custode.Server.Authentication;
using Custode.Server.Errors;
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
        services.AddCustodeSessions(configuration);
        services.AddCustodeTokenIssuance(configuration);
        services.AddCustodeAuthentication();
        services.AddCustodeSecretProtection(configuration);
        services.AddCustodeVault(configuration);
        services.AddOptions<Custode.Infrastructure.Backup.BackupOptions>().Bind(configuration.GetSection(Custode.Infrastructure.Backup.BackupOptions.SectionName));

        FeatureSlices.RegisterServices(services);

        return services;
    }

    public static WebApplication UseCustodeServer(this WebApplication app)
    {
        app.UseMiddleware<VaultExceptionMiddleware>();
        app.UseAuthentication();
        app.UseMiddleware<PasswordChangeRequirementMiddleware>();
        app.UseAuthorization();

        FeatureSlices.MapEndpoints(app);

        return app;
    }
}
