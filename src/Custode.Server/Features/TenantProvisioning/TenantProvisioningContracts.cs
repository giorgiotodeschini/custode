// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

namespace Custode.Server.Features.TenantProvisioning;

public sealed record InitializeVaultRequest(Guid AdministratorUserId);

public sealed record InitializeVaultResponse(Guid RootFolderId, string Path, Guid AdministratorUserId);
