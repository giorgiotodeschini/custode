// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Custode.Infrastructure.Persistence;
using Custode.Infrastructure.Vault;
using Microsoft.EntityFrameworkCore;

namespace Custode.Server.Features.AccessControl.Common;

internal static class GroupLookup
{
    public static async Task<Group> RequireGroupAsync(AppDbContext dbContext, Guid groupId, CancellationToken cancellationToken)
    {
        return await dbContext.Groups.FirstOrDefaultAsync(group => group.Id == groupId, cancellationToken)
            ?? throw new VaultNotFoundException("The group does not exist.");
    }
}
