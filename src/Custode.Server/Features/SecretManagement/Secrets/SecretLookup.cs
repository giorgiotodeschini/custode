// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Custode.Infrastructure.Persistence;
using Custode.Infrastructure.Vault;
using Microsoft.EntityFrameworkCore;

namespace Custode.Server.Features.SecretManagement.Secrets;

internal static class SecretLookup
{
    public static async Task<Secret> FindAsync(AppDbContext dbContext, Guid secretId, CancellationToken cancellationToken)
    {
        return await dbContext.Secrets.FirstOrDefaultAsync(secret => secret.Id == secretId, cancellationToken)
            ?? throw new VaultNotFoundException("The requested folder or item does not exist.");
    }
}
