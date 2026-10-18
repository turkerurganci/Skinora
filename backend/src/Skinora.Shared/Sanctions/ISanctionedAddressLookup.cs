using Skinora.Shared.Enums;

namespace Skinora.Shared.Sanctions;

/// <summary>
/// Read port over the sanctioned address list (06 §3.25, 02 §21.1). Used by:
/// <list type="bullet">
///   <item><description><c>DbWalletSanctionsCheck</c> (Users module, T34 pipeline) — adres kaydı sırasında.</description></item>
///   <item><description><c>DbLoginSanctionsCheck</c> (Auth module, T29 pipeline) — login sonrası mevcut wallet adresleri için.</description></item>
///   <item><description>Admin retroaktif scan (yeni adres ekleme sonrası).</description></item>
/// </list>
/// Yalnız <c>IsActive = true</c> satırları görür. Interface Skinora.Shared'de
/// yaşar çünkü Skinora.Platform → Skinora.Users yön dependency'sini koruyup
/// Users'ın da consume edebilmesini sağlar (T82 — Platform impl detayı
/// SanctionedAddressLookup'ta).
/// </summary>
public interface ISanctionedAddressLookup
{
    /// <summary>
    /// Returns a <see cref="SanctionedAddressMatch"/> when <paramref name="address"/>
    /// is on the active sanctions list of <paramref name="network"/>; <c>null</c>
    /// otherwise. Eşleşme <c>(Network, kanonik Address)</c> üzerindendir
    /// (06 §1.4, §3.25): EVM adresi büyük/küçük harfe duyarsız, TRON base58
    /// birebir (büyük/küçük harfe duyarlı) karşılaştırılır.
    /// </summary>
    Task<SanctionedAddressMatch?> FindActiveAsync(
        BlockchainNetwork network, string address, CancellationToken cancellationToken);
}
