// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

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
