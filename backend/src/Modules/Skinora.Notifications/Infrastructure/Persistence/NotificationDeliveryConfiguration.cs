using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Skinora.Notifications.Domain.Entities;

namespace Skinora.Notifications.Infrastructure.Persistence;

/// <summary>
/// EF Core configuration for <see cref="NotificationDelivery"/>.
/// 06 §3.13a, §4.1, §5.1, §5.2.
/// </summary>
public class NotificationDeliveryConfiguration : IEntityTypeConfiguration<NotificationDelivery>
{
    public void Configure(EntityTypeBuilder<NotificationDelivery> builder)
    {
        builder.ToTable("NotificationDeliveries");

        // --- Properties ---
        builder.Property(d => d.NotificationId)
            .IsRequired();

        builder.Property(d => d.Channel)
            .IsRequired();

        builder.Property(d => d.TargetExternalId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(d => d.Status)
            .IsRequired();

        builder.Property(d => d.AttemptCount)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(d => d.LastError)
            .HasMaxLength(2000);

        // --- Relationships (06 §4.1) ---
        builder.HasOne<Notification>()
            .WithMany()
            .HasForeignKey(d => d.NotificationId);

        // --- Unique constraints (06 §5.1) ---
        // One delivery record per notification per channel — single-row workflow model.
        builder.HasIndex(d => new { d.NotificationId, d.Channel })
            .IsUnique()
            .HasDatabaseName("UQ_NotificationDeliveries_NotificationId_Channel");

        // --- State-dependent CHECK constraints (06 §3.13a) ---
        // SENT → SentAt NOT NULL
        builder.ToTable(t => t.HasCheckConstraint("CK_NotificationDeliveries_Sent_SentAt",
            "(Status <> 'SENT') OR (SentAt IS NOT NULL)"));

        // FAILED → LastError NOT NULL
        builder.ToTable(t => t.HasCheckConstraint("CK_NotificationDeliveries_Failed_LastError",
            "(Status <> 'FAILED') OR (LastError IS NOT NULL)"));

        // T78 — DEFERRED (transient failure exhausted the immediate retry
        // budget, the deferred-tier job will retry on 30 dk / 1 sa / 4 sa
        // delays per 08 §4.3). The transition path is FAILED-shaped: the
        // job sets LastError before flipping to DEFERRED, so the same
        // invariant applies.
        builder.ToTable(t => t.HasCheckConstraint("CK_NotificationDeliveries_Deferred_LastError",
            "(Status <> 'DEFERRED') OR (LastError IS NOT NULL)"));
    }
}
