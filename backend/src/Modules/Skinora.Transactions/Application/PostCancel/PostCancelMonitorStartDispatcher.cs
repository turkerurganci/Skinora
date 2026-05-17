using MediatR;
using Microsoft.Extensions.Logging;
using Skinora.Shared.Events;
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
    private readonly IBlockchainSidecarClient _sidecar;
    private readonly ILogger<PostCancelMonitorStartDispatcher> _logger;

    public PostCancelMonitorStartDispatcher(
        IBlockchainSidecarClient sidecar,
        ILogger<PostCancelMonitorStartDispatcher> logger)
    {
        _sidecar = sidecar;
        _logger = logger;
    }

    public async Task Handle(
        PostCancelMonitorStartRequestedEvent notification,
        CancellationToken cancellationToken)
    {
        var request = new PostCancelMonitorStartRequest(
            Address: notification.Address,
            PaymentAddressId: notification.PaymentAddressId,
            TransactionId: notification.TransactionId,
            ExpectedContract: notification.ExpectedContractAddress,
            ExpectedSymbol: notification.ExpectedToken.ToString(),
            CancelledAt: notification.CancelledAt);

        var status = await _sidecar.StartPostCancelMonitoringAsync(request, cancellationToken);
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
