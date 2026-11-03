using System.Security.Claims;
using System.Text;
using Custode.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Custode.Infrastructure.Authentication;

public sealed class JwtTokenIssuer(
    IOptions<JwtOptions> options,
    UserManager<ApplicationUser> userManager) : ITokenIssuer
{
    public const string SubjectClaim = "sub";
    public const string TenantIdClaim = "tenant_id";
    public const string RoleClaim = "role";

    public async Task<IssuedToken> IssueAsync(ApplicationUser user, CancellationToken cancellationToken = default)
    {
        var jwt = options.Value;
        var roles = await userManager.GetRolesAsync(user);

        var issuedAt = DateTimeOffset.UtcNow;
        var expiresAt = issuedAt.AddMinutes(jwt.LifetimeMinutes);

        var claims = new List<Claim>
        {
            new(SubjectClaim, user.Id.ToString()),
            new(TenantIdClaim, user.TenantId.ToString()),
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
