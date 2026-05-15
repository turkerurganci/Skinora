using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Skinora.Shared.Persistence.Webhooks.Configurations;

/// <summary>
/// EF Core mapping for <see cref="ProcessedNonce"/> (05 §3.4, 09 §11.3).
/// </summary>
public class ProcessedNonceConfiguration : IEntityTypeConfiguration<ProcessedNonce>
{
    public void Configure(EntityTypeBuilder<ProcessedNonce> builder)
    {
        builder.ToTable("ProcessedNonces");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .ValueGeneratedNever();

        builder.Property(x => x.Source)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(x => x.Nonce)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(x => x.ProcessedAt)
            .IsRequired();

        builder.Property(x => x.ExpiresAt)
            .IsRequired();

        // (Source, Nonce) çifti benzersiz — replay tespiti DB seviyesinde garanti.
        builder.HasIndex(x => new { x.Source, x.Nonce })
            .IsUnique()
            .HasDatabaseName("UX_ProcessedNonces_Source_Nonce");

        // ProcessedNonceCleanupJob için tarama indeksi.
        builder.HasIndex(x => x.ExpiresAt)
            .HasDatabaseName("IX_ProcessedNonces_ExpiresAt");
    }
}
