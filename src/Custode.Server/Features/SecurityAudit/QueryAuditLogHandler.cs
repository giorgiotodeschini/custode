// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Custode.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Custode.Server.Features.SecurityAudit;

internal sealed class QueryAuditLogHandler(AppDbContext dbContext)
{
    public async Task<IReadOnlyList<AuditEntryResponse>> HandleAsync(
        DateTimeOffset? from,
        DateTimeOffset? to,
        Guid? userId,
        string? action,
        string? outcome,
        CancellationToken cancellationToken)
    {
        var query = dbContext.AuditEntries.AsNoTracking().AsQueryable();

        if (from is not null)
        {
            query = query.Where(entry => entry.OccurredAtUtc >= from);
        }

        if (to is not null)
        {
            query = query.Where(entry => entry.OccurredAtUtc <= to);
        }

        if (userId is not null)
        {
            query = query.Where(entry => entry.ActorUserId == userId);
        }

        if (!string.IsNullOrWhiteSpace(action))
        {
            query = query.Where(entry => entry.Action == action);
        }

        if (!string.IsNullOrWhiteSpace(outcome))
        {
            query = query.Where(entry => entry.Outcome == outcome);
        }

        var entries = await query.OrderBy(entry => entry.Sequence).ToListAsync(cancellationToken);

        return entries.Select(entry => new AuditEntryResponse(
            entry.Sequence,
            entry.OccurredAtUtc,
            entry.ActorUserId,
            entry.Action,
            entry.Outcome,
            entry.TargetType,
            entry.TargetId,
            entry.SecondaryTargetType,
            entry.SecondaryTargetId)).ToList();
    }
}
