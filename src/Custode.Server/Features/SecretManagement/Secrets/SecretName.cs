// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Custode.Infrastructure.Vault;

namespace Custode.Server.Features.SecretManagement.Secrets;

internal static class SecretName
{
    private const int MaxLength = 256;

    public static string Normalize(string? name)
    {
        var trimmed = name?.Trim();

        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > MaxLength)
        {
            throw new InvalidVaultRequestException($"A secret name is required and must be at most {MaxLength} characters.");
        }

        return trimmed;
    }
}
