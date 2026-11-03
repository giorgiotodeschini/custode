using System.Reflection;
using Custode.Infrastructure.Identity;
using Custode.Infrastructure.Tenancy;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Custode.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions options, ITenantContext tenantContext)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options), IDataProtectionKeyContext
{
    private const string TenantIdProperty = nameof(ITenantOwned.TenantId);

    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

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
        foreach (var entry in PendingTenantOwnedEntries())
        {
            var storedValues = entry.State == EntityState.Added ? null : entry.GetDatabaseValues();
            EnsureTenantOwnership(entry, storedValues);
        }

        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
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
        modelBuilder.Entity<TEntity>().HasQueryFilter(entity => entity.TenantId == tenantContext.TenantId);
    }

    private IEnumerable<EntityEntry> PendingTenantOwnedEntries()
    {
        return ChangeTracker.Entries()
            .Where(entry => entry.Entity is ITenantOwned && entry.State is not (EntityState.Unchanged or EntityState.Detached));
    }

    private void EnsureTenantOwnership(EntityEntry entry, PropertyValues? storedValues)
    {
        var tenantId = ((ITenantOwned)entry.Entity).TenantId;
        var currentTenantId = tenantContext.TenantId;
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
