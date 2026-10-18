using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Skinora.Platform.Domain.Entities;
using Skinora.Shared.Persistence.Converters;
using Skinora.Users.Domain.Entities;

namespace Skinora.Platform.Infrastructure.Persistence;

/// <summary>
/// EF Core configuration for <see cref="SanctionedAddress"/> — 06 §3.25.
/// </summary>
public class SanctionedAddressConfiguration : IEntityTypeConfiguration<SanctionedAddress>
{
    public void Configure(EntityTypeBuilder<SanctionedAddress> builder)
    {
        builder.ToTable("SanctionedAddresses", t =>
        {
            // 06 §3.25, §2.25 (T145) — BlockchainNetwork değerleri. Yeni ağ
            // eklenince (Arbitrum/Avalanche, 11 §F8 K1) enum ile birlikte
            // genişler.
            t.HasCheckConstraint(
                "CK_SanctionedAddresses_Network",
                "[Network] IN ('TRON', 'ETHEREUM')");

            // 06 §3.25 — Source allowlist. MVP'de admin yalnız MANUAL set'ler;
            // OFAC/EU/UN reserved (auto-sync post-MVP).
            t.HasCheckConstraint(
                "CK_SanctionedAddresses_Source",
                "[Source] IN ('OFAC', 'EU', 'UN', 'MANUAL')");
        });

        // --- Properties (06 §3.25) ---
        // Kanonik biçim (06 §1.4): EVM küçük harf, TRON olduğu gibi.
        builder.Property(s => s.Address)
            .IsRequired()
            .HasMaxLength(64)
            .HasConversion(CanonicalChainIdentifierConverter.Instance);

        builder.Property(s => s.Network)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(s => s.Source)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(s => s.Reason)
            .HasMaxLength(500);

        builder.Property(s => s.ListedAt)
            .IsRequired();

        builder.Property(s => s.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        // --- Relationships (06 §4.1, §4.2 NO ACTION cascade) ---
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(s => s.AddedByAdminId);

        // --- Filtered UQ (06 §3.25 — ağ başına, aktif satır başına benzersiz
        // adres; deactivate edilen satır arşiv olarak kalır, aynı adres
        // yeniden listelenebilir). Lookup sorgusu
        // `WHERE Network = @ AND Address = @ AND IsActive = 1` bu indeksin
        // filter'ı ile birebir örtüştüğünden ayrı bir non-filtered performans
        // indeksi gerekmez — filtered UQ hem benzersizlik hem de match
        // hot-path için aynı erişim yolunu sağlar. T145: anahtar ağı içerir
        // (06 §1.4). ---
        builder.HasIndex(s => new { s.Network, s.Address })
            .IsUnique()
            .HasFilter("[IsActive] = 1")
            .HasDatabaseName("UQ_SanctionedAddresses_Network_Address_Active");
    }
}
