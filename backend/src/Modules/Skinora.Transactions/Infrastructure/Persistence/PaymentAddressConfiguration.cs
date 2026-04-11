using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Skinora.Transactions.Domain.Entities;

namespace Skinora.Transactions.Infrastructure.Persistence;

/// <summary>
/// EF Core configuration for PaymentAddress entity — 06 §3.7, §4.1, §5.1, §5.2.
/// </summary>
public class PaymentAddressConfiguration : IEntityTypeConfiguration<PaymentAddress>
{
    public void Configure(EntityTypeBuilder<PaymentAddress> builder)
    {
        builder.ToTable("PaymentAddresses");

        // --- Primary key ---
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();

        // --- Fields ---
        builder.Property(p => p.TransactionId)
            .IsRequired();

        builder.Property(p => p.Address)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(p => p.HdWalletIndex)
            .IsRequired();

        builder.Property(p => p.ExpectedAmount)
            .IsRequired()
            .HasPrecision(18, 6);

        builder.Property(p => p.ExpectedToken)
            .IsRequired();

        builder.Property(p => p.MonitoringStatus)
            .IsRequired();

        // --- ISoftDeletable ---
        builder.Property(p => p.IsDeleted)
            .IsRequired()
            .HasDefaultValue(false);

        // --- Relationships (06 §4.1) ---
        // PaymentAddress → Transaction (1:1)
        builder.HasOne(p => p.Transaction)
            .WithOne(t => t.PaymentAddress)
            .HasForeignKey<PaymentAddress>(p => p.TransactionId);

        // PaymentAddress → BlockchainTransactions (1:N)
        builder.HasMany(p => p.BlockchainTransactions)
            .WithOne(b => b.PaymentAddress)
            .HasForeignKey(b => b.PaymentAddressId);

        // --- Unique constraints (06 §5.1) ---
        builder.HasIndex(p => p.TransactionId)
            .IsUnique()
            .HasDatabaseName("UQ_PaymentAddresses_TransactionId");

        builder.HasIndex(p => p.Address)
            .IsUnique()
            .HasDatabaseName("UQ_PaymentAddresses_Address");

        builder.HasIndex(p => p.HdWalletIndex)
            .IsUnique()
            .HasDatabaseName("UQ_PaymentAddresses_HdWalletIndex");

        // --- Performance indexes (06 §5.2) ---
        builder.HasIndex(p => p.MonitoringStatus)
            .HasFilter("[MonitoringStatus] IN ('ACTIVE','POST_CANCEL_24H','POST_CANCEL_7D','POST_CANCEL_30D')")
            .HasDatabaseName("IX_PaymentAddresses_MonitoringStatus_Active");
    }
}
