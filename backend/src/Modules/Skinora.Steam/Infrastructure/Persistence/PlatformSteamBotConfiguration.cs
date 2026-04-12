using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Skinora.Steam.Domain.Entities;
using Skinora.Transactions.Domain.Entities;

namespace Skinora.Steam.Infrastructure.Persistence;

/// <summary>
/// EF Core configuration for <see cref="PlatformSteamBot"/>.
/// 06 §3.10, §4.1, §5.1.
/// </summary>
public class PlatformSteamBotConfiguration : IEntityTypeConfiguration<PlatformSteamBot>
{
    public void Configure(EntityTypeBuilder<PlatformSteamBot> builder)
    {
        builder.ToTable("PlatformSteamBots");

        // --- Properties ---
        builder.Property(b => b.SteamId)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(b => b.DisplayName)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(b => b.Status)
            .IsRequired();

        builder.Property(b => b.ActiveEscrowCount)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(b => b.DailyTradeOfferCount)
            .IsRequired()
            .HasDefaultValue(0);

        // --- ISoftDeletable ---
        builder.Property(b => b.IsDeleted)
            .IsRequired()
            .HasDefaultValue(false);

        builder.HasQueryFilter(b => !b.IsDeleted);

        // --- Relationships (06 §4.1) ---
        // Transaction.EscrowBotId → PlatformSteamBot (N:1)
        // Configured from Steam module side to avoid circular project references.
        builder.HasMany<Transaction>()
            .WithOne()
            .HasForeignKey(t => t.EscrowBotId);

        // --- Unique constraints (06 §5.1) ---
        builder.HasIndex(b => b.SteamId)
            .IsUnique()
            .HasDatabaseName("UQ_PlatformSteamBots_SteamId");
    }
}
