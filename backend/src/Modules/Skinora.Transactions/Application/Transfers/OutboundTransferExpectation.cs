using Skinora.Platform.Application.Wallets;
using Skinora.Shared.Enums;
using Skinora.Transactions.Application.Networks;
using Skinora.Transactions.Domain.Entities;

namespace Skinora.Transactions.Application.Transfers;

/// <summary>
/// What an outbound row moves on chain — one rule for the three places that
/// need it, so they never disagree (T157 düzeltme turu, ⚠ PARA):
/// the dispatcher (which token it asks the service to send), the confirmation
/// job (what the status read expects, T158 (g)) and the two-person closure
/// tool (what the hash is verified against, 07 §9.32).
/// </summary>
/// <remarks>
/// <para>
/// <b>Token.</b> 06 §3.8 stores the EXPECTED stablecoin in <c>Token</c> for the
/// wrong-token family and the contract that actually landed on the deposit in
/// <c>ActualTokenAddress</c>: a <see cref="BlockchainTransactionType.WRONG_TOKEN_REFUND"/>
/// returns the token that arrived (a USDC refund on a USDT transaction). Its
/// chain token is the allowlisted token of that contract; a contract that is
/// not allowlisted resolves to nothing (fail closed — never the expected token).
/// </para>
/// <para>
/// <b>Sender.</b> The row's <c>FromAddress</c> when it is written (the
/// dispatcher writes the deposit, or the hot wallet for a refund after the
/// sweep, T158 (f), before it signs); a payout never records one and leaves
/// the network's hot wallet. Anything else is unknown — never guessed.
/// </para>
/// </remarks>
public static class OutboundTransferExpectation
{
    /// <summary>The stablecoin the row moves on chain; null when it cannot be resolved.</summary>
    public static StablecoinType? ChainToken(
        BlockchainTransaction row, IStablecoinContractRegistry contracts) =>
        ChainToken(row.Network, row.Type, row.Token, row.ActualTokenAddress, contracts);

    /// <summary>The same rule on a row's columns (for list projections).</summary>
    public static StablecoinType? ChainToken(
        BlockchainNetwork network,
        BlockchainTransactionType type,
        StablecoinType token,
        string? actualTokenAddress,
        IStablecoinContractRegistry contracts)
    {
        if (type != BlockchainTransactionType.WRONG_TOKEN_REFUND) return token;
        return contracts.ResolveByContract(network, actualTokenAddress);
    }

    /// <summary>The account the tokens leave; null when the row does not say and it is not a payout.</summary>
    public static string? Sender(
        BlockchainTransaction row, IPlatformWalletAddressProvider walletAddresses)
    {
        if (!string.IsNullOrWhiteSpace(row.FromAddress)) return row.FromAddress;
        if (row.Type == BlockchainTransactionType.SELLER_PAYOUT)
        {
            var hot = walletAddresses.For(row.Network).HotWalletAddress;
            return string.IsNullOrWhiteSpace(hot) ? null : hot;
        }
        return null;
    }

    /// <summary>
    /// The full transfer the row expects on chain (contract, sender, recipient,
    /// amount); null when any part is unknown — the caller then refuses
    /// (closure tool) or lets the service answer <c>UNKNOWN</c> (status read).
    /// </summary>
    public static TransferStatusExpectation? For(
        BlockchainTransaction row,
        IStablecoinContractRegistry contracts,
        IPlatformWalletAddressProvider walletAddresses)
    {
        var token = ChainToken(row, contracts);
        var from = Sender(row, walletAddresses);
        if (token is null || from is null || string.IsNullOrWhiteSpace(row.ToAddress)) return null;

        var contract = contracts.ResolveContractAddress(row.Network, token.Value);
        if (string.IsNullOrWhiteSpace(contract)) return null;
        return new TransferStatusExpectation(contract, from, row.ToAddress, row.Amount);
    }
}
