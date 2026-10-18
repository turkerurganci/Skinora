using Microsoft.EntityFrameworkCore;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Shared.Sanctions;
using Skinora.Users.Domain.Entities;

namespace Skinora.Auth.Application.SteamAuthentication;

/// <summary>
/// Production impl of <see cref="ISanctionsCheck"/> — T82 (02 §21.1,
/// 03 §11a.3, 03 §2.1 step 6, 06 §3.25). T29
/// <see cref="NoMatchSanctionsCheck"/> stub'unun yerine geçer.
/// </summary>
/// <remarks>
/// Mevcut kullanıcı için (Steam ID ile lookup) kayıtlı bütün ödeme ve iade
/// adresleri (UserWalletAddress — her ağ için ayrı, T146) kendi ağlarının
/// aktif sanctions listesine karşı kontrol edilir. İlk eşleşme döner — admin'in hangi adresi listelediği
/// genelde önemsiz, sonuç account-level fraud flag tetikler.
/// <para>
/// Yeni kullanıcı (henüz provisioning olmamış) durumunda User satırı yok
/// → kayıt henüz cüzdan adresi içermiyor → no-match. Provisioning sonrası
/// kullanıcı bir adres kaydederse <see cref="DbWalletSanctionsCheck"/>
/// (wallet pipeline) eşleşmeyi yakalar.
/// </para>
/// <para>
/// Soft-deleted kullanıcılar (<see cref="User.IsDeactivated"/> = true)
/// için de check yapılır — admin bir adresi sonradan listelediğinde
/// soft-delete'i bypass'la sanctions amaçlı kontrol önemlidir; provisioning
/// downstream'de zaten <c>AccountBanned</c> outcome'a düşürür. Burada
/// erken durmamak audit trail için yararlı.
/// </para>
/// </remarks>
public sealed class DbLoginSanctionsCheck : ISanctionsCheck
{
    private readonly AppDbContext _db;
    private readonly ISanctionedAddressLookup _lookup;

    public DbLoginSanctionsCheck(AppDbContext db, ISanctionedAddressLookup lookup)
    {
        _db = db;
        _lookup = lookup;
    }

    public async Task<SanctionsDecision> EvaluateAsync(
        string steamId64, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(steamId64))
            return SanctionsDecision.NoMatch();

        var userId = await _db.Set<User>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(u => u.SteamId == steamId64)
            .Select(u => (Guid?)u.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (userId is null)
            return SanctionsDecision.NoMatch();

        // T146 — every saved address, each screened on its own network
        // (06 §1.4: the match key is (network, canonical address)). Payout
        // before refund, then by network, so the first match is deterministic.
        var wallets = await _db.Set<UserWalletAddress>()
            .AsNoTracking()
            .Where(w => w.UserId == userId.Value)
            .Select(w => new { w.Network, w.Type, w.Address })
            .ToListAsync(cancellationToken);

        foreach (var wallet in wallets.OrderBy(w => w.Type).ThenBy(w => w.Network))
        {
            var match = await _lookup.FindActiveAsync(wallet.Network, wallet.Address, cancellationToken);
            if (match is not null)
                return SanctionsDecision.Match(match.Source);
        }

        return SanctionsDecision.NoMatch();
    }
}
