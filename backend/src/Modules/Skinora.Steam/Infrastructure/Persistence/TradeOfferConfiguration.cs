using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Skinora.Steam.Domain.Entities;
using Skinora.Transactions.Domain.Entities;

namespace Skinora.Steam.Infrastructure.Persistence;

/// <summary>
/// EF Core configuration for <see cref="TradeOffer"/>.
/// 06 §3.9, §4.1, §4.2, §5.1, §5.2.
/// </summary>
public class TradeOfferConfiguration : IEntityTypeConfiguration<TradeOffer>
{
    public void Configure(EntityTypeBuilder<TradeOffer> builder)
    {
        builder.ToTable("TradeOffers");

        // --- Properties ---
        builder.Property(t => t.TransactionId)
            .IsRequired();

        builder.Property(t => t.PlatformSteamBotId)
            .IsRequired();

        builder.Property(t => t.Direction)
            .IsRequired();

        builder.Property(t => t.SteamTradeOfferId)
            .HasMaxLength(20);

        builder.Property(t => t.Status)
            .IsRequired();

        builder.Property(t => t.RetryCount)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(t => t.ErrorMessage)
            .HasMaxLength(500);

        // --- Relationships (06 §4.1, §4.2 — NO ACTION cascade) ---
        builder.HasOne<Transaction>()
            .WithMany()
            .HasForeignKey(t => t.TransactionId);

        builder.HasOne<PlatformSteamBot>()
            .WithMany()
            .HasForeignKey(t => t.PlatformSteamBotId);

        // --- Unique constraints (06 §5.1) ---
        // Filtered unique — only non-null SteamTradeOfferId values must be unique.
        // Prevents duplicate trade offers from retry/integration errors.
        builder.HasIndex(t => t.SteamTradeOfferId)
            .IsUnique()
            .HasFilter("[SteamTradeOfferId] IS NOT NULL")
            .HasDatabaseName("UQ_TradeOffers_SteamTradeOfferId");

        // --- Performance indexes (06 §5.2) ---
        builder.HasIndex(t => t.TransactionId)
            .HasDatabaseName("IX_TradeOffers_TransactionId");

        builder.HasIndex(t => t.PlatformSteamBotId)
            .HasDatabaseName("IX_TradeOffers_PlatformSteamBotId");

        // --- State-dependent CHECK constraints (06 §3.9) ---

        // SENT, ACCEPTED, DECLINED, EXPIRED → SentAt NOT NULL
        builder.ToTable(t => t.HasCheckConstraint("CK_TradeOffers_Sent_SentAt",
            "(Status <> 'SENT') OR (SentAt IS NOT NULL)"));

        builder.ToTable(t => t.HasCheckConstraint("CK_TradeOffers_Accepted_SentAt",
            "(Status <> 'ACCEPTED') OR (SentAt IS NOT NULL)"));

        builder.ToTable(t => t.HasCheckConstraint("CK_TradeOffers_Declined_SentAt",
            "(Status <> 'DECLINED') OR (SentAt IS NOT NULL)"));

        builder.ToTable(t => t.HasCheckConstraint("CK_TradeOffers_Expired_SentAt",
            "(Status <> 'EXPIRED') OR (SentAt IS NOT NULL)"));

        // ACCEPTED, DECLINED, EXPIRED → RespondedAt NOT NULL
        builder.ToTable(t => t.HasCheckConstraint("CK_TradeOffers_Accepted_RespondedAt",
            "(Status <> 'ACCEPTED') OR (RespondedAt IS NOT NULL)"));

        builder.ToTable(t => t.HasCheckConstraint("CK_TradeOffers_Declined_RespondedAt",
            "(Status <> 'DECLINED') OR (RespondedAt IS NOT NULL)"));

        builder.ToTable(t => t.HasCheckConstraint("CK_TradeOffers_Expired_RespondedAt",
            "(Status <> 'EXPIRED') OR (RespondedAt IS NOT NULL)"));
    }
}
