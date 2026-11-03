namespace Custode.Infrastructure.Tenancy;

public interface ITenantOwned
{
    Guid TenantId { get; set; }
}
