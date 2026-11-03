namespace Custode.Infrastructure.SecretProtection;

public interface ISecretProtector
{
    string Protect(Guid tenantId, string plaintext);

    string Unprotect(Guid tenantId, string protectedValue);
}
