using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Skinora.Auth.Domain.Entities;

namespace Skinora.Auth.Infrastructure.Persistence;

/// <summary>
/// EF Core configuration for RefreshToken entity — 06 §3.3, §5.1, §5.2.
/// </summary>
public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("RefreshTokens");

        // --- Primary key ---
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();

        // --- Token ---
        builder.Property(t => t.Token)
            .IsRequired()
            .HasMaxLength(256);

        // --- Expiration ---
        builder.Property(t => t.ExpiresAt)
            .IsRequired();

        // --- Revocation ---
        builder.Property(t => t.IsRevoked)
            .IsRequired()
            .HasDefaultValue(false);

        // --- Soft delete ---
        builder.Property(t => t.IsDeleted)
            .IsRequired()
            .HasDefaultValue(false);

        // --- Device/session ---
        builder.Property(t => t.DeviceInfo)
            .HasMaxLength(256);

        builder.Property(t => t.IpAddress)
            .HasMaxLength(45);

        // --- Relationships ---
        builder.HasOne(t => t.User)
            .WithMany()
            .HasForeignKey(t => t.UserId);

        builder.HasOne(t => t.ReplacedByToken)
            .WithMany()
            .HasForeignKey(t => t.ReplacedByTokenId);

        // --- Unique constraints (06 §5.1) ---
        builder.HasIndex(t => t.Token)
            .IsUnique()
            .HasDatabaseName("UQ_RefreshTokens_Token");

        // --- Performance indexes (06 §5.2) ---
        builder.HasIndex(t => t.UserId)
            .HasDatabaseName("IX_RefreshTokens_UserId");
    }
}
