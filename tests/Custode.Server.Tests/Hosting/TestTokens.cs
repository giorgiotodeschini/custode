// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using System.Security.Claims;
using System.Text;
using Custode.Server.Tests.Identity;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Custode.Server.Tests.Hosting;

public static class TestTokens
{
    public const string OtherSigningKey = "another-signing-key-that-is-long-enough-for-hs256";

    public static string Create(
        IEnumerable<Claim> claims,
        DateTime expiresAtUtc,
        string signingKey = IdentityTestServices.SigningKey)
    {
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = IdentityTestServices.Issuer,
            Audience = IdentityTestServices.Audience,
            Expires = expiresAtUtc,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
                SecurityAlgorithms.HmacSha256),
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    public static string ForUser(Guid userId, Guid tenantId, Guid sessionId, params string[] roles)
    {
        var claims = new List<Claim>
        {
            new("sub", userId.ToString()),
            new("tenant_id", tenantId.ToString()),
            new("sid", sessionId.ToString()),
        };
        claims.AddRange(roles.Select(role => new Claim("role", role)));

        return Create(claims, DateTime.UtcNow.AddMinutes(10));
    }
}
