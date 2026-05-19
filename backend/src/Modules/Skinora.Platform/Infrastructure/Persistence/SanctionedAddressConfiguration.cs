using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Skinora.Platform.Domain.Entities;
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
            // 06 §3.25 — MVP yalnız TRC-20. CHECK constraint ileride ek
            // ağ değerleri eklenince genişler.
            t.HasCheckConstraint(
                "CK_SanctionedAddresses_Network",
                "[Network] IN ('TRC-20')");

            // 06 §3.25 — Source allowlist. MVP'de admin yalnız MANUAL set'ler;
            // OFAC/EU/UN reserved (auto-sync post-MVP).
            t.HasCheckConstraint(
                "CK_SanctionedAddresses_Source",
                "[Source] IN ('OFAC', 'EU', 'UN', 'MANUAL')");
        });

        // --- Properties (06 §3.25) ---
        builder.Property(s => s.Address)
            .IsRequired()
            .HasMaxLength(64);

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

        // --- Filtered UQ (06 §3.25 — aktif satır başına benzersiz adres;
        // deactivate edilen satır arşiv olarak kalır, aynı adres yeniden
        // listelenebilir). Lookup sorgusu `WHERE Address = @ AND IsActive = 1`
        // bu indeksin filter'ı ile birebir örtüştüğünden ayrı bir non-filtered
        // performans indeksi gerekmez — filtered UQ hem benzersizlik hem de
        // match hot-path için aynı erişim yolunu sağlar (06 §3.25 indeks
        // tablosu ikinci satırla aynı sütun, birleştirildi). ---
        builder.HasIndex(s => s.Address)
            .IsUnique()
            .HasFilter("[IsActive] = 1")
            .HasDatabaseName("UQ_SanctionedAddresses_Address_Active");
    }
}
