using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Skinora.Users.Domain.Entities;

namespace Skinora.Users.Infrastructure.Persistence;

/// <summary>
/// EF Core configuration for WalletAddressHistory — 06 §3.1, §4.2, §5.2 (T105b).
/// Append-only audit trail of superseded wallet addresses: no UPDATE/DELETE
/// (enforced via <see cref="Skinora.Shared.Domain.IAppendOnly"/> at the
/// <c>AppDbContext</c> level).
/// </summary>
public class WalletAddressHistoryConfiguration : IEntityTypeConfiguration<WalletAddressHistory>
{
    public void Configure(EntityTypeBuilder<WalletAddressHistory> builder)
    {
        builder.ToTable("WalletAddressHistory");

        // --- Primary key (long, IDENTITY) ---
        builder.HasKey(h => h.Id);
        builder.Property(h => h.Id).ValueGeneratedOnAdd();

        // --- Fields ---
        builder.Property(h => h.UserId)
            .IsRequired();

        builder.Property(h => h.Type)
            .IsRequired()
            .HasMaxLength(10);

        // Same length as User.DefaultPayoutAddress / DefaultRefundAddress.
        builder.Property(h => h.Address)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(h => h.SetAt);

        builder.Property(h => h.CreatedAt)
            .IsRequired();

        // --- Relationship (configured from the child side, like UserLoginLog) ---
        builder.HasOne(h => h.User)
            .WithMany(u => u.WalletAddressHistory)
            .HasForeignKey(h => h.UserId);

        // --- Performance indexes (06 §5.2) ---
        builder.HasIndex(h => h.UserId)
            .HasDatabaseName("IX_WalletAddressHistory_UserId");

        builder.HasIndex(h => new { h.UserId, h.Type })
            .HasDatabaseName("IX_WalletAddressHistory_UserId_Type");
    }
}
