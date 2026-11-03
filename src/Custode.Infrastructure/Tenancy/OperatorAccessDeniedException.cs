namespace Custode.Infrastructure.Tenancy;

public sealed class OperatorAccessDeniedException() : UnauthorizedAccessException("Operator access is required for tenant-independent operations.");
