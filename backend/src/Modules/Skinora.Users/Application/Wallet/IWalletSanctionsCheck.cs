using Skinora.Shared.Enums;

namespace Skinora.Users.Application.Wallet;

/// <summary>
/// Address-keyed sanctions screening hook — 02 §12.3, §21.1.
/// Steam-ID-keyed screening (login pipeline) uses
/// <c>Skinora.Auth.Application.SteamAuthentication.ISanctionsCheck</c>; the two
/// surfaces stay separate because the data contracts differ (identity vs.
/// on-chain address). The real list integration lands in T82 — until then,
/// <see cref="NoMatchWalletSanctionsCheck"/> is the default no-op.
/// </summary>
/// <remarks>
/// The match key is <c>(network, canonical address)</c> (06 §1.4, §3.25 — T145).
/// Callers pass the network the address belongs to: the profile write path
/// the network of the saved address (T146); transaction creation and
/// acceptance TRON until T147 lets the parties choose a network.
/// </remarks>
public interface IWalletSanctionsCheck
{
    Task<WalletSanctionsDecision> EvaluateAsync(
        BlockchainNetwork network, string walletAddress, CancellationToken cancellationToken);
}

public sealed record WalletSanctionsDecision(bool IsMatch, string? MatchedList)
{
    public static WalletSanctionsDecision NoMatch() => new(false, null);
    public static WalletSanctionsDecision Match(string list) => new(true, list);
}

public sealed class NoMatchWalletSanctionsCheck : IWalletSanctionsCheck
{
    public Task<WalletSanctionsDecision> EvaluateAsync(
        BlockchainNetwork network, string walletAddress, CancellationToken cancellationToken)
        => Task.FromResult(WalletSanctionsDecision.NoMatch());
}
