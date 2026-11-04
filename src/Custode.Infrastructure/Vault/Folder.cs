// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Custode.Infrastructure.Tenancy;

namespace Custode.Infrastructure.Vault;

public sealed class Folder : ITenantOwned
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public string Path { get; set; } = string.Empty;

    public Guid? ParentId { get; set; }

    public bool InheritsPermissions { get; set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; }
}
