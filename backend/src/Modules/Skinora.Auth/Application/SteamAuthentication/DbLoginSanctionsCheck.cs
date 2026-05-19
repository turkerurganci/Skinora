using Microsoft.EntityFrameworkCore;
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
/// Mevcut kullanıcı için (Steam ID ile lookup) <c>DefaultPayoutAddress</c> ve
/// <c>DefaultRefundAddress</c> alanları aktif sanctions listesine karşı
/// kontrol edilir. İlk eşleşme döner — admin'in hangi adresi listelediği
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

        var wallets = await _db.Set<User>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(u => u.SteamId == steamId64)
            .Select(u => new
            {
                u.DefaultPayoutAddress,
                u.DefaultRefundAddress,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (wallets is null)
            return SanctionsDecision.NoMatch();

        if (!string.IsNullOrEmpty(wallets.DefaultPayoutAddress))
        {
            var payoutMatch = await _lookup.FindActiveAsync(
                wallets.DefaultPayoutAddress, cancellationToken);
            if (payoutMatch is not null)
                return SanctionsDecision.Match(payoutMatch.Source);
        }

        if (!string.IsNullOrEmpty(wallets.DefaultRefundAddress))
        {
            var refundMatch = await _lookup.FindActiveAsync(
                wallets.DefaultRefundAddress, cancellationToken);
            if (refundMatch is not null)
                return SanctionsDecision.Match(refundMatch.Source);
        }

        return SanctionsDecision.NoMatch();
    }
}
