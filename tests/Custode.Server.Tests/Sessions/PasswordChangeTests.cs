// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Custode.Infrastructure.Audit;
using Custode.Infrastructure.Identity;
using Custode.Infrastructure.Persistence;
using Custode.Server.Tests.Hosting;
using Custode.Server.Tests.Identity;
using Microsoft.EntityFrameworkCore;

namespace Custode.Server.Tests.Sessions;

public sealed class PasswordChangeTests : IAsyncLifetime
{
    private const string NewPassword = "Brand-New-Passphrase-7";

    private CustodeTestHost _host = null!;

    public async Task InitializeAsync()
    {
        _host = await CustodeTestHost.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _host.DisposeAsync();
    }

    [Fact]
    public async Task PasswordChange_EndsTheOtherSessions_AndKeepsTheCurrentOne()
    {
        var tenantId = Guid.NewGuid();
        var user = await SessionTestSupport.CreateUserAsync(_host, tenantId);
        var (_, currentToken, _) = await SessionTestSupport.SignInTokensAsync(_host, user.UserName!);
        var (_, otherToken, _) = await SessionTestSupport.SignInTokensAsync(_host, user.UserName!);

        var response = await ChangePasswordAsync(currentToken!, IdentityTestServices.Password, NewPassword);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SessionTestSupport.GetProtectedAsync(_host, otherToken!)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SessionTestSupport.GetProtectedAsync(_host, currentToken!)).StatusCode);
        Assert.True(await SessionTestSupport.AuditContainsAsync(_host, tenantId, AuditActions.PasswordChanged));
        Assert.True(await SessionTestSupport.AuditContainsAsync(_host, tenantId, AuditActions.SessionRevoked));
    }

    [Fact]
    public async Task PasswordChange_WithAWrongCurrentPassword_IsRejected_AndNothingChanges()
    {
        var user = await SessionTestSupport.CreateUserAsync(_host, Guid.NewGuid());
        var (_, token, _) = await SessionTestSupport.SignInTokensAsync(_host, user.UserName!);

        var response = await ChangePasswordAsync(token!, "Wrong-Password-42!", NewPassword);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SessionTestSupport.SignInAsync(_host, user.UserName!)).StatusCode);
    }

    [Fact]
    public async Task PasswordChange_BelowThePolicy_IsRejectedWithTheFailures()
    {
        var user = await SessionTestSupport.CreateUserAsync(_host, Guid.NewGuid());
        var (_, token, _) = await SessionTestSupport.SignInTokensAsync(_host, user.UserName!);

        var response = await ChangePasswordAsync(token!, IdentityTestServices.Password, "short");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("errors", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task FlaggedAccount_IsRestrictedToPasswordChangeAndSignOut_UntilThePasswordChanges()
    {
        var tenantId = Guid.NewGuid();
        var user = await SessionTestSupport.CreateUserAsync(_host, tenantId);
        await FlagForPasswordChangeAsync(tenantId, user.Id);
        var (status, token, cookie) = await SessionTestSupport.SignInTokensAsync(_host, user.UserName!);

        Assert.Equal(HttpStatusCode.OK, status);

        var blocked = await SessionTestSupport.GetProtectedAsync(_host, token!);
        Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);
        Assert.Contains("password change required", await blocked.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.NoContent, (await ChangePasswordAsync(token!, IdentityTestServices.Password, NewPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SessionTestSupport.GetProtectedAsync(_host, token!)).StatusCode);
    }

    [Fact]
    public async Task FlaggedAccount_CanStillSignOut()
    {
        var tenantId = Guid.NewGuid();
        var user = await SessionTestSupport.CreateUserAsync(_host, tenantId);
        await FlagForPasswordChangeAsync(tenantId, user.Id);
        var (_, token, cookie) = await SessionTestSupport.SignInTokensAsync(_host, user.UserName!);

        var response = await SessionTestSupport.SignOutAsync(_host, token, cookie);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private async Task<HttpResponseMessage> ChangePasswordAsync(string accessToken, string current, string replacement)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/change-password")
        {
            Content = JsonContent.Create(new { currentPassword = current, newPassword = replacement }),
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", accessToken) },
        };

        return await _host.Client.SendAsync(request);
    }

    private async Task FlagForPasswordChangeAsync(Guid tenantId, Guid userId)
    {
        await using var db = _host.CreateTenantContext(tenantId);
        var user = await db.Users.SingleAsync(entity => entity.Id == userId);
        user.MustChangePassword = true;
        await db.SaveChangesAsync();
    }
}
