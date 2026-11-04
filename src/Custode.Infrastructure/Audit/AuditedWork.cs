// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Custode.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Custode.Infrastructure.Audit;

public static class AuditedWork
{
    public const int DefaultMaxAttempts = 3;

    public static async Task<T> CommitAsync<T>(
        AppDbContext dbContext,
        Func<Task<T>> unitOfWork,
        CancellationToken cancellationToken,
        int maxAttempts = DefaultMaxAttempts)
    {
        for (var attempt = 1; ; attempt++)
        {
            var result = await unitOfWork();

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                return result;
            }
            catch (DbUpdateException) when (attempt < maxAttempts)
            {
                dbContext.ChangeTracker.Clear();
            }
        }
    }
}
