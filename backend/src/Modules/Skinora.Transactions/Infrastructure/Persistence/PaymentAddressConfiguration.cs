using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Skinora.Shared.Persistence.Converters;
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

        // 06 §1.4 (T145) — network of the deposit address; NOT NULL, existing
        // rows were backfilled to TRON by the migration.
        builder.Property(p => p.Network)
            .IsRequired()
            .HasMaxLength(20);

        // Stored in the canonical form (06 §1.4): EVM lowercase, TRON as-is.
        builder.Property(p => p.Address)
            .IsRequired()
            .HasMaxLength(50)
            .HasConversion(CanonicalChainIdentifierConverter.Instance);

        builder.Property(p => p.HdWalletIndex)
            .IsRequired();

        builder.Property(p => p.ExpectedAmount)
            .IsRequired()
            .HasPrecision(18, 6);

        builder.Property(p => p.ExpectedToken)
            .IsRequired();

        builder.Property(p => p.MonitoringStatus)
            .IsRequired();

        // T158 (h) — the block the deposit is watched from (the network
        // service's finalized head when it derived the address). NULL for a
        // network that does not report one (TRON) and for rows that predate
        // T158.
        builder.Property(p => p.MonitorFromBlock);
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_PaymentAddresses_MonitorFromBlock",
            "[MonitorFromBlock] IS NULL OR [MonitorFromBlock] >= 0"));

        // T160c — when the refund-fee collector looks at this deposit again
        // (NULL = next run). Plain nullable timestamp, no constraint.
        builder.Property(p => p.RefundFeeSweepNextCheckAt);

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

        // 06 §1.4 (T145) — both keys are per network: every network has its
        // own seed (11 §F8 K7), so HD index 0 exists once per network and the
        // same address string can only collide within one network.
        builder.HasIndex(p => new { p.Network, p.Address })
            .IsUnique()
            .HasDatabaseName("UQ_PaymentAddresses_Network_Address");

        builder.HasIndex(p => new { p.Network, p.HdWalletIndex })
            .IsUnique()
            .HasDatabaseName("UQ_PaymentAddresses_Network_HdWalletIndex");

        // --- Performance indexes (06 §5.2) ---
        builder.HasIndex(p => p.MonitoringStatus)
            .HasFilter("[MonitoringStatus] IN ('ACTIVE','POST_CANCEL_24H','POST_CANCEL_7D','POST_CANCEL_30D')")
            .HasDatabaseName("IX_PaymentAddresses_MonitoringStatus_Active");
    }
}
