// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Custode.Server.Features.AccessControl;
using Custode.Server.Features.Authentication.SignIn;
using Custode.Server.Features.SecretManagement;
using Custode.Server.Features.SecurityAudit;
using Custode.Server.Features.TenantProvisioning;

namespace Custode.Server.Features;

public static class FeatureSlices
{
    private static readonly IFeatureSlice[] All =
    [
        new SignInSlice(),
        new SecretManagementSlice(),
        new AccessControlSlice(),
        new SecurityAuditSlice(),
        new TenantProvisioningSlice(),
    ];

    public static int Count => All.Length;

    public static void RegisterServices(IServiceCollection services)
    {
        foreach (var slice in All)
        {
            slice.RegisterServices(services);
        }
    }

    public static void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        foreach (var slice in All)
        {
            slice.MapEndpoints(endpoints);
        }
    }
}
