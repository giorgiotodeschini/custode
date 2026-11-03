using Custode.Infrastructure.Authentication;
using Microsoft.AspNetCore.Http;

namespace Custode.Infrastructure.Tenancy;

public sealed class HttpTenantContext(IHttpContextAccessor httpContextAccessor) : ITenantContext
{
    public Guid? TenantId
    {
        get
        {
            var value = httpContextAccessor.HttpContext?.User.FindFirst(JwtTokenIssuer.TenantIdClaim)?.Value;

            return Guid.TryParse(value, out var tenantId) ? tenantId : null;
        }
    }
}
