// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using System.Reflection;
using Custode.Infrastructure.Audit;
using Custode.Infrastructure.Identity;
using Custode.Infrastructure.Tenancy;
using Custode.Infrastructure.Vault;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Custode.Infrastructure.Persistence;

public class AppDbContext : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>, IDataProtectionKeyContext
{
    private const string TenantIdProperty = nameof(ITenantOwned.TenantId);

    private readonly DbContextOptions _options;
    private readonly ITenantContext _tenantContext;

    public AppDbContext(DbContextOptions options, ITenantContext tenantContext) : base(options)
    {
        _options = options;
        _tenantContext = tenantContext;
    }

    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    public DbSet<Folder> Folders => Set<Folder>();

    public DbSet<Secret> Secrets => Set<Secret>();

    public DbSet<Group> Groups => Set<Group>();

    public DbSet<GroupMembership> GroupMemberships => Set<GroupMembership>();

    public DbSet<FolderGrant> FolderGrants => Set<FolderGrant>();

    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    public DbSet<AuditChainHead> AuditChainHeads => Set<AuditChainHead>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ConfigureVault();

        var applyTenantFilter = typeof(AppDbContext).GetMethod(
            nameof(ApplyTenantFilter),
            BindingFlags.NonPublic | BindingFlags.Instance)!;

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(ITenantOwned).IsAssignableFrom(entityType.ClrType))
            {
                applyTenantFilter
                    .MakeGenericMethod(entityType.ClrType)
                    .Invoke(this, [modelBuilder]);
            }
        }
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        RejectTamperedAuditEntriesAsync(CancellationToken.None).GetAwaiter().GetResult();

        foreach (var entry in PendingTenantOwnedEntries())
        {
            var storedValues = entry.State == EntityState.Added ? null : entry.GetDatabaseValues();
            EnsureTenantOwnership(entry, storedValues);
        }

        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        await RejectTamperedAuditEntriesAsync(cancellationToken);

        foreach (var entry in PendingTenantOwnedEntries())
        {
            var storedValues = entry.State == EntityState.Added
                ? null
                : await entry.GetDatabaseValuesAsync(cancellationToken);
            EnsureTenantOwnership(entry, storedValues);
        }

        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void ApplyTenantFilter<TEntity>(ModelBuilder modelBuilder) where TEntity : class, ITenantOwned
    {
        modelBuilder.Entity<TEntity>().HasQueryFilter(entity => entity.TenantId == _tenantContext.TenantId);
    }

    // The attempt is recorded in its own transaction before the save is blocked, so the record survives the rejection.
    private async Task RejectTamperedAuditEntriesAsync(CancellationToken cancellationToken)
    {
        var attempts = ChangeTracker.Entries<AuditEntry>()
            .Where(entry => entry.State is EntityState.Modified or EntityState.Deleted)
            .ToList();

        if (attempts.Count == 0)
        {
            return;
        }

        if (_tenantContext.TenantId is { } tenantId)
        {
            var scope = new FixedTenantContext(tenantId);
            await using var auditContext = new AppDbContext(_options, scope);
            var trail = new AuditTrail(auditContext, scope);

            foreach (var attempt in attempts)
            {
                await trail.RecordDeniedAsync(
                    new AuditRecord(
                        Guid.Empty,
                        AuditActions.AuditTamperingAttempt,
                        "audit entry",
                        attempt.Entity.Id.ToString("D"),
                        "operation",
                        attempt.State == EntityState.Deleted ? "delete" : "update"),
                    cancellationToken);
            }
        }

        throw new AuditEntryImmutableException();
    }

    private IEnumerable<EntityEntry> PendingTenantOwnedEntries()
    {
        return ChangeTracker.Entries()
            .Where(entry => entry.Entity is ITenantOwned && entry.State is not (EntityState.Unchanged or EntityState.Detached));
    }

    private void EnsureTenantOwnership(EntityEntry entry, PropertyValues? storedValues)
    {
        var tenantId = ((ITenantOwned)entry.Entity).TenantId;
        var currentTenantId = _tenantContext.TenantId;
        var entityName = entry.Metadata.ClrType.Name;

        if (tenantId == Guid.Empty)
        {
            throw new TenantScopeViolationException($"Tenant-owned entity '{entityName}' has no TenantId.");
        }

        if (currentTenantId is null || tenantId != currentTenantId)
        {
            throw new TenantScopeViolationException(
                $"Tenant-owned entity '{entityName}' belongs to a tenant other than the current one.");
        }

        if (storedValues is null)
        {
            return;
        }

        if ((Guid)storedValues[TenantIdProperty]! != currentTenantId)
        {
            throw new TenantScopeViolationException(
                $"Stored '{entityName}' belongs to a tenant other than the current one.");
        }
    }
}
