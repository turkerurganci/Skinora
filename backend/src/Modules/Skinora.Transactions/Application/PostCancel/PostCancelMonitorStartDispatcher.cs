using MediatR;
using Microsoft.Extensions.Logging;
using Skinora.Shared.Events;
using Skinora.Transactions.Application.Networks;
using Skinora.Transactions.Application.PaymentAddresses;

#pragma warning disable IDE0005 // MediatR is added explicitly to keep the
// notification handler in the Transactions
// module (T75 dispatcher).

namespace Skinora.Transactions.Application.PostCancel;

/// <summary>
/// MediatR notification handler that consumes
/// <see cref="PostCancelMonitorStartRequestedEvent"/> from the outbox and
/// asks the blockchain sidecar to begin post-cancel monitoring (T75 —
/// 08 §3.4). Mirrors the dispatcher style used by T73 transfer events.
/// </summary>
/// <remarks>
/// A transport failure (sidecar down, 5xx, timeout) re-throws so the
/// outbox dispatcher marks the row FAILED and retries it later. A 400
/// (bad request) is logged and treated as terminal — re-sending the same
/// payload will keep failing. Idempotency on the sidecar side means a
/// duplicate Success delivery is harmless.
/// </remarks>
public sealed class PostCancelMonitorStartDispatcher
    : INotificationHandler<PostCancelMonitorStartRequestedEvent>
{
    private readonly IBlockchainServiceRegistry _services;
    private readonly ILogger<PostCancelMonitorStartDispatcher> _logger;

    public PostCancelMonitorStartDispatcher(
        IBlockchainServiceRegistry services,
        ILogger<PostCancelMonitorStartDispatcher> logger)
    {
        _services = services;
        _logger = logger;
    }

    public async Task Handle(
        PostCancelMonitorStartRequestedEvent notification,
        CancellationToken cancellationToken)
    {
        // T148 düzeltme turu 1 (F1) — a network with no service configured
        // cannot answer, and a configuration change needs a restart. Throwing
        // here would leave the outbox row FAILED and re-read at the head of the
        // oldest-first outbox batch on every poll, for good — enough of them
        // would stall every other event. Log and stop instead: the address is
        // already in a POST_CANCEL_* status, so PostCancelMonitorRecoveryHook
        // registers it on the restart that configures the service.
        if (!_services.IsConfigured(notification.Network))
        {
            _logger.LogError(
                "Post-cancel monitor start not sent: {Network} has no blockchain service configured — address={Address} transactionId={TransactionId}; the startup recovery hook registers it once the service is configured.",
                notification.Network, notification.Address, notification.TransactionId);
            return;
        }

        // T139 — hand the address over, do not double-register it. Until T139
        // nothing ever armed the active monitor, so there was nothing to stop.
        // Now the cancel path must drop the 3-second monitor before the
        // gradual-cadence one takes the same address, otherwise two registries
        // poll it and each emits its own webhook. (The backend's
        // (TxHash, EventIndex) UNIQUE index absorbs the duplicate credit, so
        // this is a quota-and-noise defect rather than a money defect — but the
        // row has already left MonitoringStatus.ACTIVE by the time this event
        // is published, so EnsurePaymentMonitorJob will never see the stale
        // monitor and no other mechanism would clean it up.)
        //
        // A failed stop is logged, not thrown: the post-cancel registration
        // below carries the money-recovery guarantee (08 §3.4) and must not be
        // skipped for it. Transport failures are covered anyway — the same
        // sidecar is about to be asked to start post-cancel monitoring, and
        // that call throws, so the outbox retries the pair together.
        // T148 — both halves of the handover go to the address's own
        // network's service (05 §3.3a).
        var sidecar = _services.Sidecar(notification.Network);
        var stopStatus = await sidecar.StopMonitoringAsync(
            notification.Address, cancellationToken);
        if (stopStatus != BlockchainSidecarStatus.Success)
        {
            _logger.LogWarning(
                "Could not stop the active payment monitor before post-cancel handover "
                + "(status={Status}): address={Address} transactionId={TransactionId}",
                stopStatus, notification.Address, notification.TransactionId);
        }

        var request = new PostCancelMonitorStartRequest(
            Address: notification.Address,
            PaymentAddressId: notification.PaymentAddressId,
            TransactionId: notification.TransactionId,
            ExpectedContract: notification.ExpectedContractAddress,
            ExpectedSymbol: notification.ExpectedToken.ToString(),
            CancelledAt: notification.CancelledAt,
            // T158 (h) — the persisted start block (Ethereum).
            WatchFromBlock: notification.WatchFromBlock);

        var status = await sidecar.StartPostCancelMonitoringAsync(request, cancellationToken);
        switch (status)
        {
            case BlockchainSidecarStatus.Success:
                _logger.LogInformation(
                    "Post-cancel monitor registered with sidecar: address={Address} transactionId={TransactionId}",
                    notification.Address, notification.TransactionId);
                return;
            case BlockchainSidecarStatus.InvalidRequest:
                // 400 — payload disagreement; retry will not help. Log loud and
                // let the outbox marker stop the chain.
                _logger.LogError(
                    "Sidecar rejected post-cancel start as InvalidRequest: address={Address} transactionId={TransactionId}",
                    notification.Address, notification.TransactionId);
                return;
            default:
                throw new InvalidOperationException(
                    $"Sidecar post-cancel start unavailable (status={status}) for address {notification.Address}; outbox will retry.");
        }
    }
}
