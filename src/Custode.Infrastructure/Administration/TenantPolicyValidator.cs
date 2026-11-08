// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

namespace Custode.Infrastructure.Administration;

public sealed record TenantPolicyValues(int IdleTimeoutMinutes, int AccessTokenLifetimeMinutes, int RefreshTokenLifetimeDays, int ClipboardClearSeconds);

public static class TenantPolicyValidator
{
    // Returns one message per out-of-range field, naming the field, so the caller can report all of them.
    public static IReadOnlyList<string> Validate(int? idleTimeoutMinutes, int? accessTokenLifetimeMinutes, int? refreshTokenLifetimeDays, int? clipboardClearSeconds)
    {
        var errors = new List<string>();

        Check(errors, nameof(TenantPolicyValues.IdleTimeoutMinutes), idleTimeoutMinutes, TenantPolicyRules.IdleTimeoutMinutes, "minutes");
        Check(errors, nameof(TenantPolicyValues.AccessTokenLifetimeMinutes), accessTokenLifetimeMinutes, TenantPolicyRules.AccessTokenLifetimeMinutes, "minutes");
        Check(errors, nameof(TenantPolicyValues.RefreshTokenLifetimeDays), refreshTokenLifetimeDays, TenantPolicyRules.RefreshTokenLifetimeDays, "days");
        Check(errors, nameof(TenantPolicyValues.ClipboardClearSeconds), clipboardClearSeconds, TenantPolicyRules.ClipboardClearSeconds, "seconds");

        return errors;
    }

    private static void Check(List<string> errors, string field, int? value, IntRange range, string unit)
    {
        if (value is null)
        {
            errors.Add($"{Lower(field)} is required.");
        }
        else if (!range.Contains(value.Value))
        {
            errors.Add($"{Lower(field)} must be between {range.Minimum} and {range.Maximum} {unit}.");
        }
    }

    private static string Lower(string field)
    {
        return char.ToLowerInvariant(field[0]) + field[1..];
    }
}
