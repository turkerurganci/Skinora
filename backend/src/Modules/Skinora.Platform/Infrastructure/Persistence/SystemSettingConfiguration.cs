using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Skinora.Platform.Domain.Entities;
using Skinora.Users.Domain.Entities;

namespace Skinora.Platform.Infrastructure.Persistence;

/// <summary>
/// EF Core configuration for <see cref="SystemSetting"/>.
/// 06 §3.17, §4.1, §5.1, §5.2.
/// </summary>
public class SystemSettingConfiguration : IEntityTypeConfiguration<SystemSetting>
{
    public void Configure(EntityTypeBuilder<SystemSetting> builder)
    {
        builder.ToTable("SystemSettings", t =>
        {
            // 06 §3.17: "DataType IN ('int', 'decimal', 'bool', 'string')"
            t.HasCheckConstraint(
                "CK_SystemSettings_DataType_Valid",
                "[DataType] IN ('int', 'decimal', 'bool', 'string')");
        });

        // --- Properties ---
        builder.Property(s => s.Key)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(s => s.Value)
            .HasMaxLength(500);

        builder.Property(s => s.IsConfigured)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(s => s.DataType)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(s => s.Category)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(s => s.Description)
            .HasMaxLength(500);

        // --- Relationships (06 §4.1, §4.2 — NO ACTION cascade) ---
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(s => s.UpdatedByAdminId);

        // --- Unique constraints (06 §5.1) ---
        builder.HasIndex(s => s.Key)
            .IsUnique()
            .HasDatabaseName("UQ_SystemSettings_Key");

        // --- Performance indexes (06 §5.2) ---
        builder.HasIndex(s => s.Category)
            .HasDatabaseName("IX_SystemSettings_Category");

        // --- Seed: 28 platform parameters (06 §8.9, §3.17) ---
        // Parameters with a documented default are seeded IsConfigured = true;
        // the rest ship IsConfigured = false and must be hydrated before the
        // API completes startup (06 §8.9 fail-fast).
        builder.HasData(SystemSettingSeed.All);
    }
}
