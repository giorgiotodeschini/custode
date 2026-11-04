// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

namespace Custode.Server.Features.SecretManagement.Secrets;

public sealed record SecretWriteRequest(
    string? Name,
    string? Type,
    string? Username,
    string? Password,
    string? Value,
    string? FileName,
    string? ContentBase64);

public sealed record CreateSecretRequest(
    string? Name,
    string? Type,
    string? FolderPath,
    string? Username,
    string? Password,
    string? Value,
    string? FileName,
    string? ContentBase64);

public sealed record MoveSecretRequest(string? FolderPath);

public sealed record SecretMetadataResponse(
    Guid Id,
    string Name,
    string Type,
    string FolderPath,
    string? FileName,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record SecretValueResponse(
    Guid Id,
    string Name,
    string Type,
    string FolderPath,
    string? FileName,
    string? Username,
    string? Password,
    string? Value,
    string? ContentBase64);
