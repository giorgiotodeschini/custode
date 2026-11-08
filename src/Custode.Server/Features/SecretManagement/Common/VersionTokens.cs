// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Custode.Infrastructure.Vault;

namespace Custode.Server.Features.SecretManagement.Common;

// A secret's version number is its version token. It is sent as an ETag and must come back in If-Match.
internal static class VersionTokens
{
    public static string Format(int version) => "\"" + version.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\"";

    public static int ParseIfMatch(string? ifMatch)
    {
        if (string.IsNullOrWhiteSpace(ifMatch))
        {
            throw new VaultPreconditionRequiredException("An If-Match header carrying the secret's version token is required.");
        }

        var trimmed = ifMatch.Trim();

        if (trimmed.Length < 3 || trimmed[0] != '"' || trimmed[^1] != '"' || !int.TryParse(trimmed[1..^1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var version))
        {
            throw new VaultPreconditionFailedException("The If-Match header is not a version token.");
        }

        return version;
    }

    public static void Require(int currentVersion, string? ifMatch)
    {
        if (ParseIfMatch(ifMatch) != currentVersion)
        {
            throw new VaultPreconditionFailedException("The secret changed since it was read. Reload it and try again.");
        }
    }
}
