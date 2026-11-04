// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Custode.Infrastructure.Access;
using Custode.Infrastructure.Tenancy;

namespace Custode.Infrastructure.Vault;

public sealed class FolderGrant : ITenantOwned
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid FolderId { get; set; }

    public PrincipalKind PrincipalKind { get; set; }

    public Guid PrincipalId { get; set; }

    public FolderPermissions Permissions { get; set; }
}

public enum PrincipalKind
{
    User,
    Group,
}
