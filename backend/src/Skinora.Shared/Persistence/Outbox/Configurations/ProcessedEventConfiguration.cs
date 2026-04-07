using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Skinora.Shared.Persistence.Outbox.Configurations;

/// <summary>
/// EF Core mapping for <see cref="ProcessedEvent"/> (06 §3.19, §5.2).
/// </summary>
public class ProcessedEventConfiguration : IEntityTypeConfiguration<ProcessedEvent>
{
    public void Configure(EntityTypeBuilder<ProcessedEvent> builder)
    {
        builder.ToTable("ProcessedEvents");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .ValueGeneratedNever();

        builder.Property(x => x.EventId)
            .IsRequired();

        builder.Property(x => x.ConsumerName)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.ProcessedAt)
            .IsRequired();

        // 06 §3.19: "EventId + ConsumerName çifti unique olmalı."
        // Enforced at the DB level so duplicate insertion attempts fail
        // even if the application-level check races (last line of defence).
        builder.HasIndex(x => new { x.EventId, x.ConsumerName })
            .IsUnique()
            .HasDatabaseName("UX_ProcessedEvents_EventId_ConsumerName");
    }
}
