// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

namespace Custode.Server.Features.SecurityAudit;

public sealed record AuditEntryResponse(
    long Sequence,
    DateTimeOffset OccurredAtUtc,
    Guid ActorUserId,
    string Action,
    string Outcome,
    string? TargetType,
    string? TargetId,
    string? SecondaryTargetType,
    string? SecondaryTargetId);

public sealed record AuditVerificationResponse(bool IsIntact, long EntriesVerified, long? FirstAlteredSequence);
