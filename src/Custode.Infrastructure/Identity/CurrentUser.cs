// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Custode.Infrastructure.Authentication;
using Microsoft.AspNetCore.Http;

namespace Custode.Infrastructure.Identity;

public interface ICurrentUser
{
    Guid Id { get; }
}

public sealed class HttpCurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    public Guid Id
    {
        get
        {
            var value = httpContextAccessor.HttpContext?.User.FindFirst(JwtTokenIssuer.SubjectClaim)?.Value;

            return Guid.TryParse(value, out var id)
                ? id
                : throw new UnauthorizedAccessException("The request is not bound to a user.");
        }
    }
}
