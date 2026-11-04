// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Custode.Infrastructure.Persistence;
using Custode.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Custode.Infrastructure.Audit;

public sealed class AuditTrail(AppDbContext dbContext, ITenantContext tenantContext) : IAuditTrail
{
    public const string Succeeded = "succeeded";
    public const string Denied = "denied";

    public async Task RecordAsync(AuditRecord record, CancellationToken cancellationToken)
    {
        await AppendAsync(record, Succeeded, cancellationToken);
    }

    public async Task RecordDeniedAsync(AuditRecord record, CancellationToken cancellationToken)
    {
        await AuditedWork.CommitAsync(
            dbContext,
            async () =>
            {
                await AppendAsync(record, Denied, cancellationToken);
                return true;
            },
            cancellationToken);
    }

    public async Task<AuditVerification> VerifyAsync(CancellationToken cancellationToken)
    {
        var entries = await dbContext.AuditEntries
            .AsNoTracking()
            .OrderBy(entry => entry.Sequence)
            .ToListAsync(cancellationToken);

        var previousHash = string.Empty;
        long expectedSequence = 1;

        foreach (var entry in entries)
        {
            var expectedHash = AuditHash.Compute(entry, previousHash);

            if (entry.Sequence != expectedSequence || entry.PreviousHash != previousHash || entry.Hash != expectedHash)
            {
                return new AuditVerification(false, expectedSequence - 1, expectedSequence);
            }

            previousHash = entry.Hash;
            expectedSequence++;
        }

        return new AuditVerification(true, entries.Count, null);
    }

    private async Task AppendAsync(AuditRecord record, string outcome, CancellationToken cancellationToken)
    {
        var tenantId = tenantContext.TenantId
            ?? throw new TenantScopeViolationException("Audit entries require a tenant scope.");

        // A head created earlier in the same unit of work is tracked but not yet stored, so check the local cache first.
        var head = dbContext.AuditChainHeads.Local.FirstOrDefault(chainHead => chainHead.TenantId == tenantId)
            ?? await dbContext.AuditChainHeads.FirstOrDefaultAsync(chainHead => chainHead.TenantId == tenantId, cancellationToken);

        if (head is null)
        {
            head = new AuditChainHead { TenantId = tenantId, LastSequence = 0, LastHash = string.Empty, ConcurrencyStamp = Guid.NewGuid() };
            dbContext.AuditChainHeads.Add(head);
        }

        var entry = new AuditEntry
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Sequence = head.LastSequence + 1,
            OccurredAtUtc = MicrosecondPrecision(DateTimeOffset.UtcNow),
            ActorUserId = record.ActorUserId,
            Action = record.Action,
            Outcome = outcome,
            TargetType = record.TargetType,
            TargetId = record.TargetId,
            SecondaryTargetType = record.SecondaryTargetType,
            SecondaryTargetId = record.SecondaryTargetId,
            PreviousHash = head.LastHash,
        };

        entry.Hash = AuditHash.Compute(entry, head.LastHash);

        head.LastSequence = entry.Sequence;
        head.LastHash = entry.Hash;
        head.ConcurrencyStamp = Guid.NewGuid();

        dbContext.AuditEntries.Add(entry);
    }

    private static DateTimeOffset MicrosecondPrecision(DateTimeOffset value)
    {
        return new DateTimeOffset(value.Ticks - value.Ticks % 10, TimeSpan.Zero);
    }
}
