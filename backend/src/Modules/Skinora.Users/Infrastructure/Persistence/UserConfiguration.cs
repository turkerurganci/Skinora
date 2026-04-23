using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Skinora.Shared.Domain.Seed;
using Skinora.Users.Domain.Entities;

namespace Skinora.Users.Infrastructure.Persistence;

/// <summary>
/// EF Core configuration for User entity — 06 §3.1, §5.1, §5.2.
/// </summary>
public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");

        // --- Primary key ---
        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).ValueGeneratedNever();

        // --- Steam identity ---
        builder.Property(u => u.SteamId)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(u => u.SteamDisplayName)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(u => u.SteamAvatarUrl)
            .HasMaxLength(500);

        // --- Wallet addresses ---
        builder.Property(u => u.DefaultPayoutAddress)
            .HasMaxLength(50);

        builder.Property(u => u.DefaultRefundAddress)
            .HasMaxLength(50);

        builder.Property(u => u.PayoutAddressChangedAt);

        builder.Property(u => u.RefundAddressChangedAt);

        // --- Profile ---
        builder.Property(u => u.Email)
            .HasMaxLength(256);

        builder.Property(u => u.EmailVerifiedAt);

        builder.Property(u => u.PreferredLanguage)
            .IsRequired()
            .HasMaxLength(5)
            .HasDefaultValue("en");

        // --- Steam trade URL (07 §5.16a) ---
        builder.Property(u => u.SteamTradeUrl)
            .HasMaxLength(500);

        builder.Property(u => u.SteamTradePartner)
            .HasMaxLength(20);

        builder.Property(u => u.SteamTradeAccessToken)
            .HasMaxLength(20);

        // --- ToS ---
        builder.Property(u => u.TosAcceptedVersion)
            .HasMaxLength(20);

        // --- Steam verification ---
        builder.Property(u => u.MobileAuthenticatorVerified)
            .IsRequired()
            .HasDefaultValue(false);

        // --- Reputation ---
        builder.Property(u => u.CompletedTransactionCount)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(u => u.SuccessfulTransactionRate)
            .HasPrecision(5, 4);

        // --- Account state ---
        builder.Property(u => u.IsDeactivated)
            .IsRequired()
            .HasDefaultValue(false);

        // --- Soft delete ---
        builder.Property(u => u.IsDeleted)
            .IsRequired()
            .HasDefaultValue(false);

        // --- Unique constraints (06 §5.1) ---
        builder.HasIndex(u => u.SteamId)
            .IsUnique()
            .HasDatabaseName("UQ_Users_SteamId");

        // --- Performance indexes (06 §5.2) ---
        builder.HasIndex(u => u.DefaultPayoutAddress)
            .HasDatabaseName("IX_Users_DefaultPayoutAddress");

        builder.HasIndex(u => u.DefaultRefundAddress)
            .HasDatabaseName("IX_Users_DefaultRefundAddress");

        // --- Seed: SYSTEM service account (06 §8.9) ---
        // Referenced as the sentinel ActorId for AuditLog / TransactionHistory
        // rows produced by platform-automated actions. IsDeactivated = true
        // excludes it from operational user queries (06 §1.3 predicate).
        // RowVersion is set explicitly so SQLite-backed test hosts (which
        // don't auto-populate rowversion the way SQL Server does) accept the
        // seed INSERT; SQL Server overwrites it on first write anyway.
        builder.HasData(new
        {
            Id = SeedConstants.SystemUserId,
            SteamId = SeedConstants.SystemSteamId,
            SteamDisplayName = "System",
            PreferredLanguage = "en",
            MobileAuthenticatorVerified = false,
            CompletedTransactionCount = 0,
            IsDeactivated = true,
            IsDeleted = false,
            CreatedAt = SeedConstants.SeedAnchorUtc,
            UpdatedAt = SeedConstants.SeedAnchorUtc,
            RowVersion = SeedConstants.SeedRowVersion,
        });
    }
}
