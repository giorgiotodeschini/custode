namespace Custode.Infrastructure.Tenancy;

public sealed class TenantScopeViolationException(string message) : InvalidOperationException(message);
