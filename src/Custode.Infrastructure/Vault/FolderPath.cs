// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

namespace Custode.Infrastructure.Vault;

public static class FolderPath
{
    public const string Root = "/";

    public static string Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !path.StartsWith('/'))
        {
            throw new InvalidVaultRequestException("A folder path must be absolute and start with '/'.");
        }

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (segments.Any(segment => segment is "." or ".."))
        {
            throw new InvalidVaultRequestException("A folder path must not contain '.' or '..' segments.");
        }

        return segments.Length == 0 ? Root : "/" + string.Join('/', segments);
    }

    public static string? ParentOf(string normalizedPath)
    {
        if (normalizedPath == Root)
        {
            return null;
        }

        var index = normalizedPath.LastIndexOf('/');

        return index == 0 ? Root : normalizedPath[..index];
    }

    public static IReadOnlyList<string> AncestorsAndSelf(string normalizedPath)
    {
        var paths = new List<string> { Root };
        var current = string.Empty;

        foreach (var segment in normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            current += "/" + segment;
            paths.Add(current);
        }

        return paths;
    }
}
