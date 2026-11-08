// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Custode.Server.Tests.Hosting;
using Custode.Server.Tests.Identity;
using Custode.Server.Tests.Sessions;
using Custode.Server.Tests.TenantAdministration;

namespace Custode.Server.Tests.TenantAdministration;

public sealed class MemberLogTests
{
    [Fact]
    public async Task InitialPassword_AndSignInPassword_NeverAppearInTheLogs()
    {
        var logs = new CapturingLoggerProvider();
        await using var host = await CustodeTestHost.StartAsync(new TestHostSettings(LoggerProvider: logs));
        var tenantId = Guid.NewGuid();
        var (_, adminToken) = await TenantAdministrationTestSupport.SignedInAsync(host, tenantId, TenantAdministrationTestSupport.AdministratorRole);
        var login = "logged-" + Guid.NewGuid().ToString("N")[..8];

        await TenantAdministrationTestSupport.SendAsync(host, HttpMethod.Post, "/api/tenant/members", adminToken,
            new { login, initialPassword = "Initial-Passphrase-99" });
        await SessionTestSupport.SignInAsync(host, login, "Initial-Passphrase-99");

        Assert.DoesNotContain(logs.Messages, message => message.Contains("Initial-Passphrase-99"));
        Assert.DoesNotContain(logs.Messages, message => message.Contains(IdentityTestServices.Password));
    }
}
