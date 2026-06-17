using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Skinora.Notifications.Domain.Entities;
using Skinora.Transactions.Domain.Entities;
using Skinora.Users.Domain.Entities;

namespace Skinora.Notifications.Infrastructure.Persistence;

/// <summary>
/// EF Core configuration for <see cref="Notification"/>.
/// 06 §3.13, §4.1, §5.1, §5.2.
/// </summary>
public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("Notifications");

        // --- Properties ---
        builder.Property(n => n.UserId)
            .IsRequired();

        builder.Property(n => n.Type)
            .IsRequired();

        builder.Property(n => n.Title)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(n => n.Body)
            .IsRequired()
            .HasMaxLength(2000);

        builder.Property(n => n.IsRead)
            .IsRequired()
            .HasDefaultValue(false);

        // --- Soft delete query filter ---
        builder.HasQueryFilter(n => !n.IsDeleted);

        // --- Relationships (06 §4.1 — NO ACTION cascade) ---
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(n => n.UserId);

        builder.HasOne<Transaction>()
            .WithMany()
            .HasForeignKey(n => n.TransactionId);

        // --- Performance indexes (06 §5.2) ---
        builder.HasIndex(n => new { n.UserId, n.IsRead })
            .HasDatabaseName("IX_Notifications_UserId_IsRead");

        builder.HasIndex(n => n.CreatedAt)
            .HasDatabaseName("IX_Notifications_CreatedAt");

        // --- WP8 — admin flag-alert link (07 §8.1) ---
        // Filtered: only ADMIN_FLAG_ALERT rows carry a FlagId, so the index
        // stays small and is used to resolve a flag → its alert notifications.
        builder.HasIndex(n => n.FlagId)
            .HasDatabaseName("IX_Notifications_FlagId")
            .HasFilter("[FlagId] IS NOT NULL");
    }
}
