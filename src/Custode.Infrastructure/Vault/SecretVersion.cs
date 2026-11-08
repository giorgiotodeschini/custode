// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Custode.Infrastructure.Tenancy;

namespace Custode.Infrastructure.Vault;

public sealed class SecretVersion : ITenantOwned
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid SecretId { get; set; }

    public int Number { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? FileName { get; set; }

    public string ProtectedPayload { get; set; } = string.Empty;

    public SecretVersionChangeKind ChangeKind { get; set; }

    public int? SourceVersionNumber { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public Guid CreatedByUserId { get; set; }
}

public enum SecretVersionChangeKind
{
    Created,
    Updated,
    RolledBack,
}
