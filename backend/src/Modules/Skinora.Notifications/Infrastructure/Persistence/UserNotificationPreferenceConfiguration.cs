using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Skinora.Notifications.Domain.Entities;
using Skinora.Users.Domain.Entities;

namespace Skinora.Notifications.Infrastructure.Persistence;

/// <summary>
/// EF Core configuration for <see cref="UserNotificationPreference"/>.
/// 06 §3.4, §4.1, §5.1, §5.2.
/// </summary>
public class UserNotificationPreferenceConfiguration : IEntityTypeConfiguration<UserNotificationPreference>
{
    public void Configure(EntityTypeBuilder<UserNotificationPreference> builder)
    {
        builder.ToTable("UserNotificationPreferences");

        // --- Properties ---
        builder.Property(p => p.UserId)
            .IsRequired();

        builder.Property(p => p.Channel)
            .IsRequired();

        builder.Property(p => p.IsEnabled)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(p => p.ExternalId)
            .HasMaxLength(256);

        // --- Soft delete query filter ---
        builder.HasQueryFilter(p => !p.IsDeleted);

        // --- Relationships (06 §4.1 — NO ACTION cascade) ---
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(p => p.UserId);

        // --- Unique constraints (06 §5.1) ---
        // One active preference per user per channel.
        builder.HasIndex(p => new { p.UserId, p.Channel })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("UQ_UserNotificationPreferences_UserId_Channel");

        // Same external target can only be linked to one active account —
        // prevents multi-account abuse and notification confusion (06 §3.4).
        builder.HasIndex(p => new { p.Channel, p.ExternalId })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0 AND [ExternalId] IS NOT NULL")
            .HasDatabaseName("UQ_UserNotificationPreferences_Channel_ExternalId");
    }
}
