// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Custode.Infrastructure.Audit;
using Custode.Infrastructure.Identity;
using Custode.Infrastructure.Persistence;
using Custode.Infrastructure.Sessions;
using Custode.Infrastructure.Tenancy;
using Custode.Infrastructure.Vault;
using Microsoft.AspNetCore.Identity;

namespace Custode.Server.Features.Authentication.PasswordChange;

public sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);

public sealed class ChangePasswordHandler(
    UserManager<ApplicationUser> userManager,
    ISessionRevoker sessions,
    AppDbContext dbContext,
    ITenantContext tenantContext,
    ICurrentUser currentUser,
    IAuditTrail auditTrail)
{
    // Returns the failures to report; an empty list means the password was changed.
    public async Task<IReadOnlyList<string>> HandleAsync(ChangePasswordRequest request, Guid? currentSessionId, CancellationToken cancellationToken)
    {
        var userId = currentUser.Id;
        var tenantId = tenantContext.RequireTenantId();

        if (string.IsNullOrEmpty(request.CurrentPassword) || string.IsNullOrEmpty(request.NewPassword))
        {
            return ["Both the current and the new password are required."];
        }

        var user = await userManager.FindByIdAsync(userId.ToString())
            ?? throw new UnauthorizedAccessException();

        if (!await userManager.CheckPasswordAsync(user, request.CurrentPassword))
        {
            return ["The current password is incorrect."];
        }

        var result = await userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);

        if (!result.Succeeded)
        {
            return result.Errors.Select(error => error.Description).ToList();
        }

        user.MustChangePassword = false;
        await userManager.UpdateAsync(user);

        await sessions.RevokeUserSessionsAsync(tenantId, userId, SessionService.PasswordChangedReason, userId, currentSessionId, cancellationToken);

        await AuditedWork.CommitAsync(dbContext, async () =>
        {
            await auditTrail.RecordAsync(
                new AuditRecord(userId, AuditActions.PasswordChanged, "user", userId.ToString("D")),
                cancellationToken);
            return true;
        }, cancellationToken);

        return [];
    }
}
