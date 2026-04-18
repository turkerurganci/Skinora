using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Skinora.Admin.Domain.Entities;

namespace Skinora.Admin.Infrastructure.Persistence;

/// <summary>
/// EF Core configuration for <see cref="AdminRole"/>.
/// 06 §3.14, §5.1.
/// </summary>
public class AdminRoleConfiguration : IEntityTypeConfiguration<AdminRole>
{
    public void Configure(EntityTypeBuilder<AdminRole> builder)
    {
        builder.ToTable("AdminRoles");

        // --- Properties ---
        builder.Property(r => r.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(r => r.Description)
            .HasMaxLength(500);

        builder.Property(r => r.IsSuperAdmin)
            .IsRequired()
            .HasDefaultValue(false);

        // --- Soft delete query filter ---
        builder.HasQueryFilter(r => !r.IsDeleted);

        // --- Unique constraints (06 §5.1) ---
        builder.HasIndex(r => r.Name)
            .IsUnique()
            .HasDatabaseName("UQ_AdminRoles_Name");
    }
}
