// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Custode.Infrastructure.Access;
using Custode.Infrastructure.Audit;
using Custode.Infrastructure.Vault;
using Microsoft.EntityFrameworkCore;

namespace Custode.Infrastructure.Persistence;

public static class VaultModelBuilderExtensions
{
    public const int MaxNameLength = 256;
    public const int MaxPathLength = 1024;
    public const int MaxHashLength = 64;

    public static ModelBuilder ConfigureVault(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Folder>(folder =>
        {
            folder.HasKey(entity => entity.Id);
            folder.Property(entity => entity.Path).HasMaxLength(MaxPathLength).IsRequired();
            folder.HasIndex(entity => new { entity.TenantId, entity.Path }).IsUnique();
            folder.HasIndex(entity => new { entity.TenantId, entity.ParentId });
            folder.HasOne<Folder>().WithMany().HasForeignKey(entity => entity.ParentId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Secret>(secret =>
        {
            secret.HasKey(entity => entity.Id);
            secret.Property(entity => entity.Name).HasMaxLength(MaxNameLength).IsRequired();
            secret.Property(entity => entity.Type).HasConversion<string>().HasMaxLength(32);
            secret.Property(entity => entity.FileName).HasMaxLength(255);
            secret.Property(entity => entity.ProtectedPayload).IsRequired();
            secret.Property(entity => entity.ConcurrencyStamp).IsConcurrencyToken();
            secret.HasIndex(entity => new { entity.TenantId, entity.FolderId, entity.Name }).IsUnique();
            secret.HasOne<Folder>().WithMany().HasForeignKey(entity => entity.FolderId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SecretVersion>(version =>
        {
            version.HasKey(entity => entity.Id);
            version.Property(entity => entity.Name).HasMaxLength(MaxNameLength).IsRequired();
            version.Property(entity => entity.FileName).HasMaxLength(255);
            version.Property(entity => entity.ProtectedPayload).IsRequired();
            version.Property(entity => entity.ChangeKind).HasConversion<string>().HasMaxLength(16);
            version.HasIndex(entity => new { entity.SecretId, entity.Number }).IsUnique();
            version.HasIndex(entity => new { entity.TenantId, entity.SecretId });
            version.HasOne<Secret>().WithMany().HasForeignKey(entity => entity.SecretId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Group>(group =>
        {
            group.HasKey(entity => entity.Id);
            group.Property(entity => entity.Name).HasMaxLength(MaxNameLength).IsRequired();
            group.HasIndex(entity => new { entity.TenantId, entity.Name }).IsUnique();
        });

        modelBuilder.Entity<GroupMembership>(membership =>
        {
            membership.HasKey(entity => new { entity.GroupId, entity.UserId });
            membership.HasIndex(entity => new { entity.TenantId, entity.UserId });
            membership.HasOne<Group>().WithMany().HasForeignKey(entity => entity.GroupId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<FolderGrant>(grant =>
        {
            grant.HasKey(entity => entity.Id);
            grant.Property(entity => entity.PrincipalKind).HasConversion<string>().HasMaxLength(16);
            grant.Property(entity => entity.Permissions).HasConversion<int>();
            grant.HasIndex(entity => new { entity.FolderId, entity.PrincipalKind, entity.PrincipalId }).IsUnique();
            grant.HasIndex(entity => new { entity.TenantId, entity.PrincipalId });
            grant.HasOne<Folder>().WithMany().HasForeignKey(entity => entity.FolderId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AuditEntry>(entry =>
        {
            entry.ToTable("AuditEntries");
            entry.HasKey(entity => entity.Id);
            entry.Property(entity => entity.Action).HasMaxLength(64).IsRequired();
            entry.Property(entity => entity.Outcome).HasMaxLength(16).IsRequired();
            entry.Property(entity => entity.TargetType).HasMaxLength(64);
            entry.Property(entity => entity.TargetId).HasMaxLength(128);
            entry.Property(entity => entity.SecondaryTargetType).HasMaxLength(64);
            entry.Property(entity => entity.SecondaryTargetId).HasMaxLength(128);
            entry.Property(entity => entity.PreviousHash).HasMaxLength(MaxHashLength);
            entry.Property(entity => entity.Hash).HasMaxLength(MaxHashLength);
            entry.HasIndex(entity => new { entity.TenantId, entity.Sequence }).IsUnique();
        });

        modelBuilder.Entity<AuditChainHead>(head =>
        {
            head.ToTable("AuditChainHeads");
            head.HasKey(entity => entity.TenantId);
            head.Property(entity => entity.LastHash).HasMaxLength(MaxHashLength);
            head.Property(entity => entity.ConcurrencyStamp).IsConcurrencyToken();
        });

        return modelBuilder;
    }
}
