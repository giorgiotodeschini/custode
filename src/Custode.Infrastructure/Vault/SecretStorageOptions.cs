// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

namespace Custode.Infrastructure.Vault;

public sealed class SecretStorageOptions
{
    public const string SectionName = "Secrets";

    public const long DefaultMaxFileSizeBytes = 1024 * 1024;

    public long MaxFileSizeBytes { get; set; } = DefaultMaxFileSizeBytes;

    public const long DefaultMaxImportBodyBytes = 5L * 1024 * 1024;

    public long MaxImportBodyBytes { get; set; } = DefaultMaxImportBodyBytes;
}
