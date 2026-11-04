// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Custode.Infrastructure.Tenancy;

namespace Custode.Infrastructure.Vault;

public sealed class Group : ITenantOwned
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public string Name { get; set; } = string.Empty;
}

public sealed class GroupMembership : ITenantOwned
{
    public Guid TenantId { get; set; }

    public Guid GroupId { get; set; }

    public Guid UserId { get; set; }
}
