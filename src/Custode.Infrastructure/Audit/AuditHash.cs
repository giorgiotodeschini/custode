// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Custode.Infrastructure.Audit;

public static class AuditHash
{
    public static string Compute(AuditEntry entry, string previousHash)
    {
        return Compute(
            entry.TenantId,
            entry.Sequence,
            entry.OccurredAtUtc,
            entry.ActorUserId,
            entry.Action,
            entry.Outcome,
            entry.TargetType,
            entry.TargetId,
            entry.SecondaryTargetType,
            entry.SecondaryTargetId,
            previousHash);
    }

    public static string Compute(
        Guid tenantId,
        long sequence,
        DateTimeOffset occurredAtUtc,
        Guid actorUserId,
        string action,
        string outcome,
        string? targetType,
        string? targetId,
        string? secondaryTargetType,
        string? secondaryTargetId,
        string previousHash)
    {
        var fields = new[]
        {
            tenantId.ToString("D"),
            sequence.ToString(CultureInfo.InvariantCulture),
            occurredAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            actorUserId.ToString("D"),
            action,
            outcome,
            targetType ?? string.Empty,
            targetId ?? string.Empty,
            secondaryTargetType ?? string.Empty,
            secondaryTargetId ?? string.Empty,
            previousHash,
        };

        var canonical = new StringBuilder();

        foreach (var field in fields)
        {
            canonical.Append(field.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(field).Append('|');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString()))).ToLowerInvariant();
    }
}
