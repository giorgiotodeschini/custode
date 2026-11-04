// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Custode.Server.Tests.SecretProtection;

public static class TestKeyEncryptionCertificate
{
    public const string Password = "test-kek-password";

    private static readonly Lazy<string> SharedPath = new(() => Create("custode-kek-shared"));

    public static string Shared => SharedPath.Value;

    public static string Create(string name)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest($"CN={name}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));

        var directory = Path.Combine(Path.GetTempPath(), "custode-tests");
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, $"{name}-{Guid.NewGuid():N}.pfx");
        File.WriteAllBytes(path, certificate.Export(X509ContentType.Pfx, Password));

        return path;
    }
}
