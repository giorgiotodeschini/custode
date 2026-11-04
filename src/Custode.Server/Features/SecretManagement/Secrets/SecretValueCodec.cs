// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using System.Text.Json;
using Custode.Infrastructure.SecretProtection;
using Custode.Infrastructure.Vault;
using Microsoft.Extensions.Options;

namespace Custode.Server.Features.SecretManagement.Secrets;

internal sealed record SecretValueInput(
    string? Username,
    string? Password,
    string? Value,
    string? FileName,
    string? ContentBase64);

internal sealed record StoredSecretValue(
    string? Username,
    string? Password,
    string? Value,
    string? ContentBase64);

internal sealed record ValidatedSecretValue(string? FileName, string ProtectedPayload);

internal sealed class SecretValueCodec(ISecretProtector protector, IOptions<SecretStorageOptions> storageOptions)
{
    private static readonly HashSet<string> AllowedFileExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".crt", ".cer", ".pem", ".pfx", ".p12", ".key",
    };

    public ValidatedSecretValue Protect(Guid tenantId, SecretType type, SecretValueInput input)
    {
        var stored = type switch
        {
            SecretType.UsernamePassword => ValidateUsernamePassword(input),
            SecretType.Pat or SecretType.ApiKey => ValidateSingleValue(input),
            SecretType.File => ValidateFile(input),
            _ => throw new InvalidVaultRequestException("Unsupported secret type."),
        };

        var fileName = type == SecretType.File ? input.FileName : null;
        var payload = protector.Protect(tenantId, JsonSerializer.Serialize(stored));

        return new ValidatedSecretValue(fileName, payload);
    }

    public StoredSecretValue Unprotect(Guid tenantId, string protectedPayload)
    {
        return JsonSerializer.Deserialize<StoredSecretValue>(protector.Unprotect(tenantId, protectedPayload))
            ?? throw new InvalidOperationException("The stored secret payload is empty.");
    }

    public static SecretType ParseType(string? raw)
    {
        return raw switch
        {
            "username-password" => SecretType.UsernamePassword,
            "pat" => SecretType.Pat,
            "api-key" => SecretType.ApiKey,
            "file" => SecretType.File,
            _ => throw new InvalidVaultRequestException("The secret type must be one of: username-password, pat, api-key, file."),
        };
    }

    public static string FormatType(SecretType type)
    {
        return type switch
        {
            SecretType.UsernamePassword => "username-password",
            SecretType.Pat => "pat",
            SecretType.ApiKey => "api-key",
            SecretType.File => "file",
            _ => throw new InvalidOperationException("Unsupported secret type."),
        };
    }

    private static StoredSecretValue ValidateUsernamePassword(SecretValueInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Username) || string.IsNullOrEmpty(input.Password))
        {
            throw new InvalidVaultRequestException("A username/password secret requires both a username and a password.");
        }

        return new StoredSecretValue(input.Username, input.Password, null, null);
    }

    private static StoredSecretValue ValidateSingleValue(SecretValueInput input)
    {
        if (string.IsNullOrEmpty(input.Value))
        {
            throw new InvalidVaultRequestException("This secret type requires a value.");
        }

        return new StoredSecretValue(null, null, input.Value, null);
    }

    private StoredSecretValue ValidateFile(SecretValueInput input)
    {
        if (string.IsNullOrWhiteSpace(input.FileName) || Path.GetFileName(input.FileName) != input.FileName)
        {
            throw new InvalidVaultRequestException("A file-based secret requires a plain file name.");
        }

        if (!AllowedFileExtensions.Contains(Path.GetExtension(input.FileName)))
        {
            throw new InvalidVaultRequestException("The file extension is not supported. Allowed: .crt, .cer, .pem, .pfx, .p12, .key.");
        }

        if (string.IsNullOrEmpty(input.ContentBase64))
        {
            throw new InvalidVaultRequestException("A file-based secret requires file content.");
        }

        var decodedLength = DecodedLength(input.ContentBase64);

        if (decodedLength > storageOptions.Value.MaxFileSizeBytes)
        {
            throw new InvalidVaultRequestException("The file exceeds the maximum size allowed by the server.");
        }

        if (!Convert.TryFromBase64String(input.ContentBase64, new byte[decodedLength], out _))
        {
            throw new InvalidVaultRequestException("The file content is not valid base64.");
        }

        return new StoredSecretValue(null, null, null, input.ContentBase64);
    }

    private static long DecodedLength(string base64)
    {
        var padding = base64.EndsWith("==", StringComparison.Ordinal) ? 2 : base64.EndsWith('=') ? 1 : 0;

        return (long)base64.Length / 4 * 3 - padding;
    }
}
