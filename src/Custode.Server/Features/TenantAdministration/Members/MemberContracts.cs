// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

namespace Custode.Server.Features.TenantAdministration.Members;

public sealed record MemberResponse(
    Guid Id,
    string Login,
    IReadOnlyList<string> Roles,
    int GroupCount,
    bool IsEnabled,
    bool MustChangePassword);

public sealed record CreateMemberRequest(string? Login, string? InitialPassword);

public sealed record SetAdministratorRequest(bool? IsAdministrator);
