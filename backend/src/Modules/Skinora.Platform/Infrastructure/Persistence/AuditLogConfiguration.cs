using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Skinora.Platform.Domain.Entities;
using Skinora.Users.Domain.Entities;

namespace Skinora.Platform.Infrastructure.Persistence;

/// <summary>
/// EF Core configuration for <see cref="AuditLog"/>.
/// 06 §3.20, §4.1, §4.2, §5.2.
/// </summary>
/// <remarks>
/// Immutability (UPDATE/DELETE rejection) is enforced at the
/// <see cref="Skinora.Shared.Persistence.AppDbContext"/> level so the rule
/// applies to every connection regardless of module wiring.
/// </remarks>
public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("AuditLogs");

        builder.HasKey(a => a.Id);

        // long IDENTITY (06 §3.20).
        builder.Property(a => a.Id)
            .ValueGeneratedOnAdd();

        builder.Property(a => a.UserId);

        builder.Property(a => a.ActorId)
            .IsRequired();

        builder.Property(a => a.ActorType)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(a => a.Action)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(a => a.EntityType)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(a => a.EntityId)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(a => a.OldValue);

        builder.Property(a => a.NewValue);

        builder.Property(a => a.IpAddress)
            .HasMaxLength(45);

        builder.Property(a => a.CreatedAt)
            .IsRequired();

        // --- Relationships (06 §4.1, §4.2 — NO ACTION cascade) ---
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(a => a.ActorId);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(a => a.UserId);

        // --- Performance indexes (06 §5.2) ---
        builder.HasIndex(a => a.ActorId)
            .HasDatabaseName("IX_AuditLogs_ActorId");

        builder.HasIndex(a => a.UserId)
            .HasDatabaseName("IX_AuditLogs_UserId");

        builder.HasIndex(a => new { a.EntityType, a.EntityId })
            .HasDatabaseName("IX_AuditLogs_EntityType_EntityId");

        builder.HasIndex(a => a.Action)
            .HasDatabaseName("IX_AuditLogs_Action");

        builder.HasIndex(a => a.CreatedAt)
            .HasDatabaseName("IX_AuditLogs_CreatedAt");
    }
}
