using Microsoft.AspNetCore.Identity;

namespace Custode.Infrastructure.Identity;

public class ApplicationUser : IdentityUser<Guid>
{
    public Guid TenantId { get; set; }
}
