using Microsoft.Extensions.Logging;
using Skinora.Shared.Enums;
using Skinora.Transactions.Application.GasFee;
using Skinora.Transactions.Application.PaymentAddresses;
using Skinora.Transactions.Application.Transfers;

namespace Skinora.Transactions.Application.Networks;

/// <summary>
/// The service of a network that has none configured (T148 — fail closed).
/// It sends nothing; every call answers with the outcome its port already
/// defines for "the service cannot act", so no caller moves money and every
/// caller retries on its usual schedule:
/// <list type="bullet">
///   <item><description>wallet / monitor / balances / cold transfer →
///   <see cref="BlockchainSidecarStatus.NotConfigured"/>;</description></item>
///   <item><description>broadcast → <see cref="TransferBroadcastStatus.TransientFailure"/>
///   with code <see cref="ErrorCode"/>;</description></item>
///   <item><description>status → <see cref="TransferStatusOutcome.Unavailable"/>;</description></item>
///   <item><description>fee estimate → <c>null</c> (the static per-network fallback).</description></item>
/// </list>
/// </summary>
public sealed class UnconfiguredBlockchainService
    : IBlockchainSidecarClient, IBlockchainTransferClient, IGasFeeEstimator
{
    /// <summary>Error code a broadcast to an unconfigured network carries.</summary>
    public const string ErrorCode = "NETWORK_SERVICE_NOT_CONFIGURED";

    private readonly BlockchainNetwork _network;
    private readonly ILogger _logger;

    public UnconfiguredBlockchainService(BlockchainNetwork network, ILogger logger)
    {
        _network = network;
        _logger = logger;
    }

    /// <summary>The network that has no service.</summary>
    public BlockchainNetwork Network => _network;

    public Task<BlockchainSidecarDeriveResult> DeriveAddressAsync(
        int index, Guid transactionId, CancellationToken cancellationToken)
    {
        Refuse("wallet/derive");
        return Task.FromResult(
            new BlockchainSidecarDeriveResult(BlockchainSidecarStatus.NotConfigured, null, null));
    }

    public Task<BlockchainSidecarStatus> StartMonitoringAsync(
        PaymentMonitorStartRequest request, CancellationToken cancellationToken) =>
        NotConfigured("monitor/start");

    public Task<BlockchainSidecarStatus> StopMonitoringAsync(
        string address, CancellationToken cancellationToken) =>
        NotConfigured("monitor/stop");

    public Task<BlockchainSidecarStatus> StartPostCancelMonitoringAsync(
        PostCancelMonitorStartRequest request, CancellationToken cancellationToken) =>
        NotConfigured("monitor/post-cancel-start");

    public Task<BlockchainSidecarStatus> StopPostCancelMonitoringAsync(
        string address, CancellationToken cancellationToken) =>
        NotConfigured("monitor/post-cancel-stop");

    public Task<BlockchainSidecarBalancesResult> GetWalletBalancesAsync(
        IReadOnlyList<string> addresses, CancellationToken cancellationToken)
    {
        Refuse("wallet/balances");
        return Task.FromResult(
            new BlockchainSidecarBalancesResult(BlockchainSidecarStatus.NotConfigured, null, null));
    }

    public Task<BlockchainSidecarTransferResult> SendHotToColdTransferAsync(
        HotToColdTransferRequest request, CancellationToken cancellationToken)
    {
        Refuse("transfer/cold-wallet");
        return Task.FromResult(
            new BlockchainSidecarTransferResult(BlockchainSidecarStatus.NotConfigured, null));
    }

    public Task<TransferBroadcastResult> BroadcastAsync(
        TransferBroadcastRequest request, CancellationToken cancellationToken)
    {
        Refuse($"transfer ({request.Type}, row {request.BlockchainTransactionId})");
        return Task.FromResult(new TransferBroadcastResult(
            TransferBroadcastStatus.TransientFailure,
            null,
            ErrorCode,
            $"No {_network} blockchain service is configured."));
    }

    public Task<TransferStatusResult> GetStatusAsync(
        string txHash, CancellationToken cancellationToken)
    {
        Refuse("transfer/status");
        return Task.FromResult(new TransferStatusResult(
            TransferStatusOutcome.Unavailable, null, null, null,
            $"No {_network} blockchain service is configured."));
    }

    public Task<decimal?> EstimateFeeUsdtAsync(
        GasFeeEstimateRequest request, CancellationToken cancellationToken)
    {
        Refuse("transfer/estimate-fee");
        return Task.FromResult<decimal?>(null);
    }

    public Task<SweepCostEstimate?> EstimateSweepCostAsync(
        StablecoinType token, CancellationToken cancellationToken)
    {
        Refuse("transfer/estimate-sweep-fee");
        return Task.FromResult<SweepCostEstimate?>(null);
    }

    private Task<BlockchainSidecarStatus> NotConfigured(string operation)
    {
        Refuse(operation);
        return Task.FromResult(BlockchainSidecarStatus.NotConfigured);
    }

    private void Refuse(string operation) =>
        _logger.LogError(
            "Blockchain call {Operation} refused: no {Network} service is configured "
            + "({Section}:BaseUrl is empty). Nothing was sent; the caller retries on its own schedule.",
            operation, _network, BlockchainSidecarOptions.SectionNameFor(_network));
}
