// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using System.Text.Json;
using Custode.Server.Features.SecretManagement.Secrets;

namespace Custode.Server.Features.SecretManagement.Import;

public sealed record ImportItemRequest(
    string? Name,
    string? Type,
    string? FolderPath,
    string? Username,
    string? Password,
    string? Value,
    string? FileName,
    string? ContentBase64,
    JsonElement? Fields = null,
    IReadOnlyList<SecretPairRequest>? Pairs = null);

public sealed record ImportRequest(bool? CreateMissingFolders, IReadOnlyList<ImportItemRequest>? Items);

public sealed record ImportRowResult(int Index, string Status, string? Message);

public sealed record ImportResponse(Guid? BatchId, bool DryRun, bool Committed, int Created, IReadOnlyList<ImportRowResult> Rows);

public sealed record ImportOutcome(int StatusCode, ImportResponse Body);
