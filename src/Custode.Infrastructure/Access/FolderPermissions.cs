// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

namespace Custode.Infrastructure.Access;

[Flags]
public enum FolderPermissions
{
    None = 0,
    Read = 1,
    Write = 2,
    Create = 4,
    Delete = 8,
}
