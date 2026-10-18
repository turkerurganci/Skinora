using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Skinora.Shared.Persistence.Converters;
using Skinora.Users.Domain.Entities;

namespace Skinora.Users.Infrastructure.Persistence;

/// <summary>
/// EF Core configuration for <see cref="UserWalletAddress"/> — 06 §1.4, §3.1a,
/// §5.1, §5.2 (T146).
/// </summary>
public class UserWalletAddressConfiguration : IEntityTypeConfiguration<UserWalletAddress>
{
    public void Configure(EntityTypeBuilder<UserWalletAddress> builder)
    {
        builder.ToTable("UserWalletAddresses");

        builder.HasKey(w => w.Id);
        builder.Property(w => w.Id).ValueGeneratedNever();

        builder.Property(w => w.UserId)
            .IsRequired();

        // Enum names, like every Skinora enum (AppDbContext convention).
        // No database DEFAULT — a row without a network or type cannot be written.
        builder.Property(w => w.Network)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(w => w.Type)
            .IsRequired()
            .HasMaxLength(10);

        // Same length as every other address column; canonical form (06 §1.4).
        builder.Property(w => w.Address)
            .IsRequired()
            .HasMaxLength(50)
            .HasConversion(CanonicalChainIdentifierConverter.Instance);

        builder.Property(w => w.ChangedAt);

        builder.HasOne(w => w.User)
            .WithMany(u => u.WalletAddresses)
            .HasForeignKey(w => w.UserId);

        // --- Unique (06 §5.1): one address per user, network and type ---
        builder.HasIndex(w => new { w.UserId, w.Network, w.Type })
            .IsUnique()
            .HasDatabaseName("UQ_UserWalletAddresses_UserId_Network_Type");

        // --- Performance (06 §5.2): address lookups — multi-account matching
        // (same address on another account) and the retroactive sanctions scan.
        builder.HasIndex(w => new { w.Network, w.Address })
            .HasDatabaseName("IX_UserWalletAddresses_Network_Address");
    }
}
