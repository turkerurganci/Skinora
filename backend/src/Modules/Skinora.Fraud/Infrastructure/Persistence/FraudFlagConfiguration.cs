using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Skinora.Fraud.Domain.Entities;
using Skinora.Transactions.Domain.Entities;
using Skinora.Users.Domain.Entities;

namespace Skinora.Fraud.Infrastructure.Persistence;

/// <summary>
/// EF Core configuration for <see cref="FraudFlag"/>.
/// 06 §3.12, §4.1, §4.2, §5.2.
/// </summary>
public class FraudFlagConfiguration : IEntityTypeConfiguration<FraudFlag>
{
    public void Configure(EntityTypeBuilder<FraudFlag> builder)
    {
        builder.ToTable("FraudFlags");

        // --- Properties ---
        builder.Property(f => f.UserId)
            .IsRequired();

        builder.Property(f => f.Scope)
            .IsRequired();

        builder.Property(f => f.Type)
            .IsRequired();

        builder.Property(f => f.Details)
            .IsRequired();

        builder.Property(f => f.Status)
            .IsRequired();

        builder.Property(f => f.AdminNote)
            .HasMaxLength(2000);

        // --- Soft delete query filter ---
        builder.HasQueryFilter(f => !f.IsDeleted);

        // --- Relationships (06 §4.1, §4.2 — NO ACTION cascade) ---
        builder.HasOne<Transaction>()
            .WithMany()
            .HasForeignKey(f => f.TransactionId);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(f => f.UserId);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(f => f.ReviewedByAdminId);

        // --- Performance indexes (06 §5.2) ---
        builder.HasIndex(f => f.Status)
            .HasFilter("[Status] = 'PENDING'")
            .HasDatabaseName("IX_FraudFlags_Status_Pending");

        builder.HasIndex(f => f.TransactionId)
            .HasDatabaseName("IX_FraudFlags_TransactionId");

        builder.HasIndex(f => f.UserId)
            .HasDatabaseName("IX_FraudFlags_UserId");

        // --- Scope-based CHECK constraints (06 §3.12) ---
        // ACCOUNT_LEVEL → TransactionId NULL
        builder.ToTable(t => t.HasCheckConstraint("CK_FraudFlags_AccountLevel_TransactionId",
            "(Scope <> 'ACCOUNT_LEVEL') OR (TransactionId IS NULL)"));

        // TRANSACTION_PRE_CREATE → TransactionId NOT NULL
        builder.ToTable(t => t.HasCheckConstraint("CK_FraudFlags_PreCreate_TransactionId",
            "(Scope <> 'TRANSACTION_PRE_CREATE') OR (TransactionId IS NOT NULL)"));

        // --- State-dependent CHECK constraints (06 §3.12) ---
        // APPROVED → ReviewedAt NOT NULL, ReviewedByAdminId NOT NULL
        builder.ToTable(t => t.HasCheckConstraint("CK_FraudFlags_Approved_ReviewedAt",
            "(Status <> 'APPROVED') OR (ReviewedAt IS NOT NULL AND ReviewedByAdminId IS NOT NULL)"));

        // REJECTED → ReviewedAt NOT NULL, ReviewedByAdminId NOT NULL
        builder.ToTable(t => t.HasCheckConstraint("CK_FraudFlags_Rejected_ReviewedAt",
            "(Status <> 'REJECTED') OR (ReviewedAt IS NOT NULL AND ReviewedByAdminId IS NOT NULL)"));
    }
}
