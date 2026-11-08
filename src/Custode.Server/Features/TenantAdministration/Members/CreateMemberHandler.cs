// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Custode.Infrastructure.Audit;
using Custode.Infrastructure.Identity;
using Custode.Infrastructure.Persistence;
using Custode.Infrastructure.Tenancy;
using Custode.Infrastructure.Vault;
using Microsoft.AspNetCore.Identity;

namespace Custode.Server.Features.TenantAdministration.Members;

public sealed class CreateMemberHandler(
    UserManager<ApplicationUser> userManager,
    AppDbContext dbContext,
    ITenantContext tenantContext,
    ICurrentUser currentUser,
    IAuditTrail auditTrail)
{
    public async Task<MemberResponse> HandleAsync(CreateMemberRequest request, CancellationToken cancellationToken)
    {
        var tenantId = tenantContext.RequireTenantId();
        var login = request.Login?.Trim();

        if (string.IsNullOrEmpty(login))
        {
            throw new InvalidVaultRequestException("A login is required.");
        }

        if (string.IsNullOrEmpty(request.InitialPassword))
        {
            throw new InvalidVaultRequestException("An initial password is required.");
        }

        var user = new ApplicationUser
        {
            UserName = login,
            TenantId = tenantId,
            IsEnabled = true,
            MustChangePassword = true,
        };

        var result = await userManager.CreateAsync(user, request.InitialPassword);

        if (!result.Succeeded)
        {
            if (result.Errors.Any(error => error.Code == "DuplicateUserName"))
            {
                throw new VaultConflictException("A member with this login already exists.");
            }

            throw new InvalidVaultRequestException(string.Join(" ", result.Errors.Select(error => error.Description)));
        }

        var actorId = currentUser.Id;

        await AuditedWork.CommitAsync(dbContext, async () =>
        {
            await auditTrail.RecordAsync(
                new AuditRecord(actorId, AuditActions.MemberCreated, "user", user.Id.ToString("D")),
                cancellationToken);
            return true;
        }, cancellationToken);

        return new MemberResponse(user.Id, login, [], 0, true, true);
    }
}
