using MediatR;
using Microsoft.Extensions.Logging;
using Skinora.Shared.Events;
using Skinora.Transactions.Application.PaymentAddresses;

#pragma warning disable IDE0005 // MediatR is added explicitly to keep the
// notification handler in the Transactions
// module (T139 dispatcher).

namespace Skinora.Transactions.Application.PaymentMonitoring;

/// <summary>
/// MediatR notification handler that consumes
/// <see cref="PaymentMonitorStartRequestedEvent"/> from the outbox and asks
/// the blockchain sidecar to begin active payment monitoring (T139 — T71
/// endpoint, 08 §3.4). Twin of
/// <see cref="PostCancel.PostCancelMonitorStartDispatcher"/>.
/// </summary>
/// <remarks>
/// A transport failure (sidecar down, 5xx, timeout) re-throws so the outbox
/// dispatcher marks the row FAILED and retries it later. A 400 (bad request)
/// is logged and treated as terminal — re-sending the same payload will keep
/// failing. Either way the per-minute
/// <see cref="EnsurePaymentMonitorJob"/> re-arm sweep is the backstop: this
/// dispatcher is the fast path, not the only path, so a permanently lost
/// event costs at most a minute of polling latency rather than the whole
/// payment leg.
/// </remarks>
public sealed class PaymentMonitorStartDispatcher
    : INotificationHandler<PaymentMonitorStartRequestedEvent>
{
    private readonly IBlockchainSidecarClient _sidecar;
    private readonly ILogger<PaymentMonitorStartDispatcher> _logger;

    public PaymentMonitorStartDispatcher(
        IBlockchainSidecarClient sidecar,
        ILogger<PaymentMonitorStartDispatcher> logger)
    {
        _sidecar = sidecar;
        _logger = logger;
    }

    public async Task Handle(
        PaymentMonitorStartRequestedEvent notification,
        CancellationToken cancellationToken)
    {
        var request = new PaymentMonitorStartRequest(
            Address: notification.Address,
            PaymentAddressId: notification.PaymentAddressId,
            TransactionId: notification.TransactionId,
            ExpectedContract: notification.ExpectedContractAddress,
            ExpectedSymbol: notification.ExpectedToken.ToString());

        var status = await _sidecar.StartMonitoringAsync(request, cancellationToken);
        switch (status)
        {
            case BlockchainSidecarStatus.Success:
                _logger.LogInformation(
                    "Payment monitor armed with sidecar: address={Address} transactionId={TransactionId}",
                    notification.Address, notification.TransactionId);
                return;
            case BlockchainSidecarStatus.InvalidRequest:
                // 400 — payload disagreement; retry will not help. Log loud and
                // let the outbox marker stop the chain. The re-arm sweep will
                // keep trying with the same payload, so a persistent 400 shows
                // up as a repeating warning rather than a silent dead window.
                _logger.LogError(
                    "Sidecar rejected payment monitor start as InvalidRequest: address={Address} transactionId={TransactionId}",
                    notification.Address, notification.TransactionId);
                return;
            default:
                throw new InvalidOperationException(
                    $"Sidecar monitor start unavailable (status={status}) for address {notification.Address}; outbox will retry.");
        }
    }
}
