using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Skinora.Shared.Persistence.Converters;
using Skinora.Transactions.Domain.Entities;

namespace Skinora.Transactions.Infrastructure.Persistence;

/// <summary>
/// EF Core configuration for <see cref="TransactionNetworkOption"/> — 06 §1.4,
/// §3.5a, §4.1, §5.1 (T147).
/// </summary>
public class TransactionNetworkOptionConfiguration : IEntityTypeConfiguration<TransactionNetworkOption>
{
    public void Configure(EntityTypeBuilder<TransactionNetworkOption> builder)
    {
        builder.ToTable("TransactionNetworkOptions");

        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).ValueGeneratedNever();

        builder.Property(o => o.TransactionId)
            .IsRequired();

        // Enum name, like every Skinora enum. No database DEFAULT — a row
        // without a network cannot be written.
        builder.Property(o => o.Network)
            .IsRequired()
            .HasMaxLength(20);

        // Same length and canonical form as Transaction.SellerPayoutAddress,
        // which is copied from this column at acceptance.
        builder.Property(o => o.SellerPayoutAddress)
            .IsRequired()
            .HasMaxLength(50)
            .HasConversion(CanonicalChainIdentifierConverter.Instance);

        builder.Property(o => o.CreatedAt)
            .IsRequired();

        builder.HasOne(o => o.Transaction)
            .WithMany(t => t.NetworkOptions)
            .HasForeignKey(o => o.TransactionId);

        // --- Unique (06 §5.1): one row per listing and network ---
        builder.HasIndex(o => new { o.TransactionId, o.Network })
            .IsUnique()
            .HasDatabaseName("UQ_TransactionNetworkOptions_TransactionId_Network");
    }
}
