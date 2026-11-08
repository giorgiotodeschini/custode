// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using System.Text.Json;
using Custode.Infrastructure.SecretProtection;
using Custode.Infrastructure.Vault;
using Microsoft.Extensions.Options;

namespace Custode.Server.Features.SecretManagement.Secrets;

internal sealed record SecretPairInput(string? Key, string? Value);

internal sealed record SecretValueInput(
    string? Username,
    string? Password,
    string? Value,
    string? FileName,
    string? ContentBase64,
    JsonElement? Fields = null,
    IReadOnlyList<SecretPairInput>? Pairs = null);

internal sealed record StoredSecretPair(string Key, string Value);

internal sealed record StoredSecretValue(
    string? Username,
    string? Password,
    string? Value,
    string? ContentBase64,
    IReadOnlyList<StoredSecretPair>? Pairs = null);

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
            SecretType.Structured => ValidateStructured(input),
            SecretType.KeyValue => ValidateKeyValue(input),
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
            "structured" => SecretType.Structured,
            "key-value" => SecretType.KeyValue,
            _ => throw new InvalidVaultRequestException("The secret type must be one of: username-password, pat, api-key, file, structured, key-value."),
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
            SecretType.Structured => "structured",
            SecretType.KeyValue => "key-value",
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

    public const int MaxStructuredFields = 100;
    public const int MaxKeyValuePairs = 200;
    public const int MaxNameLength = 128;
    public const int MaxValueLength = 4096;

    private static StoredSecretValue ValidateStructured(SecretValueInput input)
    {
        if (input.Fields is not { } fields || fields.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidVaultRequestException("A structured secret requires a JSON object of named string fields.");
        }

        var pairs = new List<StoredSecretPair>();
        var names = new HashSet<string>(StringComparer.Ordinal);

        // Enumerating the object exposes repeated property names, which deserializing into a dictionary would hide.
        foreach (var property in fields.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String)
            {
                throw new InvalidVaultRequestException("Each field of a structured secret must have a string value.");
            }

            if (!names.Add(property.Name))
            {
                throw new InvalidVaultRequestException("A structured secret must not contain a field name more than once.");
            }

            pairs.Add(new StoredSecretPair(ValidateName(property.Name, "field"), ValidateValue(property.Value.GetString()!)));
        }

        if (pairs.Count is 0 or > MaxStructuredFields)
        {
            throw new InvalidVaultRequestException($"A structured secret requires between 1 and {MaxStructuredFields} fields.");
        }

        return new StoredSecretValue(null, null, null, null, pairs);
    }

    private static StoredSecretValue ValidateKeyValue(SecretValueInput input)
    {
        if (input.Pairs is null)
        {
            throw new InvalidVaultRequestException("A key-value secret requires an ordered list of key and value pairs.");
        }

        if (input.Pairs.Count is 0 or > MaxKeyValuePairs)
        {
            throw new InvalidVaultRequestException($"A key-value secret requires between 1 and {MaxKeyValuePairs} pairs.");
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        var pairs = new List<StoredSecretPair>();

        foreach (var pair in input.Pairs)
        {
            var key = ValidateName(pair.Key, "key");

            if (!keys.Add(key))
            {
                throw new InvalidVaultRequestException("A key-value secret must not contain a key more than once.");
            }

            pairs.Add(new StoredSecretPair(key, ValidateValue(pair.Value ?? string.Empty)));
        }

        return new StoredSecretValue(null, null, null, null, pairs);
    }

    private static string ValidateName(string? name, string kind)
    {
        if (string.IsNullOrEmpty(name))
        {
            throw new InvalidVaultRequestException($"A {kind} name must not be empty.");
        }

        if (name.Length > MaxNameLength)
        {
            throw new InvalidVaultRequestException($"A {kind} name must not exceed {MaxNameLength} characters.");
        }

        return name;
    }

    private static string ValidateValue(string value)
    {
        if (value.Length > MaxValueLength)
        {
            throw new InvalidVaultRequestException($"A value must not exceed {MaxValueLength} characters.");
        }

        return value;
    }

    private static long DecodedLength(string base64)
    {
        var padding = base64.EndsWith("==", StringComparison.Ordinal) ? 2 : base64.EndsWith('=') ? 1 : 0;

        return (long)base64.Length / 4 * 3 - padding;
    }
}
