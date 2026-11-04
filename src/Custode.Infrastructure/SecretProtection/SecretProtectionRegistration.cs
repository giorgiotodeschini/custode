// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Custode.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Custode.Infrastructure.SecretProtection;

public static class SecretProtectionRegistration
{
    public const string CertificatePathKey = "DataProtection:KeyEncryptionCertificatePath";
    public const string CertificatePasswordKey = "DataProtection:KeyEncryptionCertificatePassword";

    public static IServiceCollection AddCustodeSecretProtection(this IServiceCollection services, IConfiguration configuration)
    {
        var status = new SecretProtectionStatus();

        services.AddSingleton(status);
        services.AddSingleton<ISecretProtector, SecretProtector>();
        services.AddHostedService<SecretProtectionStartupCheck>();

        var dataProtection = services
            .AddDataProtection()
            .SetApplicationName("Custode");

        var certificate = LoadKeyEncryptionCertificate(configuration, status);

        if (certificate is null)
        {
            dataProtection.DisableAutomaticKeyGeneration();
        }
        else
        {
            dataProtection
                .PersistKeysToDbContext<AppDbContext>()
                .ProtectKeysWithCertificate(certificate)
                .DisableAutomaticKeyGeneration();
        }

        return services;
    }

    private static X509Certificate2? LoadKeyEncryptionCertificate(IConfiguration configuration, SecretProtectionStatus status)
    {
        var path = configuration[CertificatePathKey];

        if (string.IsNullOrWhiteSpace(path))
        {
            status.MarkConfigurationFailed($"{CertificatePathKey} is not configured.");
            return null;
        }

        if (!File.Exists(path))
        {
            status.MarkConfigurationFailed("The key-encryption certificate file was not found.");
            return null;
        }

        try
        {
            var certificate = X509CertificateLoader.LoadPkcs12FromFile(path, configuration[CertificatePasswordKey]);

            if (!certificate.HasPrivateKey)
            {
                certificate.Dispose();
                status.MarkConfigurationFailed("The key-encryption certificate has no private key.");
                return null;
            }

            return certificate;
        }
        catch (Exception exception) when (exception is CryptographicException or IOException)
        {
            status.MarkConfigurationFailed("The key-encryption certificate could not be loaded.");
            return null;
        }
    }
}
