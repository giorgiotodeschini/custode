// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using System.Text;
using Custode.Infrastructure.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
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
            OnTokenValidated = context =>
            {
                var tenantId = context.Principal?.FindFirst(JwtTokenIssuer.TenantIdClaim)?.Value;

                if (!Guid.TryParse(tenantId, out _))
                {
                    context.Fail("The token is not bound to a tenant.");
                }

                return Task.CompletedTask;
            },
        };
    }
}
