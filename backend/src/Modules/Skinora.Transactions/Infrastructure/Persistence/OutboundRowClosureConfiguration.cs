using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Skinora.Shared.Persistence.Converters;
using Skinora.Transactions.Domain.Entities;
using Skinora.Users.Domain.Entities;

namespace Skinora.Transactions.Infrastructure.Persistence;

/// <summary>
/// EF Core configuration for <see cref="OutboundRowClosure"/> — 06 §3.8b,
/// §4.1, §5.1 (T157). The database repeats the rules the money tool must not
/// break even if a code path forgets them: Ethereum only, a different admin
/// approves, one live closure per row, one live closure per hash.
/// </summary>
public class OutboundRowClosureConfiguration : IEntityTypeConfiguration<OutboundRowClosure>
{
    public void Configure(EntityTypeBuilder<OutboundRowClosure> builder)
    {
        builder.ToTable("OutboundRowClosures", t =>
        {
            // The tool exists for the Ethereum journal-loss case only.
            t.HasCheckConstraint("CK_OutboundRowClosures_Network_Ethereum",
                "[Network] = 'ETHEREUM'");

            // Two-person rule, at the schema: an APPROVED closure was decided
            // by someone, and that someone is not the proposer.
            t.HasCheckConstraint("CK_OutboundRowClosures_Approved_DifferentAdmin",
                "([Status] <> 'APPROVED') " +
                "OR ([DecidedByAdminId] IS NOT NULL AND [DecidedByAdminId] <> [ProposedByAdminId] AND [DecidedAt] IS NOT NULL)");

            // A decided closure carries who and when; a PROPOSED one neither.
            t.HasCheckConstraint("CK_OutboundRowClosures_Decision_Fields",
                "([Status] = 'PROPOSED' AND [DecidedByAdminId] IS NULL AND [DecidedAt] IS NULL) " +
                "OR ([Status] <> 'PROPOSED' AND [DecidedByAdminId] IS NOT NULL AND [DecidedAt] IS NOT NULL)");
        });

        builder.Property(c => c.BlockchainTransactionId).IsRequired();

        builder.Property(c => c.Network)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(c => c.TxHash)
            .IsRequired()
            .HasMaxLength(100)
            .HasConversion(CanonicalChainIdentifierConverter.Instance);

        builder.Property(c => c.Reason)
            .IsRequired()
            .HasMaxLength(2000);

        builder.Property(c => c.Status)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(c => c.ProposedByAdminId).IsRequired();

        builder.Property(c => c.DecisionNote)
            .HasMaxLength(2000);

        // --- Relationships (NO ACTION cascade, 06 §4.1) ---
        builder.HasOne(c => c.BlockchainTransaction)
            .WithMany()
            .HasForeignKey(c => c.BlockchainTransactionId);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(c => c.ProposedByAdminId);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(c => c.DecidedByAdminId);

        // --- Unique constraints (06 §5.1) ---
        // One live closure (PROPOSED or APPROVED) per row: a second hash for a
        // row that is already closed — or waiting — is refused by the database.
        builder.HasIndex(c => c.BlockchainTransactionId)
            .IsUnique()
            .HasFilter("[Status] <> 'REJECTED'")
            .HasDatabaseName("UQ_OutboundRowClosures_Row_Live");

        // One live closure per hash: the same on-chain transaction can close
        // only one row (a hash bound to two rows would pay one of them twice
        // on paper and leave the other open).
        builder.HasIndex(c => new { c.Network, c.TxHash })
            .IsUnique()
            .HasFilter("[Status] <> 'REJECTED'")
            .HasDatabaseName("UQ_OutboundRowClosures_Network_TxHash_Live");

        builder.HasIndex(c => c.Status)
            .HasDatabaseName("IX_OutboundRowClosures_Status");
    }
}
