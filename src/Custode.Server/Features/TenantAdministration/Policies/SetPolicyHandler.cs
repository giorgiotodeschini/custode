// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Custode.Infrastructure.Administration;
using Custode.Infrastructure.Audit;
using Custode.Infrastructure.Identity;
using Custode.Infrastructure.Persistence;
using Custode.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Custode.Server.Features.TenantAdministration.Policies;

public sealed class SetPolicyHandler(
    AppDbContext dbContext,
    ITenantContext tenantContext,
    ICurrentUser currentUser,
    IAuditTrail auditTrail,
    GetPolicyHandler reader)
{
    // Returns the validation failures; an empty list means the policy was saved.
    public async Task<IReadOnlyList<string>> HandleAsync(SetPolicyRequest request, CancellationToken cancellationToken)
    {
        var errors = TenantPolicyValidator.Validate(
            request.IdleTimeoutMinutes,
            request.AccessTokenLifetimeMinutes,
            request.RefreshTokenLifetimeDays,
            request.ClipboardClearSeconds);

        if (errors.Count > 0)
        {
            return errors;
        }

        var tenantId = tenantContext.RequireTenantId();
        var actorId = currentUser.Id;

        await AuditedWork.CommitAsync(dbContext, async () =>
        {
            var policy = await dbContext.TenantPolicies.FirstOrDefaultAsync(entity => entity.TenantId == tenantId, cancellationToken);

            if (policy is null)
            {
                policy = new TenantPolicy { TenantId = tenantId };
                dbContext.TenantPolicies.Add(policy);
            }

            policy.IdleTimeoutMinutes = request.IdleTimeoutMinutes!.Value;
            policy.AccessTokenLifetimeMinutes = request.AccessTokenLifetimeMinutes!.Value;
            policy.RefreshTokenLifetimeDays = request.RefreshTokenLifetimeDays!.Value;
            policy.ClipboardClearSeconds = request.ClipboardClearSeconds!.Value;
            policy.UpdatedAtUtc = DateTimeOffset.UtcNow;

            await auditTrail.RecordAsync(
                new AuditRecord(actorId, AuditActions.TenantPolicyChanged, "tenant policy", tenantId.ToString("D")),
                cancellationToken);
            return true;
        }, cancellationToken);

        return [];
    }

    public Task<PolicyResponse> ReadAsync(CancellationToken cancellationToken)
    {
        return reader.HandleAsync(cancellationToken);
    }
}
