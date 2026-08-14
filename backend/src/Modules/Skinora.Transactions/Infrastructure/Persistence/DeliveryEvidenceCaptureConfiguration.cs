using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Skinora.Transactions.Domain.Entities;

namespace Skinora.Transactions.Infrastructure.Persistence;

/// <summary>
/// EF Core configuration for <see cref="DeliveryEvidenceCapture"/> (T125 —
/// 06 §3.5a, §4.1, §4.2).
/// </summary>
/// <remarks>
/// Append-only semantics are enforced centrally by
/// <c>AppDbContext.EnforceAppendOnly()</c> (06 §4.2) — UPDATE and DELETE on a
/// written row are rejected before they reach the database.
/// </remarks>
public class DeliveryEvidenceCaptureConfiguration
    : IEntityTypeConfiguration<DeliveryEvidenceCapture>
{
    public void Configure(EntityTypeBuilder<DeliveryEvidenceCapture> builder)
    {
        builder.ToTable("DeliveryEvidenceCaptures");

        // --- Primary key (long, IDENTITY) ---
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedOnAdd();

        builder.Property(c => c.TransactionId)
            .IsRequired();

        builder.Property(c => c.ObservedAt)
            .IsRequired();

        builder.Property(c => c.Verdict)
            .IsRequired()
            .HasMaxLength(40);

        // Stored as int for the same reason as Transaction.DeliveryEvidence:
        // it is a [Flags] enum, and the global EnumToStringConverter would
        // persist combinations as comma-joined names.
        builder.Property(c => c.Evidence)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(c => c.AutoReleaseGated)
            .IsRequired();

        // Payload: string with no explicit length → nvarchar(max) on SQL Server,
        // TEXT on SQLite. An explicit HasColumnType would force SQL Server
        // syntax and break the SQLite integration tests (same reasoning as
        // TransactionHistory.AdditionalData).
        builder.Property(c => c.Payload)
            .IsRequired();

        builder.Property(c => c.CreatedAt)
            .IsRequired();

        // --- Relationships (06 §4.1 — NO ACTION cascade) ---
        builder.HasOne<Transaction>()
            .WithMany()
            .HasForeignKey(c => c.TransactionId)
            .OnDelete(DeleteBehavior.NoAction);

        // --- Performance indexes ---
        // The launch-gate review reads by transaction, and the operator asking
        // "how many gated observations are waiting" filters on the flag.
        builder.HasIndex(c => c.TransactionId)
            .HasDatabaseName("IX_DeliveryEvidenceCaptures_TransactionId");

        builder.HasIndex(c => c.AutoReleaseGated)
            .HasDatabaseName("IX_DeliveryEvidenceCaptures_AutoReleaseGated");
    }
}
