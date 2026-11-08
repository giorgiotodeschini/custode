// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Custode.Infrastructure.Persistence;
using Custode.Infrastructure.Vault;
using Microsoft.EntityFrameworkCore;

namespace Custode.Server.Features.SecurityAudit;

internal sealed class QueryAuditLogHandler(AppDbContext dbContext)
{
    public const int DefaultLimit = 100;
    public const int MaxLimit = 500;

    public async Task<AuditPageResponse> HandleAsync(
        DateTimeOffset? from,
        DateTimeOffset? to,
        Guid? userId,
        string? action,
        string? outcome,
        long? afterSequence,
        int? limit,
        CancellationToken cancellationToken)
    {
        var pageSize = limit ?? DefaultLimit;

        if (pageSize is < 1 or > MaxLimit)
        {
            throw new InvalidVaultRequestException($"The limit must be between 1 and {MaxLimit}.");
        }

        if (afterSequence is < 0)
        {
            throw new InvalidVaultRequestException("The cursor must not be negative.");
        }

        var query = dbContext.AuditEntries.AsNoTracking().AsQueryable();

        if (afterSequence is { } cursor)
        {
            query = query.Where(entry => entry.Sequence > cursor);
        }

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

        // One extra row tells whether another page follows, without a second query.
        var rows = await query.OrderBy(entry => entry.Sequence).Take(pageSize + 1).ToListAsync(cancellationToken);
        var page = rows.Take(pageSize).ToList();
        var next = rows.Count > pageSize ? page[^1].Sequence : (long?)null;

        return new AuditPageResponse(page.Select(entry => new AuditEntryResponse(
            entry.Sequence,
            entry.OccurredAtUtc,
            entry.ActorUserId,
            entry.Action,
            entry.Outcome,
            entry.TargetType,
            entry.TargetId,
            entry.SecondaryTargetType,
            entry.SecondaryTargetId,
            entry.Hash,
            entry.PreviousHash)).ToList(), next);
    }
}
