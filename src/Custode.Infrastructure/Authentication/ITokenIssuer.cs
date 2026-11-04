// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Custode.Infrastructure.Identity;

namespace Custode.Infrastructure.Authentication;

public interface ITokenIssuer
{
    Task<IssuedToken> IssueAsync(ApplicationUser user, CancellationToken cancellationToken = default);
}

public sealed record IssuedToken(string AccessToken, DateTimeOffset ExpiresAt);
