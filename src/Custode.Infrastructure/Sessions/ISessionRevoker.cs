// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

namespace Custode.Infrastructure.Sessions;

// The published contract other slices use to end sessions. Every revocation is recorded in the audit log.
public interface ISessionRevoker
{
    Task RevokeAsync(Guid tenantId, Guid sessionId, string reason, Guid actorUserId, CancellationToken cancellationToken);

    Task<int> RevokeUserSessionsAsync(
        Guid tenantId,
        Guid userId,
        string reason,
        Guid actorUserId,
        Guid? exceptSessionId,
        CancellationToken cancellationToken);
}
