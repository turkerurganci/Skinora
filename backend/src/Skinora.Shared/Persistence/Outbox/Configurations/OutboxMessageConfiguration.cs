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
            // T17: Status stored as string (global enum→string convention).
            t.HasCheckConstraint(
                "CK_OutboxMessages_Status_Invariants",
                "([Status] = 'PENDING' AND [ProcessedAt] IS NULL) OR " +
                "([Status] = 'PROCESSED' AND [ProcessedAt] IS NOT NULL) OR " +
                "([Status] = 'DEFERRED') OR " +
                "([Status] = 'FAILED' AND [ProcessedAt] IS NULL AND [ErrorMessage] IS NOT NULL)");
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
            .HasMaxLength(20)
            .HasDefaultValue(OutboxMessageStatus.PENDING);

        builder.Property(x => x.RetryCount)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(x => x.ErrorMessage)
            .HasMaxLength(2000);

        builder.Property(x => x.CreatedAt)
            .IsRequired();

        builder.Property(x => x.ProcessedAt);

        // Publish ordinal within the producing unit of work. Defaults to 0 so
        // rows written before this column existed keep sorting purely by
        // CreatedAt — their relative order was never recorded and cannot be
        // reconstructed, and by the time this ships they are long processed.
        builder.Property(x => x.Sequence)
            .IsRequired()
            .HasDefaultValue(0);

        // Filtered index on (Status, CreatedAt, Sequence) WHERE Status IN
        // (PENDING, FAILED) — feeds the dispatcher's "fetch unprocessed and
        // retryable" query (06 §5.2). Sequence is part of the key rather than
        // an include so the dispatcher's ORDER BY is served by the index and
        // does not add a sort. T17: Status stored as string.
        builder.HasIndex(x => new { x.Status, x.CreatedAt, x.Sequence })
            .HasFilter("[Status] IN ('PENDING', 'FAILED')")
            .HasDatabaseName("IX_OutboxMessages_Status_CreatedAt_Pending");
    }
}
