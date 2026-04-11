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

        builder.Property(b => b.Status)
            .IsRequired();

        builder.Property(b => b.ConfirmationCount)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(b => b.RetryCount)
            .IsRequired()
            .HasDefaultValue(0);

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
        // TxHash filtered unique — NULL allowed (before broadcast)
        builder.HasIndex(b => b.TxHash)
            .IsUnique()
            .HasFilter("[TxHash] IS NOT NULL")
            .HasDatabaseName("UQ_BlockchainTransactions_TxHash");

        // --- Performance indexes (06 §5.2) ---
        builder.HasIndex(b => b.TransactionId)
            .HasDatabaseName("IX_BlockchainTransactions_TransactionId");

        builder.HasIndex(b => b.Status)
            .HasFilter("[Status] = 'PENDING'")
            .HasDatabaseName("IX_BlockchainTransactions_Status_Pending");

        builder.HasIndex(b => b.FromAddress)
            .HasDatabaseName("IX_BlockchainTransactions_FromAddress");
    }
}
