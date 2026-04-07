using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Skinora.Shared.Enums;

namespace Skinora.Shared.Persistence.Outbox.Configurations;

/// <summary>
/// EF Core mapping for <see cref="OutboxMessage"/> (06 §3.18, §5.2).
/// </summary>
public class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("OutboxMessages", t =>
        {
            // Status-dependent invariants (06 §3.18). DEFERRED is allowed
            // without an additional invariant — its semantics are tightened
            // by the consumer that introduces it.
            //
            // Enum literal values:
            //   PENDING = 0, PROCESSED = 1, DEFERRED = 2, FAILED = 3.
            t.HasCheckConstraint(
                "CK_OutboxMessages_Status_Invariants",
                "(\"Status\" = 0 AND \"ProcessedAt\" IS NULL) OR " +
                "(\"Status\" = 1 AND \"ProcessedAt\" IS NOT NULL) OR " +
                "(\"Status\" = 2) OR " +
                "(\"Status\" = 3 AND \"ProcessedAt\" IS NULL AND \"ErrorMessage\" IS NOT NULL)");
        });

        builder.HasKey(x => x.Id);

        // Id is the EventId — supplied by the caller, never DB-generated
        // (09 §9.3 "Tek ID, tek otorite").
        builder.Property(x => x.Id)
            .ValueGeneratedNever();

        builder.Property(x => x.EventType)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.Payload)
            .IsRequired();

        builder.Property(x => x.Status)
            .IsRequired()
            .HasConversion<int>()
            .HasDefaultValue(OutboxMessageStatus.PENDING);

        builder.Property(x => x.RetryCount)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(x => x.ErrorMessage)
            .HasMaxLength(2000);

        builder.Property(x => x.CreatedAt)
            .IsRequired();

        builder.Property(x => x.ProcessedAt);

        // Filtered index on (Status, CreatedAt) WHERE Status IN (PENDING, FAILED)
        // — feeds the dispatcher's "fetch unprocessed and retryable" query
        // (06 §5.2). Status uses int literals (0 = PENDING, 3 = FAILED) so the
        // filter is portable across SQL Server and SQLite.
        builder.HasIndex(x => new { x.Status, x.CreatedAt })
            .HasFilter("\"Status\" IN (0, 3)")
            .HasDatabaseName("IX_OutboxMessages_Status_CreatedAt_Pending");
    }
}
