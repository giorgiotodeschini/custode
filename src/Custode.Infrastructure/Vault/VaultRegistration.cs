// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Custode.Infrastructure.Access;
using Custode.Infrastructure.Audit;
using Custode.Infrastructure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Custode.Infrastructure.Vault;

public static class VaultRegistration
{
    public static IServiceCollection AddCustodeVault(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<SecretStorageOptions>(configuration.GetSection(SecretStorageOptions.SectionName));
        services.AddScoped<IFolderAccess, FolderAccess>();
        services.AddScoped<IAuditTrail, AuditTrail>();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();

        return services;
    }
}
