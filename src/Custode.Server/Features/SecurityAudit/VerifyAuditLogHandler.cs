// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Custode.Infrastructure.Audit;

namespace Custode.Server.Features.SecurityAudit;

internal sealed class VerifyAuditLogHandler(IAuditTrail auditTrail)
{
    public async Task<AuditVerificationResponse> HandleAsync(CancellationToken cancellationToken)
    {
        var verification = await auditTrail.VerifyAsync(cancellationToken);

        return new AuditVerificationResponse(verification.IsIntact, verification.EntriesVerified, verification.FirstAlteredSequence);
    }
}
