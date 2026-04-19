using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Skinora.Platform.Domain.Entities;
using Skinora.Shared.Domain.Seed;

namespace Skinora.Platform.Infrastructure.Persistence;

/// <summary>
/// EF Core configuration for <see cref="SystemHeartbeat"/>.
/// 06 §3.23.
/// </summary>
public class SystemHeartbeatConfiguration : IEntityTypeConfiguration<SystemHeartbeat>
{
    public void Configure(EntityTypeBuilder<SystemHeartbeat> builder)
    {
        builder.ToTable("SystemHeartbeats", t =>
        {
            // 06 §3.23: Id pinned to 1 so the table holds at most one row.
            // A second row is rejected by the CHECK + PK combination.
            t.HasCheckConstraint(
                "CK_SystemHeartbeats_Singleton",
                "[Id] = 1");
        });

        builder.HasKey(h => h.Id);

        // Singleton: Id is supplied by the application (constant 1), never
        // DB-generated.
        builder.Property(h => h.Id)
            .ValueGeneratedNever();

        builder.Property(h => h.LastHeartbeat)
            .IsRequired();

        builder.Property(h => h.UpdatedAt)
            .IsRequired();

        // --- Seed: singleton row pinned to Id = 1 (06 §8.9) ---
        // Application code only UPDATEs this row; it never INSERTs a second
        // one (the CK_SystemHeartbeats_Singleton CHECK forbids it).
        builder.HasData(new SystemHeartbeat
        {
            Id = SeedConstants.SystemHeartbeatId,
            LastHeartbeat = SeedConstants.SeedAnchorUtc,
            UpdatedAt = SeedConstants.SeedAnchorUtc,
        });
    }
}
