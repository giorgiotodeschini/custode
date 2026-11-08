// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Custode.Infrastructure.Tenancy;

namespace Custode.Infrastructure.Administration;

public sealed class TenantPolicy : ITenantOwned
{
    public Guid TenantId { get; set; }

    public int IdleTimeoutMinutes { get; set; } = TenantPolicyRules.IdleTimeoutMinutes.Default;

    public int AccessTokenLifetimeMinutes { get; set; } = TenantPolicyRules.AccessTokenLifetimeMinutes.Default;

    public int RefreshTokenLifetimeDays { get; set; } = TenantPolicyRules.RefreshTokenLifetimeDays.Default;

    public int ClipboardClearSeconds { get; set; } = TenantPolicyRules.ClipboardClearSeconds.Default;

    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public static class TenantPolicyRules
{
    public static readonly IntRange IdleTimeoutMinutes = new(5, 120, 15);

    public static readonly IntRange AccessTokenLifetimeMinutes = new(5, 60, 15);

    public static readonly IntRange RefreshTokenLifetimeDays = new(1, 30, 7);

    public static readonly IntRange ClipboardClearSeconds = new(10, 120, 30);
}

public sealed record IntRange(int Minimum, int Maximum, int Default)
{
    public bool Contains(int value) => value >= Minimum && value <= Maximum;
}
