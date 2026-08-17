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
            // --- CHECK: Cancel/refund terminal states require CancelledBy, CancelReason, CancelledAt ---
            // REFUNDED (WP5 buyer-favor dispute resolution) reuses the cancellation
            // fields (CancelledBy=ADMIN, dispute reason) — same forensic trail.
            t.HasCheckConstraint("CK_Transactions_Cancel",
                "(Status <> 'CANCELLED_TIMEOUT' AND Status <> 'CANCELLED_SELLER' AND Status <> 'CANCELLED_BUYER' AND Status <> 'CANCELLED_ADMIN' AND Status <> 'REFUNDED') " +
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
        builder.Property(t => t.DeliveredBuyerAssetId)
            .HasMaxLength(20);

        // --- Delivery Verification (02 §9.2) ---
        builder.Property(t => t.BuyerTradeUrl)
            .HasMaxLength(500);

        builder.Property(t => t.BuyerBaselineAssetIds)
            .HasMaxLength(400);

        // T130 — deliberately NOT capped like the column above. That one is an
        // audit aid whose loss only degrades asset discrimination, so truncating
        // it is survivable; this one is the reference the wrong-item comparison
        // diffs against, and a truncated set makes every dropped class look like
        // a fresh arrival — an invented accusation against a seller. T122
        // measured 159 distinct classes on a real account (~1.8 KB serialized),
        // so nvarchar(max) is the honest size rather than a generous one.
        builder.Property(t => t.BuyerBaselineClassIds);

        // --- Settlement (02 §4.5.1) ---
        // A SettlementReviewReasons constant, not free text — sized for the
        // longest code with room for another, and deliberately not an enum
        // column: the codes travel over the outbox event as strings already.
        builder.Property(t => t.SettlementEscalationReason)
            .HasMaxLength(64);

        // Stored as int, not string: this is a [Flags] enum and the global
        // EnumToStringConverter would persist combinations as comma-joined
        // names, which are awkward to query and brittle to rename.
        builder.Property(t => t.DeliveryEvidence)
            .IsRequired()
            .HasConversion<int>()
            .HasDefaultValue(DeliveryEvidence.NONE);

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

        // --- Unique constraints (06 §5.1) ---
        builder.HasIndex(t => t.InviteToken)
            .IsUnique()
            .HasFilter("[InviteToken] IS NOT NULL")
            .HasDatabaseName("UQ_Transactions_InviteToken");

        // One open transaction per item (02 §2.3). Delivery is verified at the
        // item-class level, so two live transactions targeting the same asset
        // would let an arriving item be attributed to the wrong one — and pay
        // the wrong seller.
        builder.HasIndex(t => new { t.SellerId, t.ItemAssetId })
            .IsUnique()
            .HasFilter(
                "[Status] <> 'COMPLETED' AND [Status] <> 'CANCELLED_TIMEOUT' AND [Status] <> 'CANCELLED_SELLER' " +
                "AND [Status] <> 'CANCELLED_BUYER' AND [Status] <> 'CANCELLED_ADMIN' AND [Status] <> 'REFUNDED' " +
                "AND [IsDeleted] = 0")
            .HasDatabaseName("UQ_Transactions_SellerId_ItemAssetId_Active");

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

        // Delivery verification / seller non-delivery sweep (02 §9.2).
        //
        // T127 orders the sweep by DeliveryRoundAt (nulls first) rather than by
        // the deadline. No index change: this filtered index still resolves the
        // predicate, and what it hands the sort is only the overdue
        // PAYMENT_RECEIVED rows — a set bounded by how many deliveries are in
        // flight, not by table size.
        builder.HasIndex(t => new { t.Status, t.DeliveryDeadline })
            .HasFilter("[Status] = 'PAYMENT_RECEIVED'")
            .HasDatabaseName("IX_Transactions_Delivery_Pending");

        // Settlement sweep: transactions whose reversal window has closed and
        // are awaiting the final "is the item still with the buyer?" check
        // before payout (02 §4.5.1).
        //
        // T129 orders that sweep by SettlementCheckedAt (nulls first) for the
        // same reason T127 orders its own by DeliveryRoundAt: a row whose
        // inventory cannot be read stays eligible indefinitely, so ordering by
        // PayoutEligibleAt would let the oldest unreadable rows hold the window.
        // The filtered index still resolves the predicate and the set it hands
        // the sort is bounded by settlements in flight, not by table size.
        builder.HasIndex(t => new { t.Status, t.PayoutEligibleAt })
            .HasFilter("[Status] = 'ITEM_DELIVERED'")
            .HasDatabaseName("IX_Transactions_Settlement_Pending");
    }
}
