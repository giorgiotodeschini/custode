// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using System.Security.Claims;
using System.Text;
using Custode.Infrastructure.Administration;
using Custode.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Custode.Infrastructure.Authentication;

public sealed class JwtTokenIssuer(
    IOptions<JwtOptions> options,
    UserManager<ApplicationUser> userManager,
    TenantPolicyReader policies) : ITokenIssuer
{
    public const string SubjectClaim = "sub";
    public const string TenantIdClaim = "tenant_id";
    public const string RoleClaim = "role";
    public const string SessionClaim = "sid";

    public async Task<IssuedToken> IssueAsync(ApplicationUser user, Guid sessionId, CancellationToken cancellationToken = default)
    {
        var jwt = options.Value;
        var roles = await userManager.GetRolesAsync(user);
        var policy = await policies.GetAsync(user.TenantId, cancellationToken);

        var issuedAt = DateTimeOffset.UtcNow;
        var expiresAt = issuedAt.AddMinutes(policy.AccessTokenLifetimeMinutes);

        var claims = new List<Claim>
        {
            new(SubjectClaim, user.Id.ToString()),
            new(TenantIdClaim, user.TenantId.ToString()),
            new(SessionClaim, sessionId.ToString()),
        };
        claims.AddRange(roles.Select(role => new Claim(RoleClaim, role)));

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = jwt.Issuer,
            Audience = jwt.Audience,
            IssuedAt = issuedAt.UtcDateTime,
            NotBefore = issuedAt.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
                SecurityAlgorithms.HmacSha256),
        };

        var accessToken = new JsonWebTokenHandler().CreateToken(descriptor);

        return new IssuedToken(accessToken, expiresAt);
    }
}
