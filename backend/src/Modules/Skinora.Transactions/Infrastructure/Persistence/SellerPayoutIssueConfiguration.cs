using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Skinora.Transactions.Domain.Entities;
using Skinora.Users.Domain.Entities;

namespace Skinora.Transactions.Infrastructure.Persistence;

/// <summary>
/// EF Core configuration for <see cref="SellerPayoutIssue"/>.
/// 06 §3.8a, §4.1, §4.2, §5.1, §5.2.
/// </summary>
public class SellerPayoutIssueConfiguration
    : IEntityTypeConfiguration<SellerPayoutIssue>
{
    public void Configure(EntityTypeBuilder<SellerPayoutIssue> builder)
    {
        builder.ToTable("SellerPayoutIssues", t =>
        {
            // 06 §3.8a state-dependent invariants:
            //   ESCALATED        → EscalatedToAdminId NOT NULL
            //   RESOLVED         → ResolvedAt         NOT NULL
            //   RETRY_SCHEDULED  → RetryCount         > 0
            t.HasCheckConstraint(
                "CK_SellerPayoutIssues_Status_Invariants",
                "([VerificationStatus] = 'ESCALATED' AND [EscalatedToAdminId] IS NOT NULL) OR " +
                "([VerificationStatus] = 'RESOLVED' AND [ResolvedAt] IS NOT NULL) OR " +
                "([VerificationStatus] = 'RETRY_SCHEDULED' AND [RetryCount] > 0) OR " +
                "[VerificationStatus] IN ('REPORTED', 'VERIFYING')");
        });

        // --- Properties ---
        builder.Property(i => i.TransactionId).IsRequired();
        builder.Property(i => i.SellerId).IsRequired();

        builder.Property(i => i.Detail)
            .IsRequired()
            .HasMaxLength(2000);

        builder.Property(i => i.PayoutTxHash)
            .HasMaxLength(100);

        builder.Property(i => i.VerificationStatus)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(i => i.RetryCount)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(i => i.AdminNote)
            .HasMaxLength(2000);

        // --- Relationships (06 §4.1, §4.2 — NO ACTION cascade) ---
        builder.HasOne<Transaction>()
            .WithMany()
            .HasForeignKey(i => i.TransactionId);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(i => i.SellerId);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(i => i.EscalatedToAdminId);

        // --- Unique constraints (06 §5.1) ---
        // 06 §3.8a: "Tek aktif issue kuralı — UNIQUE(TransactionId) WHERE
        // VerificationStatus != RESOLVED." Yeni issue ancak önceki RESOLVED
        // olduktan sonra açılabilir.
        builder.HasIndex(i => i.TransactionId)
            .IsUnique()
            .HasFilter("[VerificationStatus] <> 'RESOLVED'")
            .HasDatabaseName("UQ_SellerPayoutIssues_TransactionId_Active");

        // --- Performance indexes (06 §5.2) ---
        builder.HasIndex(i => i.TransactionId)
            .HasDatabaseName("IX_SellerPayoutIssues_TransactionId");

        builder.HasIndex(i => i.SellerId)
            .HasDatabaseName("IX_SellerPayoutIssues_SellerId");

        builder.HasIndex(i => i.VerificationStatus)
            .HasFilter("[VerificationStatus] <> 'RESOLVED'")
            .HasDatabaseName("IX_SellerPayoutIssues_VerificationStatus_Active");
    }
}
