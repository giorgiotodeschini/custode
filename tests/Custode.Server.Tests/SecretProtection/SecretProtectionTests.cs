// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using System.Security.Cryptography;
using Custode.Infrastructure.SecretProtection;
using Custode.Infrastructure.Tenancy;
using Custode.Server.Tests.Hosting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;

namespace Custode.Server.Tests.SecretProtection;

public sealed class SecretProtectionTests : IAsyncLifetime
{
    private const string Plaintext = "db-password-7f3a";

    private CustodeTestHost _host = null!;
    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        _host = await CustodeTestHost.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _host.DisposeAsync();
    }

    [Fact]
    public void Protect_ThenUnprotect_RoundTripsWithoutPlaintextInCiphertext()
    {
        var protector = _host.Services.GetRequiredService<ISecretProtector>();

        var ciphertext = protector.Protect(_tenantA, Plaintext);

        Assert.NotEqual(Plaintext, ciphertext);
        Assert.DoesNotContain(Plaintext, ciphertext);
        Assert.Equal(Plaintext, protector.Unprotect(_tenantA, ciphertext));
    }

    [Fact]
    public void SameValue_ProtectedForTwoTenants_ProducesDifferentCiphertexts()
    {
        var protector = _host.Services.GetRequiredService<ISecretProtector>();

        var forA = protector.Protect(_tenantA, Plaintext);
        var forB = protector.Protect(_tenantB, Plaintext);

        Assert.NotEqual(forA, forB);
    }

    [Fact]
    public void Ciphertext_ProtectedForAnotherTenant_CannotBeDecrypted()
    {
        var protector = _host.Services.GetRequiredService<ISecretProtector>();
        var ciphertextOfA = protector.Protect(_tenantA, Plaintext);

        Assert.Throws<CryptographicException>(() => protector.Unprotect(_tenantB, ciphertextOfA));
    }

    [Fact]
    public void Ciphertext_DecryptedUnderDifferentPurpose_Fails()
    {
        var protector = _host.Services.GetRequiredService<ISecretProtector>();
        var ciphertext = protector.Protect(_tenantA, Plaintext);

        var otherPurpose = _host.Services
            .GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("custode.secrets.v1|" + _tenantA + "|other");

        Assert.Throws<CryptographicException>(() => otherPurpose.Unprotect(ciphertext));
    }

    [Fact]
    public void Protect_WithoutTenant_IsRejected()
    {
        var protector = _host.Services.GetRequiredService<ISecretProtector>();

        Assert.Throws<TenantScopeViolationException>(() => protector.Protect(Guid.Empty, Plaintext));
    }
}
