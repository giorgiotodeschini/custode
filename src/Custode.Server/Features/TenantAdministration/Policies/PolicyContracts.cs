// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

namespace Custode.Server.Features.TenantAdministration.Policies;

public sealed record PolicyResponse(
    int IdleTimeoutMinutes,
    int AccessTokenLifetimeMinutes,
    int RefreshTokenLifetimeDays,
    int ClipboardClearSeconds);

public sealed record SetPolicyRequest(
    int? IdleTimeoutMinutes,
    int? AccessTokenLifetimeMinutes,
    int? RefreshTokenLifetimeDays,
    int? ClipboardClearSeconds);
