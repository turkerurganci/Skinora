using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Skinora.Transactions.Domain.Entities;

namespace Skinora.Transactions.Infrastructure.Persistence;

/// <summary>
/// EF Core configuration for BlockchainTransaction entity — 06 §3.8, §4.1, §5.1, §5.2.
/// Includes type-dependent and status-dependent CHECK constraints.
/// </summary>
public class BlockchainTransactionConfiguration : IEntityTypeConfiguration<BlockchainTransaction>
{
    public void Configure(EntityTypeBuilder<BlockchainTransaction> builder)
    {
        builder.ToTable("BlockchainTransactions", t =>
        {
            // ===== Type-dependent CHECK constraints (06 §3.8) =====

            // BUYER_PAYMENT: PaymentAddressId NOT NULL, ActualTokenAddress NULL
            t.HasCheckConstraint("CK_BlockchainTransactions_Type_BuyerPayment",
                "(Type <> 'BUYER_PAYMENT') " +
                "OR (PaymentAddressId IS NOT NULL AND ActualTokenAddress IS NULL)");

            // WRONG_TOKEN_INCOMING: ActualTokenAddress NOT NULL, PaymentAddressId NOT NULL
            t.HasCheckConstraint("CK_BlockchainTransactions_Type_WrongTokenIncoming",
                "(Type <> 'WRONG_TOKEN_INCOMING') " +
                "OR (ActualTokenAddress IS NOT NULL AND PaymentAddressId IS NOT NULL)");

            // WRONG_TOKEN_REFUND: ActualTokenAddress NOT NULL, PaymentAddressId NULL
            t.HasCheckConstraint("CK_BlockchainTransactions_Type_WrongTokenRefund",
                "(Type <> 'WRONG_TOKEN_REFUND') " +
                "OR (ActualTokenAddress IS NOT NULL AND PaymentAddressId IS NULL)");

            // SPAM_TOKEN_INCOMING: ActualTokenAddress NOT NULL, PaymentAddressId NOT NULL
            t.HasCheckConstraint("CK_BlockchainTransactions_Type_SpamTokenIncoming",
                "(Type <> 'SPAM_TOKEN_INCOMING') " +
                "OR (ActualTokenAddress IS NOT NULL AND PaymentAddressId IS NOT NULL)");

            // Outbound transfers: PaymentAddressId NULL, ActualTokenAddress NULL
            // (SELLER_PAYOUT, BUYER_REFUND, EXCESS_REFUND, LATE_PAYMENT_REFUND, INCORRECT_AMOUNT_REFUND)
            t.HasCheckConstraint("CK_BlockchainTransactions_Type_Outbound",
                "(Type NOT IN ('SELLER_PAYOUT', 'BUYER_REFUND', 'EXCESS_REFUND', 'LATE_PAYMENT_REFUND', 'INCORRECT_AMOUNT_REFUND')) " +
                "OR (PaymentAddressId IS NULL AND ActualTokenAddress IS NULL)");

            // SWEEP (WP3): deposit → hot wallet, so it is anchored to the source
            // deposit PaymentAddress (PaymentAddressId NOT NULL) and is a
            // canonical-stablecoin transfer (ActualTokenAddress NULL). SWEEP is
            // intentionally NOT in the Outbound constraint above — that mandates
            // PaymentAddressId IS NULL, the opposite invariant. This positive
            // invariant mirrors BUYER_PAYMENT's shape and keeps the reconciliation
            // deposit-outflow attribution (which keys on PaymentAddressId) exact.
            t.HasCheckConstraint("CK_BlockchainTransactions_Type_Sweep",
                "(Type <> 'SWEEP') " +
                "OR (PaymentAddressId IS NOT NULL AND ActualTokenAddress IS NULL)");

            // ===== Status-dependent CHECK constraints (06 §3.8) =====

            // CONFIRMED: ConfirmationCount >= 20, ConfirmedAt NOT NULL
            t.HasCheckConstraint("CK_BlockchainTransactions_Status_Confirmed",
                "(Status <> 'CONFIRMED') " +
                "OR (ConfirmationCount >= 20 AND ConfirmedAt IS NOT NULL)");

            // DETECTED: ConfirmationCount = 0
            t.HasCheckConstraint("CK_BlockchainTransactions_Status_Detected",
                "(Status <> 'DETECTED') " +
                "OR (ConfirmationCount = 0)");

            // FAILED: ConfirmedAt NULL
            t.HasCheckConstraint("CK_BlockchainTransactions_Status_Failed",
                "(Status <> 'FAILED') " +
                "OR (ConfirmedAt IS NULL)");

            // PENDING: ConfirmationCount < 20
            t.HasCheckConstraint("CK_BlockchainTransactions_Status_Pending",
                "(Status <> 'PENDING') " +
                "OR (ConfirmationCount < 20)");

            // EventIndex (WP10 — 08 §3.4): NULL for outbound rows, otherwise a
            // non-negative on-chain log index for inbound monitored transfers.
            t.HasCheckConstraint("CK_BlockchainTransactions_EventIndex",
                "([EventIndex] IS NULL) OR ([EventIndex] >= 0)");
        });

        // --- Primary key ---
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).ValueGeneratedNever();

        // --- Fields ---
        builder.Property(b => b.TransactionId)
            .IsRequired();

        builder.Property(b => b.Type)
            .IsRequired();

        builder.Property(b => b.TxHash)
            .HasMaxLength(100);

        // WP10 (08 §3.4) — on-chain event index; nullable for outbound rows.
        builder.Property(b => b.EventIndex);

        builder.Property(b => b.FromAddress)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(b => b.ToAddress)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(b => b.Amount)
            .IsRequired()
            .HasPrecision(18, 6);

        builder.Property(b => b.Token)
            .IsRequired();

