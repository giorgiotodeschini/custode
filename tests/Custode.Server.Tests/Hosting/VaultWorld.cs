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

    private readonly Dictionary<Guid, Guid> _sessionIds = new();

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

        foreach (var user in new[] { administratorA, memberA, outsiderA, userB, operatorUser })
        {
            world._sessionIds[user.Id] = await SessionTokens.StartSessionAsync(host.Services, user);
        }

        await VaultHttpSteps.InitializeVaultAsync(world.Operator, tenantA, administratorA.Id);
        await VaultHttpSteps.InitializeVaultAsync(world.Operator, tenantB, userB.Id);

        return world;
    }

    // Users created inside a test are signed in through this helper before they are used with As.
    public async Task RegisterSessionAsync(ApplicationUser user)
    {
        _sessionIds[user.Id] = await SessionTokens.StartSessionAsync(Host.Services, user);
    }

    public ApiClient As(ApplicationUser user, Guid tenantId, params string[] roles)
    {
        var sessionId = _sessionIds.GetValueOrDefault(user.Id);
        if (sessionId == Guid.Empty)
        {
            throw new InvalidOperationException($"User '{user.UserName}' has no session. Create it through the world before using it.");
        }

        return new ApiClient(Host.Client, TestTokens.ForUser(user.Id, tenantId, sessionId, roles));
    }

    public async ValueTask DisposeAsync()
    {
        await Host.DisposeAsync();
    }
}
