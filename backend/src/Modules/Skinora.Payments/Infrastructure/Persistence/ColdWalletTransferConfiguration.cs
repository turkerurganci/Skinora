using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Skinora.Payments.Domain.Entities;
using Skinora.Users.Domain.Entities;

namespace Skinora.Payments.Infrastructure.Persistence;

/// <summary>
/// EF Core configuration for <see cref="ColdWalletTransfer"/>.
/// 06 §3.22, §4.1, §4.2, §5.1.
/// </summary>
/// <remarks>
/// Append-only semantics are enforced at the
/// <see cref="Skinora.Shared.Persistence.AppDbContext"/> level — no UPDATE or
/// DELETE is permitted once a row has been inserted.
/// </remarks>
public class ColdWalletTransferConfiguration
    : IEntityTypeConfiguration<ColdWalletTransfer>
{
    public void Configure(EntityTypeBuilder<ColdWalletTransfer> builder)
    {
        builder.ToTable("ColdWalletTransfers");

        builder.HasKey(c => c.Id);

        // long IDENTITY (06 §3.22).
        builder.Property(c => c.Id)
            .ValueGeneratedOnAdd();

        builder.Property(c => c.Amount)
            .IsRequired()
            .HasColumnType("decimal(18,6)");

        builder.Property(c => c.Token)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(c => c.FromAddress)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(c => c.ToAddress)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(c => c.TxHash)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(c => c.InitiatedByAdminId)
            .IsRequired();

        builder.Property(c => c.CreatedAt)
            .IsRequired();

        // --- Relationships (06 §4.1, §4.2 — NO ACTION cascade) ---
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(c => c.InitiatedByAdminId);

        // --- Unique constraints (06 §5.1) ---
        builder.HasIndex(c => c.TxHash)
            .IsUnique()
            .HasDatabaseName("UQ_ColdWalletTransfers_TxHash");
    }
}