        builder.Property(b => b.ActualTokenAddress)
            .HasMaxLength(50);

        builder.Property(b => b.GasFee)
            .HasPrecision(18, 6);

        // Realized on-chain cost, filled at confirmation. Nullable and
        // deliberately unconstrained: 0 is a real, common answer (delegated
        // energy, or a contract that pays for its callers) and must not be
        // confused with "not measured", which is NULL.
        builder.Property(b => b.RealizedFeeSun);
        builder.Property(b => b.EnergyUsageTotal);
        builder.Property(b => b.OriginEnergyUsage);

        builder.Property(b => b.Status)
            .IsRequired();

        builder.Property(b => b.ConfirmationCount)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(b => b.RetryCount)
            .IsRequired()
            .HasDefaultValue(0);

        // NextAttemptAt — T73 retry scheduling. NULL = eligible immediately;
        // dispatcher sets it to `now + retryInterval` after a transient failure.
        builder.Property(b => b.NextAttemptAt);

        builder.Property(b => b.ErrorMessage)
            .HasMaxLength(500);

        builder.Property(b => b.CreatedAt)
            .IsRequired();

        // --- Relationships (06 §4.1) ---
        // BlockchainTransaction → Transaction (N:1)
        builder.HasOne(b => b.Transaction)
            .WithMany(t => t.BlockchainTransactions)
            .HasForeignKey(b => b.TransactionId);

        // BlockchainTransaction → PaymentAddress (N:1, optional)
        // Configured in PaymentAddressConfiguration via HasMany

        // --- Unique constraints (06 §5.1) ---
        // WP10 (08 §3.4) — per-event uniqueness on (TxHash, EventIndex) so a
        // single transaction carrying several Transfer events to the deposit
        // address is recorded once per event rather than collapsed to one row.
        // Filtered on [TxHash] IS NOT NULL (NULL before broadcast). Outbound
        // rows have EventIndex NULL but each broadcast yields a distinct TxHash,
        // so (distinctTxHash, NULL) pairs never collide; inbound rows carry a
        // real >= 0 index. Replaces the former TxHash-only UQ index.
        builder.HasIndex(b => new { b.TxHash, b.EventIndex })
            .IsUnique()
            .HasFilter("[TxHash] IS NOT NULL")
            .HasDatabaseName("UQ_BlockchainTransactions_TxHash_EventIndex");

        // WP1 F1 (S2 money-safety) — at most one SELLER_PAYOUT row per
        // transaction. Database-level backstop behind SellerPayoutQueueJob's
        // [DisableConcurrentExecution] lock: two overlapping producer ticks
        // cannot queue duplicate payouts → no double-pay. Refund / other
        // outbound types are intentionally unconstrained (a transaction may
        // legitimately accumulate several refund rows). Named overload so this
        // coexists with the non-unique IX_BlockchainTransactions_TransactionId.
        builder.HasIndex(b => b.TransactionId, "UQ_BlockchainTransactions_SellerPayout_TransactionId")
            .IsUnique()
            .HasFilter("[Type] = 'SELLER_PAYOUT'");

        // WP2 F1-parity (S2 money-safety) — at most one BUYER_REFUND row per
        // transaction. Database-level backstop behind PaymentRefundToBuyerConsumer's
        // AnyAsync + catch(DbUpdateException) guards: an at-least-once outbox
        // redelivery that slips past the existence check cannot queue a duplicate
        // refund → no double-refund. Legitimate because all three publish sites
        // (delivery timeout, admin-cancel, emergency-hold-release-cancel) are
        // terminal transitions and a transaction is cancelled exactly once. The
        // OTHER outbound refund types stay unconstrained (a transaction may
        // legitimately accumulate several refund rows of different types). Named
        // overload so it coexists with IX_BlockchainTransactions_TransactionId.
        builder.HasIndex(b => b.TransactionId, "UQ_BlockchainTransactions_BuyerRefund_TransactionId")
            .IsUnique()
            .HasFilter("[Type] = 'BUYER_REFUND'");

        // WP3 (S2 money-safety) — at most one SWEEP row per transaction.
        // Database-level backstop behind SweepQueueJob's [DisableConcurrentExecution]
        // lock + AnyAsync guard: two overlapping producer ticks cannot queue
        // duplicate sweeps → no double-sweep of the same deposit (which would
        // double-credit the hot wallet in reconciliation). A transaction sweeps
        // exactly one deposit (its 1:1 PaymentAddress). Named overload so it
        // coexists with the non-unique IX_BlockchainTransactions_TransactionId.
        builder.HasIndex(b => b.TransactionId, "UQ_BlockchainTransactions_Sweep_TransactionId")
            .IsUnique()
            .HasFilter("[Type] = 'SWEEP'");

        // --- Performance indexes (06 §5.2) ---
        builder.HasIndex(b => b.TransactionId)
            .HasDatabaseName("IX_BlockchainTransactions_TransactionId");

        builder.HasIndex(b => b.Status)
            .HasFilter("[Status] = 'PENDING'")
            .HasDatabaseName("IX_BlockchainTransactions_Status_Pending");

        // T73 dispatcher hot path — composite covers the "Status=PENDING AND
        // (NextAttemptAt IS NULL OR NextAttemptAt <= @now)" lookup that runs
        // every minute. ORDER BY CreatedAt keeps the dispatcher fair (oldest
        // refund first) on backlogs.
        builder.HasIndex(b => new { b.Status, b.NextAttemptAt, b.CreatedAt })
            .HasFilter("[Status] = 'PENDING'")
            .HasDatabaseName("IX_BlockchainTransactions_DispatchScan");

        builder.HasIndex(b => b.FromAddress)
            .HasDatabaseName("IX_BlockchainTransactions_FromAddress");
    }
}
