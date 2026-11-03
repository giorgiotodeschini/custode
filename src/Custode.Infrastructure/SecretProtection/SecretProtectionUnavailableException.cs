namespace Custode.Infrastructure.SecretProtection;

public sealed class SecretProtectionUnavailableException(string reason)
    : InvalidOperationException($"Secret operations are unavailable: {reason}");
