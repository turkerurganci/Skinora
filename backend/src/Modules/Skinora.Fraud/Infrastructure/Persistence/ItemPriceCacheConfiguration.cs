using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Skinora.Fraud.Domain.Entities;

namespace Skinora.Fraud.Infrastructure.Persistence;

/// <summary>
/// EF Core configuration for <see cref="ItemPriceCache"/> (06 §3.24,
/// §5.1, §5.2).
/// </summary>
public class ItemPriceCacheConfiguration : IEntityTypeConfiguration<ItemPriceCache>
{
    public void Configure(EntityTypeBuilder<ItemPriceCache> builder)
    {
        builder.ToTable("ItemPriceCaches");

        // --- Properties (06 §3.24) ---
        builder.Property(c => c.MarketHashName)
            .IsRequired()
            .HasMaxLength(450);

        builder.Property(c => c.MedianPrice)
            .HasPrecision(18, 6);

        builder.Property(c => c.LowestPrice)
            .HasPrecision(18, 6);

        builder.Property(c => c.FetchedAt)
            .IsRequired();

        builder.Property(c => c.Source)
            .IsRequired()
            .HasMaxLength(20);

        // --- Unique index (06 §5.1) — one cache row per item. ---
        builder.HasIndex(c => c.MarketHashName)
            .IsUnique()
            .HasDatabaseName("UQ_ItemPriceCaches_MarketHashName");

        // --- Performance index (06 §5.2) — stale-row scan for the
        // background refresh job. ---
        builder.HasIndex(c => c.FetchedAt)
            .HasDatabaseName("IX_ItemPriceCaches_FetchedAt");

        // --- Source allowlist (06 §3.24 — MVP single source). ---
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_ItemPriceCaches_Source",
            "[Source] = 'STEAM_MARKET'"));
    }
}
