using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Skinora.Steam.Domain.Entities;
using Skinora.Transactions.Domain.Entities;
using Skinora.Users.Domain.Entities;

namespace Skinora.Steam.Infrastructure.Persistence;

/// <summary>
/// EF Core configuration for <see cref="BotRecoveryItem"/> (T103b-2 — 06 §3.10a).
/// FK delete behaviour is forced to <c>NoAction</c> globally in
/// <c>AppDbContext.OnModelCreating</c> (09 §10.6); enums persist as strings.
/// </summary>
public class BotRecoveryItemConfiguration : IEntityTypeConfiguration<BotRecoveryItem>
{
    public void Configure(EntityTypeBuilder<BotRecoveryItem> builder)
    {
        builder.ToTable("BotRecoveryItems");

        builder.Property(r => r.PlatformSteamBotId)
            .IsRequired();

        builder.Property(r => r.TransactionId)
            .IsRequired();

        builder.Property(r => r.RecoveryStatus)
            .IsRequired();

        builder.Property(r => r.StatusAtRestriction)
            .IsRequired();

        builder.Property(r => r.AdminNote)
            .HasMaxLength(2000);

        // --- Relationships (FK only; navs are joined manually in queries) ---
        builder.HasOne<PlatformSteamBot>()
            .WithMany()
            .HasForeignKey(r => r.PlatformSteamBotId);

        builder.HasOne<Transaction>()
            .WithMany()
            .HasForeignKey(r => r.TransactionId);

        // Responsible admin is a User; nullable FK.
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(r => r.ResponsibleAdminId);

        // --- Indexes ---
        // One recovery row per transaction → idempotent materialisation.
        builder.HasIndex(r => r.TransactionId)
            .IsUnique()
            .HasDatabaseName("UQ_BotRecoveryItems_TransactionId");

        // Per-bot recovery count / queue scans (drives RecoveryTransactionCount
        // + FailoverStatus derivation in AdminSteamBotQueryService).
        builder.HasIndex(r => new { r.PlatformSteamBotId, r.RecoveryStatus })
            .HasDatabaseName("IX_BotRecoveryItems_PlatformSteamBotId_RecoveryStatus");
    }
}
