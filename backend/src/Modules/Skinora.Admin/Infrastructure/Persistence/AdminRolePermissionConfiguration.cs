using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Skinora.Admin.Domain.Entities;

namespace Skinora.Admin.Infrastructure.Persistence;

/// <summary>
/// EF Core configuration for <see cref="AdminRolePermission"/>.
/// 06 §3.15, §4.1, §4.2, §5.1.
/// </summary>
public class AdminRolePermissionConfiguration : IEntityTypeConfiguration<AdminRolePermission>
{
    public void Configure(EntityTypeBuilder<AdminRolePermission> builder)
    {
        builder.ToTable("AdminRolePermissions");

        // --- Properties ---
        builder.Property(p => p.AdminRoleId)
            .IsRequired();

        builder.Property(p => p.Permission)
            .IsRequired()
            .HasMaxLength(100);

        // --- Soft delete query filter ---
        builder.HasQueryFilter(p => !p.IsDeleted);

        // --- Relationships (06 §4.1, §4.2 — NO ACTION cascade) ---
        builder.HasOne<AdminRole>()
            .WithMany()
            .HasForeignKey(p => p.AdminRoleId);

        // --- Unique constraints (06 §5.1) ---
        // Per-role permission uniqueness among active rows.
        builder.HasIndex(p => new { p.AdminRoleId, p.Permission })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("UQ_AdminRolePermissions_AdminRoleId_Permission");
    }
}
