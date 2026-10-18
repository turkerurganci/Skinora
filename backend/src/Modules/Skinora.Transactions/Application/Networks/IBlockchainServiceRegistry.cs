using Skinora.Shared.Enums;
using Skinora.Transactions.Application.GasFee;
using Skinora.Transactions.Application.PaymentAddresses;
using Skinora.Transactions.Application.Transfers;

namespace Skinora.Transactions.Application.Networks;

/// <summary>
/// The backend's one way to reach a network's blockchain service (T148 —
/// 05 §3.3a "Servisler", 08 §3.0 "Ağ başına servis"). Every network has its own
/// service — its own container, base URL, internal key and seed (11 §F8 K7) —
/// and the backend resolves the service from the network of the record it is
/// acting on (<c>PaymentAddress.Network</c>, <c>BlockchainTransaction.Network</c>,
/// <c>Transaction.Network</c>). No request is sent without a network.
/// </summary>
/// <remarks>
/// <para>
/// <b>Fail closed, one network at a time.</b> A network whose service is not
/// configured (its <c>BaseUrl</c> is empty — Ethereum until T150) still
/// resolves, to a client that never sends anything and answers every call with
/// the "not configured / unavailable" outcome of its port. Callers already
/// treat that outcome as "do not move money, retry later"; the other network's
/// clients are untouched by it.
/// </para>
/// <para>
/// <b>Same-network rule (11 §F8 K3, 05 §3.3a "Aynı ağ kuralı").</b> A payout or
/// refund is sent to the service of the transaction's own network; the
/// registry has no "default" network to fall back to.
/// </para>
/// </remarks>
public interface IBlockchainServiceRegistry
{
    /// <summary>Networks whose service has a base URL, in enum order.</summary>
    IReadOnlyList<BlockchainNetwork> ConfiguredNetworks { get; }

    /// <summary>True when <paramref name="network"/>'s service has a base URL.</summary>
    bool IsConfigured(BlockchainNetwork network);

    /// <summary>Wallet, monitor, balance and cold-transfer calls of <paramref name="network"/>'s service.</summary>
    IBlockchainSidecarClient Sidecar(BlockchainNetwork network);

    /// <summary>Payout / refund / sweep broadcasts and status reads of <paramref name="network"/>'s service.</summary>
    IBlockchainTransferClient Transfers(BlockchainNetwork network);

    /// <summary>Pre-send fee estimates of <paramref name="network"/>'s service.</summary>
    IGasFeeEstimator GasFee(BlockchainNetwork network);
}
