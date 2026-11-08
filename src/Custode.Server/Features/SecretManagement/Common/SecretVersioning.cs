// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Custode.Infrastructure.Vault;

namespace Custode.Server.Features.SecretManagement.Common;

internal static class SecretVersioning
{
    public static SecretVersion Snapshot(Secret secret, SecretVersionChangeKind kind, int? sourceVersion, Guid userId, DateTimeOffset now)
    {
        return new SecretVersion
        {
            Id = Guid.NewGuid(),
            TenantId = secret.TenantId,
            SecretId = secret.Id,
            Number = secret.CurrentVersion,
            Name = secret.Name,
            FileName = secret.FileName,
            ProtectedPayload = secret.ProtectedPayload,
            ChangeKind = kind,
            SourceVersionNumber = sourceVersion,
            CreatedAtUtc = now,
            CreatedByUserId = userId,
        };
    }

    public static string ChangeKindName(SecretVersionChangeKind kind) => kind switch
    {
        SecretVersionChangeKind.Created => "created",
        SecretVersionChangeKind.Updated => "updated",
        SecretVersionChangeKind.RolledBack => "rolled back",
        _ => throw new InvalidOperationException("Unknown change kind."),
    };
}
