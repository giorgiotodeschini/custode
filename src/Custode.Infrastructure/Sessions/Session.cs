// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Custode.Infrastructure.Tenancy;

namespace Custode.Infrastructure.Sessions;

public sealed class Session : ITenantOwned
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid UserId { get; set; }

    public DateTimeOffset StartedAtUtc { get; set; }

    public DateTimeOffset LastActivityAtUtc { get; set; }

    public DateTimeOffset? RevokedAtUtc { get; set; }

    public string? RevocationReason { get; set; }
}
