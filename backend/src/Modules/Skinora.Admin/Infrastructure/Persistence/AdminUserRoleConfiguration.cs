using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Skinora.Admin.Domain.Entities;
using Skinora.Users.Domain.Entities;

namespace Skinora.Admin.Infrastructure.Persistence;

/// <summary>
/// EF Core configuration for <see cref="AdminUserRole"/>.
/// 06 §3.16, §4.1, §4.2, §5.1.
/// </summary>
public class AdminUserRoleConfiguration : IEntityTypeConfiguration<AdminUserRole>
{
    public void Configure(EntityTypeBuilder<AdminUserRole> builder)
    {
        builder.ToTable("AdminUserRoles");

        // --- Properties ---
        builder.Property(ur => ur.UserId)
            .IsRequired();

        builder.Property(ur => ur.AdminRoleId)
            .IsRequired();

        builder.Property(ur => ur.AssignedAt)
            .IsRequired();

        // --- Soft delete query filter ---
        builder.HasQueryFilter(ur => !ur.IsDeleted);

        // --- Relationships (06 §4.1, §4.2 — NO ACTION cascade) ---
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(ur => ur.UserId);

        builder.HasOne<AdminRole>()
            .WithMany()
            .HasForeignKey(ur => ur.AdminRoleId);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(ur => ur.AssignedByAdminId);

        // --- Unique constraints (06 §5.1) ---
        // Per-user role uniqueness among active rows — surrogate PK enables
        // re-assignment after soft delete.
        builder.HasIndex(ur => new { ur.UserId, ur.AdminRoleId })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("UQ_AdminUserRoles_UserId_AdminRoleId");
    }
}
