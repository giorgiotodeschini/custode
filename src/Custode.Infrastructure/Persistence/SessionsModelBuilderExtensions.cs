// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Custode.Infrastructure.Administration;
using Custode.Infrastructure.Sessions;
using Microsoft.EntityFrameworkCore;

namespace Custode.Infrastructure.Persistence;

public static class SessionsModelBuilderExtensions
{
    public static ModelBuilder ConfigureSessionsAndPolicies(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Session>(session =>
        {
            session.ToTable("Sessions");
            session.HasKey(entity => entity.Id);
            session.Property(entity => entity.RevocationReason).HasMaxLength(64);
            session.HasIndex(entity => new { entity.TenantId, entity.UserId });
        });

        modelBuilder.Entity<RefreshToken>(token =>
        {
            token.ToTable("RefreshTokens");
            token.HasKey(entity => entity.Id);
            token.Property(entity => entity.TokenHash).HasMaxLength(VaultModelBuilderExtensions.MaxHashLength).IsRequired();
            token.Property(entity => entity.ConcurrencyStamp).IsConcurrencyToken();
            token.HasIndex(entity => entity.TokenHash).IsUnique();
            token.HasIndex(entity => new { entity.TenantId, entity.SessionId });
            token.HasOne<Session>().WithMany().HasForeignKey(entity => entity.SessionId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TenantPolicy>(policy =>
        {
            policy.ToTable("TenantPolicies");
            policy.HasKey(entity => entity.TenantId);
        });

        return modelBuilder;
    }
}
