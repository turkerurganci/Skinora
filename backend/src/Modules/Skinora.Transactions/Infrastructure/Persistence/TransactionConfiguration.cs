using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Skinora.Shared.Enums;
using Skinora.Transactions.Domain.Entities;

namespace Skinora.Transactions.Infrastructure.Persistence;

/// <summary>
/// EF Core configuration for Transaction entity — 06 §3.5, §4.1, §5.1, §5.2, §8.3, §8.7.
/// </summary>
public class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
{
    public void Configure(EntityTypeBuilder<Transaction> builder)
    {
        builder.ToTable("Transactions", t =>
        {
            // --- CHECK: Cancel states require CancelledBy, CancelReason, CancelledAt ---
            t.HasCheckConstraint("CK_Transactions_Cancel",
                "(Status <> 'CANCELLED_TIMEOUT' AND Status <> 'CANCELLED_SELLER' AND Status <> 'CANCELLED_BUYER' AND Status <> 'CANCELLED_ADMIN') " +
                "OR (CancelledBy IS NOT NULL AND CancelReason IS NOT NULL AND CancelledAt IS NOT NULL)");

            // --- CHECK: Emergency hold active → hold fields required ---
            t.HasCheckConstraint("CK_Transactions_Hold",
                "(IsOnHold = 0) " +
                "OR (EmergencyHoldAt IS NOT NULL AND EmergencyHoldReason IS NOT NULL AND EmergencyHoldByAdminId IS NOT NULL)");

            // --- CHECK: Timeout freeze active → reason and remaining required ---
            t.HasCheckConstraint("CK_Transactions_FreezeActive",
                "(TimeoutFrozenAt IS NULL) " +
                "OR (TimeoutFreezeReason IS NOT NULL AND TimeoutRemainingSeconds IS NOT NULL)");

            // --- CHECK: Timeout freeze passive → reason and remaining must be null ---
            t.HasCheckConstraint("CK_Transactions_FreezePassive",
                "(TimeoutFrozenAt IS NOT NULL) " +
                "OR (TimeoutFreezeReason IS NULL AND TimeoutRemainingSeconds IS NULL)");

            // --- CHECK: Freeze-hold mutual binding ---
            // EMERGENCY_HOLD freeze reason requires IsOnHold = 1
            t.HasCheckConstraint("CK_Transactions_FreezeHold_Forward",
                "(TimeoutFreezeReason != 'EMERGENCY_HOLD') " +
                "OR (IsOnHold = 1)");

            // IsOnHold = 1 requires freeze with EMERGENCY_HOLD reason
            t.HasCheckConstraint("CK_Transactions_FreezeHold_Reverse",
                "(IsOnHold = 0) " +
                "OR (TimeoutFrozenAt IS NOT NULL AND TimeoutFreezeReason = 'EMERGENCY_HOLD')");

            // --- CHECK: Buyer identification method ---
            // STEAM_ID → TargetBuyerSteamId NOT NULL, InviteToken NULL
            t.HasCheckConstraint("CK_Transactions_BuyerMethod_SteamId",
                "(BuyerIdentificationMethod != 'STEAM_ID') " +
                "OR (TargetBuyerSteamId IS NOT NULL AND InviteToken IS NULL)");

            // OPEN_LINK → InviteToken NOT NULL, TargetBuyerSteamId NULL
            t.HasCheckConstraint("CK_Transactions_BuyerMethod_OpenLink",
                "(BuyerIdentificationMethod != 'OPEN_LINK') " +
                "OR (InviteToken IS NOT NULL AND TargetBuyerSteamId IS NULL)");
        });

        // --- Primary key ---
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();

        // --- Status ---
        builder.Property(t => t.Status)
            .IsRequired();

        // --- Parties ---
        builder.Property(t => t.SellerId)
            .IsRequired();

        builder.Property(t => t.TargetBuyerSteamId)
            .HasMaxLength(20);

        builder.Property(t => t.InviteToken)
            .HasMaxLength(64);

        // --- Item Snapshot ---
        builder.Property(t => t.ItemAssetId)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(t => t.ItemClassId)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(t => t.ItemInstanceId)
            .HasMaxLength(20);

        builder.Property(t => t.ItemName)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(t => t.ItemIconUrl)
            .HasMaxLength(500);

        builder.Property(t => t.ItemExterior)
            .HasMaxLength(50);

        builder.Property(t => t.ItemType)
            .HasMaxLength(100);

        builder.Property(t => t.ItemInspectLink)
            .HasMaxLength(500);

        // --- Item Asset Lineage ---
        builder.Property(t => t.EscrowBotAssetId)
            .HasMaxLength(20);

        builder.Property(t => t.DeliveredBuyerAssetId)
            .HasMaxLength(20);

        // --- Price & Commission (06 §8.3) ---
        builder.Property(t => t.StablecoinType)
            .IsRequired();

        builder.Property(t => t.Price)
            .IsRequired()
            .HasPrecision(18, 6);

        builder.Property(t => t.CommissionRate)
            .IsRequired()
            .HasPrecision(5, 4);

        builder.Property(t => t.CommissionAmount)
            .IsRequired()
            .HasPrecision(18, 6);

        builder.Property(t => t.TotalAmount)
            .IsRequired()
            .HasPrecision(18, 6);

        builder.Property(t => t.MarketPriceAtCreation)
            .HasPrecision(18, 6);

        // --- Wallet Addresses ---
        builder.Property(t => t.SellerPayoutAddress)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(t => t.BuyerRefundAddress)
            .HasMaxLength(50);

        // --- Timeout ---
        builder.Property(t => t.PaymentTimeoutMinutes)
            .IsRequired();

        builder.Property(t => t.TimeoutRemainingSeconds);

        // --- Emergency Hold ---
        builder.Property(t => t.IsOnHold)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(t => t.EmergencyHoldReason)
            .HasMaxLength(500);

        // --- Hangfire Job IDs ---
        builder.Property(t => t.PaymentTimeoutJobId)
            .HasMaxLength(50);

        builder.Property(t => t.TimeoutWarningJobId)
            .HasMaxLength(50);

        // --- Cancellation ---
        builder.Property(t => t.CancelReason)
            .HasMaxLength(500);

        // --- Dispute ---
        builder.Property(t => t.HasActiveDispute)
            .IsRequired()
            .HasDefaultValue(false);

        // --- ISoftDeletable ---
        builder.Property(t => t.IsDeleted)
            .IsRequired()
            .HasDefaultValue(false);

        // --- Relationships (06 §4.1) ---
        // Transaction → User (seller)
        builder.HasOne<Skinora.Users.Domain.Entities.User>()
            .WithMany()
            .HasForeignKey(t => t.SellerId);

        // Transaction → User (buyer)
        builder.HasOne<Skinora.Users.Domain.Entities.User>()
            .WithMany()
            .HasForeignKey(t => t.BuyerId);

        // Transaction → User (emergency hold admin)
        builder.HasOne<Skinora.Users.Domain.Entities.User>()
            .WithMany()
            .HasForeignKey(t => t.EmergencyHoldByAdminId);

        // Transaction → TransactionHistory (navigation)
        builder.HasMany(t => t.History)
            .WithOne(h => h.Transaction)
            .HasForeignKey(h => h.TransactionId);

        // NOTE: EscrowBotId → PlatformSteamBot FK will be configured in T21
        // when PlatformSteamBot entity is created.

        // --- Unique constraints (06 §5.1) ---
        builder.HasIndex(t => t.InviteToken)
            .IsUnique()
            .HasFilter("[InviteToken] IS NOT NULL")
            .HasDatabaseName("UQ_Transactions_InviteToken");

        // --- Performance indexes (06 §5.2) ---
        builder.HasIndex(t => t.Status)
            .HasFilter("[Status] <> 'COMPLETED' AND [Status] <> 'CANCELLED_TIMEOUT' AND [Status] <> 'CANCELLED_SELLER' AND [Status] <> 'CANCELLED_BUYER' AND [Status] <> 'CANCELLED_ADMIN'")
            .HasDatabaseName("IX_Transactions_Status_Active");

        builder.HasIndex(t => t.SellerId)
            .HasDatabaseName("IX_Transactions_SellerId");

        builder.HasIndex(t => t.BuyerId)
            .HasDatabaseName("IX_Transactions_BuyerId");

        builder.HasIndex(t => t.CreatedAt)
            .HasDatabaseName("IX_Transactions_CreatedAt");

        builder.HasIndex(t => t.EscrowBotId)
            .HasDatabaseName("IX_Transactions_EscrowBotId");
    }
}
