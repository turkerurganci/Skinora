using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Skinora.Transactions.Domain.Entities;

namespace Skinora.Transactions.Infrastructure.Persistence;

/// <summary>
/// EF Core configuration for TransactionHistory entity — 06 §3.6, §4.1, §5.2.
/// Append-only audit trail: no UPDATE/DELETE operations defined.
/// </summary>
public class TransactionHistoryConfiguration : IEntityTypeConfiguration<TransactionHistory>
{
    public void Configure(EntityTypeBuilder<TransactionHistory> builder)
    {
        builder.ToTable("TransactionHistory");

        // --- Primary key (long, IDENTITY) ---
        builder.HasKey(h => h.Id);
        builder.Property(h => h.Id).ValueGeneratedOnAdd();

        // --- Fields ---
        builder.Property(h => h.TransactionId)
            .IsRequired();

        builder.Property(h => h.NewStatus)
            .IsRequired();

        builder.Property(h => h.Trigger)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(h => h.ActorType)
            .IsRequired();

        builder.Property(h => h.ActorId)
            .IsRequired();

        // AdditionalData: string? with no explicit length → EF Core default is
        // nvarchar(max) on SQL Server and TEXT on SQLite. Explicit HasColumnType
        // would force SQL Server syntax and break SQLite integration tests
        // (API.Tests/Integration use SQLite, see T12 Notlar).
        builder.Property(h => h.AdditionalData);

        builder.Property(h => h.CreatedAt)
            .IsRequired();

        // --- Relationships (06 §4.1) ---
        // TransactionHistory → Transaction (configured in TransactionConfiguration via HasMany)

        // TransactionHistory → User (actor)
        builder.HasOne<Skinora.Users.Domain.Entities.User>()
            .WithMany()
            .HasForeignKey(h => h.ActorId);

        // --- Performance indexes (06 §5.2) ---
        builder.HasIndex(h => h.TransactionId)
            .HasDatabaseName("IX_TransactionHistory_TransactionId");
    }
}
