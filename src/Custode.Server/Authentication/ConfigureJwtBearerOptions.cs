// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using System.Security.Claims;
using System.Text;
using Custode.Infrastructure.Authentication;
using Custode.Infrastructure.Sessions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Custode.Server.Authentication;

public static class ConfigureJwtBearerOptions
{
    public static void Apply(JwtBearerOptions options, JwtOptions jwt)
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            NameClaimType = JwtTokenIssuer.SubjectClaim,
            RoleClaimType = JwtTokenIssuer.RoleClaim,
        };

        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                var principal = context.Principal!;
                var tenantValue = principal.FindFirst(JwtTokenIssuer.TenantIdClaim)?.Value;
                var subjectValue = principal.FindFirst(JwtTokenIssuer.SubjectClaim)?.Value;
                var sessionValue = principal.FindFirst(JwtTokenIssuer.SessionClaim)?.Value;

                if (!Guid.TryParse(tenantValue, out var tenantId))
                {
                    context.Fail("The token is not bound to a tenant.");
                    return;
                }

                if (!Guid.TryParse(subjectValue, out var userId) || !Guid.TryParse(sessionValue, out var sessionId))
                {
                    context.Fail("The token is not bound to a session.");
                    return;
                }

                // Every authenticated request is checked against its session, so revocation applies before the token expires.
                var sessions = context.HttpContext.RequestServices.GetRequiredService<SessionService>();
                var check = await sessions.CheckAsync(tenantId, userId, sessionId, context.HttpContext.RequestAborted);

                if (!check.IsValid)
                {
                    context.Fail("The session is no longer active.");
                    return;
                }

                if (check.PasswordChangeRequired)
                {
                    principal.Identities.First().AddClaim(new Claim(PasswordChangeRequirementMiddleware.ClaimType, "true"));
                }
            },
        };
    }
}
