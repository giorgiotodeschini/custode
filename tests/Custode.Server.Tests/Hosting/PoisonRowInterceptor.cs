// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Custode.Infrastructure.Vault;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Custode.Server.Tests.Hosting;

// Fault injection: fails the database save when a secret with the poison name is being written. Used to show that a
// batch which fails on its last row stores none of its earlier rows.
public sealed class PoisonRowInterceptor : SaveChangesInterceptor
{
    public const string PoisonName = "poison-row";

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Check(eventData);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Check(eventData);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void Check(DbContextEventData eventData)
    {
        var poisoned = eventData.Context?.ChangeTracker.Entries<Secret>()
            .Any(entry => entry.State == EntityState.Added && entry.Entity.Name == PoisonName) ?? false;

        if (poisoned)
        {
            throw new InvalidOperationException("Injected failure while storing the last row.");
        }
    }
}
