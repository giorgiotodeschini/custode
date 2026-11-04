// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Custode.Infrastructure.Identity;
using Custode.Server.Tests.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Custode.Server.Tests.Hosting;

public sealed class VaultWorld : IAsyncDisposable
{
    public const string AdministratorRole = "TenantAdministrator";
    public const string OperatorRole = "Operator";

    private VaultWorld(
        CustodeTestHost host,
        Guid tenantA,
        Guid tenantB,
        ApplicationUser administratorA,
        ApplicationUser memberA,
        ApplicationUser outsiderA,
        ApplicationUser userB,
        ApplicationUser operatorUser)
    {
        Host = host;
        TenantA = tenantA;
        TenantB = tenantB;
        AdministratorA = administratorA;
        MemberA = memberA;
        OutsiderA = outsiderA;
        UserB = userB;
        OperatorUser = operatorUser;
    }

    public CustodeTestHost Host { get; }

    public Guid TenantA { get; }

    public Guid TenantB { get; }

    public ApplicationUser AdministratorA { get; }

    public ApplicationUser MemberA { get; }

    public ApplicationUser OutsiderA { get; }

    public ApplicationUser UserB { get; }

    public ApplicationUser OperatorUser { get; }

    public ApiClient Administrator => As(AdministratorA, TenantA, AdministratorRole);

    public ApiClient Member => As(MemberA, TenantA);

    public ApiClient Outsider => As(OutsiderA, TenantA);

    public ApiClient TenantBUser => As(UserB, TenantB);

    public ApiClient Operator => As(OperatorUser, OperatorUser.TenantId, OperatorRole);

    public static async Task<VaultWorld> CreateAsync(CustodeTestHost host)
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        using var scope = host.Services.CreateScope();
        var provider = scope.ServiceProvider;
        var administratorA = await IdentityTestServices.CreateUserAsync(provider, "admin-a", tenantA, AdministratorRole);
        var memberA = await IdentityTestServices.CreateUserAsync(provider, "member-a", tenantA);
        var outsiderA = await IdentityTestServices.CreateUserAsync(provider, "outsider-a", tenantA);
        var userB = await IdentityTestServices.CreateUserAsync(provider, "user-b", tenantB);
        var operatorUser = await IdentityTestServices.CreateUserAsync(provider, "operator", Guid.NewGuid(), OperatorRole);

        var world = new VaultWorld(host, tenantA, tenantB, administratorA, memberA, outsiderA, userB, operatorUser);

        await VaultHttpSteps.InitializeVaultAsync(world.Operator, tenantA, administratorA.Id);
        await VaultHttpSteps.InitializeVaultAsync(world.Operator, tenantB, userB.Id);

        return world;
    }

    public ApiClient As(ApplicationUser user, Guid tenantId, params string[] roles)
    {
        return new ApiClient(Host.Client, TestTokens.ForUser(user.Id, tenantId, roles));
    }

    public async ValueTask DisposeAsync()
    {
        await Host.DisposeAsync();
    }
}
