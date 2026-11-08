// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Custode.Infrastructure.Tenancy;

namespace Custode.Infrastructure.Sessions;

public sealed class RefreshToken : ITenantOwned
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid SessionId { get; set; }

    public string TokenHash { get; set; } = string.Empty;

    public DateTimeOffset IssuedAtUtc { get; set; }

    public DateTimeOffset? ReplacedAtUtc { get; set; }

    public Guid ConcurrencyStamp { get; set; }
}
