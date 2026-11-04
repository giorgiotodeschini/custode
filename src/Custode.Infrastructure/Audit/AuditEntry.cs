// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Custode.Infrastructure.Tenancy;

namespace Custode.Infrastructure.Audit;

public sealed class AuditEntry : ITenantOwned
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public long Sequence { get; set; }

    public DateTimeOffset OccurredAtUtc { get; set; }

    public Guid ActorUserId { get; set; }

    public string Action { get; set; } = string.Empty;

    public string Outcome { get; set; } = string.Empty;

    public string? TargetType { get; set; }

    public string? TargetId { get; set; }

    public string? SecondaryTargetType { get; set; }

    public string? SecondaryTargetId { get; set; }

    public string PreviousHash { get; set; } = string.Empty;

    public string Hash { get; set; } = string.Empty;
}

public sealed class AuditChainHead : ITenantOwned
{
    public Guid TenantId { get; set; }

    public long LastSequence { get; set; }

    public string LastHash { get; set; } = string.Empty;

    public Guid ConcurrencyStamp { get; set; }
}
