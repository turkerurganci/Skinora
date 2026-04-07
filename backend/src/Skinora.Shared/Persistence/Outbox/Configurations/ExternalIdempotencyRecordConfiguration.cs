using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Skinora.Shared.Persistence.Outbox.Configurations;

/// <summary>
/// EF Core mapping for <see cref="ExternalIdempotencyRecord"/> (06 §3.21, §5.2).
/// </summary>
public class ExternalIdempotencyRecordConfiguration
    : IEntityTypeConfiguration<ExternalIdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<ExternalIdempotencyRecord> builder)
    {
        builder.ToTable("ExternalIdempotencyRecords", t =>
        {
            // Status-dependent invariants (06 §3.21). Status is persisted as
            // its lower-case enum name so CHECK literals are stable across
            // SQL Server and SQLite.
            t.HasCheckConstraint(
                "CK_ExternalIdempotencyRecords_Status_Invariants",
                "(\"Status\" = 'in_progress' AND \"CompletedAt\" IS NULL " +
                    "AND \"ResultPayload\" IS NULL AND \"LeaseExpiresAt\" IS NOT NULL) OR " +
                "(\"Status\" = 'completed' AND \"CompletedAt\" IS NOT NULL) OR " +
                "(\"Status\" = 'failed' AND \"CompletedAt\" IS NULL)");
        });

        builder.HasKey(x => x.Id);

        // long IDENTITY (06 §3.21).
        builder.Property(x => x.Id)
            .ValueGeneratedOnAdd();

        builder.Property(x => x.IdempotencyKey)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.ServiceName)
            .IsRequired()
            .HasMaxLength(100);

        // Persist as lower-case string so the CHECK constraint can use the
        // literal values 'in_progress' / 'completed' / 'failed' directly
        // (06 §3.21).
        builder.Property(x => x.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(x => x.ResultPayload);

        builder.Property(x => x.LeaseExpiresAt);

        builder.Property(x => x.CreatedAt)
            .IsRequired();

        builder.Property(x => x.CompletedAt);

        // 06 §3.21: "UNIQUE(ServiceName, IdempotencyKey) — servis bazlı scope."
        builder.HasIndex(x => new { x.ServiceName, x.IdempotencyKey })
            .IsUnique()
            .HasDatabaseName("UX_ExternalIdempotencyRecords_ServiceName_IdempotencyKey");
    }
}
