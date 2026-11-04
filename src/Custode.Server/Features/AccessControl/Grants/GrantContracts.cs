// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

namespace Custode.Server.Features.AccessControl.Grants;

public sealed record SetGrantRequest(
    string? FolderPath,
    string? PrincipalKind,
    Guid PrincipalId,
    IReadOnlyList<string>? Permissions);
