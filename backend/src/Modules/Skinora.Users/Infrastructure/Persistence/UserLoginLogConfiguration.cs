using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Skinora.Users.Domain.Entities;

namespace Skinora.Users.Infrastructure.Persistence;

/// <summary>
/// EF Core configuration for UserLoginLog entity — 06 §3.2, §5.2.
/// </summary>
public class UserLoginLogConfiguration : IEntityTypeConfiguration<UserLoginLog>
{
    public void Configure(EntityTypeBuilder<UserLoginLog> builder)
    {
        builder.ToTable("UserLoginLogs");

        // --- Primary key (long, IDENTITY) ---
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedOnAdd();

        // --- Fields ---
        builder.Property(l => l.UserId)
            .IsRequired();

        builder.Property(l => l.IpAddress)
            .IsRequired()
            .HasMaxLength(45);

        builder.Property(l => l.DeviceFingerprint)
            .HasMaxLength(256);

        builder.Property(l => l.UserAgent)
            .HasMaxLength(500);

        // --- Soft delete ---
        builder.Property(l => l.IsDeleted)
            .IsRequired()
            .HasDefaultValue(false);

        // --- Timestamp ---
        builder.Property(l => l.CreatedAt)
            .IsRequired();

        // --- Relationships ---
        builder.HasOne(l => l.User)
            .WithMany(u => u.LoginLogs)
            .HasForeignKey(l => l.UserId);

        // --- Performance indexes (06 §5.2) ---
        builder.HasIndex(l => l.UserId)
            .HasDatabaseName("IX_UserLoginLogs_UserId");

        builder.HasIndex(l => l.IpAddress)
            .HasDatabaseName("IX_UserLoginLogs_IpAddress");

        builder.HasIndex(l => l.DeviceFingerprint)
            .HasDatabaseName("IX_UserLoginLogs_DeviceFingerprint");
    }
}
