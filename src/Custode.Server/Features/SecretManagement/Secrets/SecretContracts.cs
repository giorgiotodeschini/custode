// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using System.Text.Json;

namespace Custode.Server.Features.SecretManagement.Secrets;

public sealed record SecretPairRequest(string? Key, string? Value);

public sealed record SecretPairResponse(string Key, string Value);

public sealed record SecretWriteRequest(
    string? Name,
    string? Type,
    string? Username,
    string? Password,
    string? Value,
    string? FileName,
    string? ContentBase64,
    JsonElement? Fields = null,
    IReadOnlyList<SecretPairRequest>? Pairs = null);

public sealed record CreateSecretRequest(
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

public sealed record MoveSecretRequest(string? FolderPath);

public sealed record SecretMetadataResponse(
    Guid Id,
    string Name,
    string Type,
    string FolderPath,
    string? FileName,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    int Version);

public sealed record SecretValueResponse(
    Guid Id,
    string Name,
    string Type,
    string FolderPath,
    string? FileName,
    string? Username,
    string? Password,
    string? Value,
    string? ContentBase64,
    IReadOnlyList<SecretPairResponse>? Fields = null,
    IReadOnlyList<SecretPairResponse>? Pairs = null,
    int Version = 0);

public sealed record SecretVersionResponse(
    int Number,
    string Name,
    string? FileName,
    string ChangeKind,
    DateTimeOffset CreatedAtUtc,
    Guid CreatedByUserId,
    int? SourceVersion);
