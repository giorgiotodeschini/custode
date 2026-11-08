// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Custode.Infrastructure.Vault;

namespace Custode.Server.Features.SecretManagement.Secrets;

internal static class SecretMetadataMapper
{
    public static SecretMetadataResponse ToResponse(Secret secret, string folderPath)
    {
        return new SecretMetadataResponse(
            secret.Id,
            secret.Name,
            SecretValueCodec.FormatType(secret.Type),
            folderPath,
            secret.FileName,
            secret.CreatedAtUtc,
            secret.UpdatedAtUtc,
            secret.CurrentVersion);
    }
}
