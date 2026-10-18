using Skinora.Shared.Enums;
using Skinora.Transactions.Application.GasFee;
using Skinora.Transactions.Application.PaymentAddresses;
using Skinora.Transactions.Application.Transfers;

namespace Skinora.Transactions.Application.Networks;

/// <summary>
/// Named <see cref="HttpClient"/>s of each network's service (T148). The three
/// kinds keep their separate time budgets (05 §3.3, #323): a broadcast waits
/// for blocks, an estimate or status read is a chain read, a wallet / monitor
/// call is quick. TRON keeps the pre-F8 names so existing wiring and
/// diagnostics are unchanged; other networks append <c>.{NETWORK}</c>.
/// </summary>
public static class BlockchainHttpClientNames
{
    /// <summary>Wallet / monitor / balances / cold-transfer client of <paramref name="network"/>.</summary>
    public static string Sidecar(BlockchainNetwork network) =>
        For(HttpBlockchainSidecarClient.HttpClientName, network);

    /// <summary>Broadcast + status client of <paramref name="network"/>.</summary>
    public static string Transfer(BlockchainNetwork network) =>
        For(HttpBlockchainTransferClient.HttpClientName, network);

    /// <summary>Fee-estimate client of <paramref name="network"/>.</summary>
    public static string GasFee(BlockchainNetwork network) =>
        For(HttpSidecarGasFeeEstimator.HttpClientName, network);

    private static string For(string baseName, BlockchainNetwork network) =>
        network == BlockchainNetwork.TRON ? baseName : $"{baseName}.{network}";
}
