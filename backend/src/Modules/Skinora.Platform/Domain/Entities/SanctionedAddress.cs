using Skinora.Shared.Domain;

namespace Skinora.Platform.Domain.Entities;

/// <summary>
/// Yaptırımlı cüzdan adresi (06 §3.25). MVP'de yalnız admin tarafından
/// yönetilen manuel kayıt — OFAC SDN / EU / BM feed auto-sync entegrasyonu
/// post-MVP. Match sorguları yalnız <see cref="IsActive"/> = true satırları
/// görür. Deactivation soft yapılır (<see cref="IsActive"/> = false) — audit
/// izi korunur, aynı adres tekrar eklenebilir (filtered UQ izin verir).
/// </summary>
public class SanctionedAddress : BaseEntity
{
    public string Address { get; set; } = string.Empty;

    /// <summary>
    /// Ağ kimliği — MVP'de yalnız <c>TRC-20</c> (06 §3.25). Reserved for
    /// ERC-20 / BTC genişlemesi. CHECK constraint yalnız MVP değerini kabul
    /// eder.
    /// </summary>
    public string Network { get; set; } = SanctionedAddressNetworks.Trc20;

    /// <summary>
    /// Liste kaynağı — <c>OFAC</c>, <c>EU</c>, <c>UN</c>, <c>MANUAL</c>.
    /// MVP'de yalnız <c>MANUAL</c> admin entry; auto-sync (post-MVP) için
    /// diğer değerler reserved.
    /// </summary>
    public string Source { get; set; } = SanctionedAddressSources.Manual;

    /// <summary>
    /// Admin tarafından girilen serbest metin sebep — OFAC SDN list
    /// referansı, FBI bildirim no, soruşturma referansı vb. Opsiyonel,
    /// max 500 karakter.
    /// </summary>
    public string? Reason { get; set; }

    /// <summary>
    /// Adresin listeye eklendiği orijinal tarih — admin entry için
    /// <see cref="BaseEntity.CreatedAt"/> ile aynı; auto-sync (post-MVP)
    /// için kaynak feed'in <c>listing_date</c>'i.
    /// </summary>
    public DateTime ListedAt { get; set; }

    /// <summary>
    /// MANUAL kaynak için admin guid; auto-sync (OFAC/EU/UN) için
    /// <c>null</c> (SYSTEM aktör — post-MVP).
    /// </summary>
    public Guid? AddedByAdminId { get; set; }

    /// <summary>
    /// Soft deactivation flag. <c>DELETE /admin/sanctions/addresses/:id</c>
    /// bu flag'i <c>false</c> yapar; satır audit kanıt olarak korunur.
    /// Match sorguları yalnız <c>true</c> satırları arar.
    /// </summary>
    public bool IsActive { get; set; } = true;
}
