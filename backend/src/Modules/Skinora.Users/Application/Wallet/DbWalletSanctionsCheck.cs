using Skinora.Shared.Enums;
using Skinora.Shared.Sanctions;

namespace Skinora.Users.Application.Wallet;

/// <summary>
/// Production impl of <see cref="IWalletSanctionsCheck"/> — T82 (02 §21.1,
/// 03 §11a.3, 06 §3.25). DB lookup keyed on <c>(network, address)</c>; T34
/// <see cref="NoMatchWalletSanctionsCheck"/> stub'unun yerine geçer.
/// </summary>
/// <remarks>
/// Karşılaştırma kanonik biçim üzerindendir (06 §1.4, T145): TRON base58
/// büyük/küçük harfe duyarlı (birebir), EVM adresi duyarsız. Kural
/// <see cref="ISanctionedAddressLookup"/> implementasyonundadır. Boş tablo
/// durumunda lookup <c>null</c> döner → <see cref="WalletSanctionsDecision.NoMatch"/>;
/// dev/test ortamlarda admin yeni adres eklemediği sürece sanctions check
/// pas geçer.
/// </remarks>
public sealed class DbWalletSanctionsCheck : IWalletSanctionsCheck
{
    private readonly ISanctionedAddressLookup _lookup;

    public DbWalletSanctionsCheck(ISanctionedAddressLookup lookup)
    {
        _lookup = lookup;
    }

    public async Task<WalletSanctionsDecision> EvaluateAsync(
        BlockchainNetwork network, string walletAddress, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(walletAddress))
            return WalletSanctionsDecision.NoMatch();

        var match = await _lookup.FindActiveAsync(network, walletAddress, cancellationToken);
        return match is null
            ? WalletSanctionsDecision.NoMatch()
            : WalletSanctionsDecision.Match(match.Source);
    }
}
