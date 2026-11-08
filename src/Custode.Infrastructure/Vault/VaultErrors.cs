// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Microsoft.AspNetCore.Http;

namespace Custode.Infrastructure.Vault;

public abstract class VaultException(string message, int statusCode) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

public sealed class InvalidVaultRequestException(string message) : VaultException(message, StatusCodes.Status400BadRequest);

public sealed class VaultNotFoundException(string message) : VaultException(message, StatusCodes.Status404NotFound);

public sealed class VaultForbiddenException(string message) : VaultException(message, StatusCodes.Status403Forbidden);

public sealed class VaultConflictException(string message) : VaultException(message, StatusCodes.Status409Conflict);

public sealed class VaultPreconditionFailedException(string message) : VaultException(message, StatusCodes.Status412PreconditionFailed);

public sealed class VaultPreconditionRequiredException(string message) : VaultException(message, StatusCodes.Status428PreconditionRequired);

public sealed class VaultPayloadTooLargeException(string message) : VaultException(message, StatusCodes.Status413PayloadTooLarge);
