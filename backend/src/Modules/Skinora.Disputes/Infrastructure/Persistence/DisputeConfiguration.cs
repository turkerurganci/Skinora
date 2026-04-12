using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Skinora.Disputes.Domain.Entities;
using Skinora.Transactions.Domain.Entities;
using Skinora.Users.Domain.Entities;

namespace Skinora.Disputes.Infrastructure.Persistence;

/// <summary>
/// EF Core configuration for <see cref="Dispute"/>.
/// 06 §3.11, §4.1, §4.2, §5.1, §5.2.
/// </summary>
public class DisputeConfiguration : IEntityTypeConfiguration<Dispute>
{
    public void Configure(EntityTypeBuilder<Dispute> builder)
    {
        builder.ToTable("Disputes");

        // --- Properties ---
        builder.Property(d => d.TransactionId)
            .IsRequired();

        builder.Property(d => d.OpenedByUserId)
            .IsRequired();

        builder.Property(d => d.Type)
            .IsRequired();

        builder.Property(d => d.Status)
            .IsRequired();

        builder.Property(d => d.UserDescription)
            .HasMaxLength(2000);

        builder.Property(d => d.AdminNote)
            .HasMaxLength(2000);

        // --- Soft delete query filter ---
        builder.HasQueryFilter(d => !d.IsDeleted);

        // --- Relationships (06 §4.1, §4.2 — NO ACTION cascade) ---
        builder.HasOne<Transaction>()
            .WithMany()
            .HasForeignKey(d => d.TransactionId);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(d => d.OpenedByUserId);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(d => d.AdminId);

        // --- Unique constraints (06 §5.1) ---
        // Unfiltered unique — same dispute type cannot be reopened for a transaction,
        // even after closure or soft delete (02 §10.2).
        builder.HasIndex(d => new { d.TransactionId, d.Type })
            .IsUnique()
            .HasDatabaseName("UQ_Disputes_TransactionId_Type");

        // --- Performance indexes (06 §5.2) ---
        builder.HasIndex(d => d.TransactionId)
            .HasDatabaseName("IX_Disputes_TransactionId");

        builder.HasIndex(d => d.Status)
            .HasFilter("[Status] IN ('OPEN', 'ESCALATED')")
            .HasDatabaseName("IX_Disputes_Status_Active");

        // --- State-dependent CHECK constraints (06 §3.11) ---
        // CLOSED → ResolvedAt NOT NULL
        builder.ToTable(t => t.HasCheckConstraint("CK_Disputes_Closed_ResolvedAt",
            "(Status <> 'CLOSED') OR (ResolvedAt IS NOT NULL)"));
    }
}
