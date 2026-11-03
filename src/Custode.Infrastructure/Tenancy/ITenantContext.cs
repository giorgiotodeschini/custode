namespace Custode.Infrastructure.Tenancy;

public interface ITenantContext
{
    Guid? TenantId { get; }
}
