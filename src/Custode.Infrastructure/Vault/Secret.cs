// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Custode.Infrastructure.Tenancy;

namespace Custode.Infrastructure.Vault;

public sealed class Secret : ITenantOwned
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid FolderId { get; set; }

    public string Name { get; set; } = string.Empty;

    public SecretType Type { get; set; }

    public string? FileName { get; set; }

    public string ProtectedPayload { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public enum SecretType
{
    UsernamePassword,
    Pat,
    ApiKey,
    File,
}
