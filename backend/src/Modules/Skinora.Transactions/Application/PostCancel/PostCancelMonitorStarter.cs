using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Interfaces;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Application.Webhooks;
using Skinora.Transactions.Domain.Entities;

namespace Skinora.Transactions.Application.PostCancel;

/// <inheritdoc cref="IPostCancelMonitorStarter"/>
public sealed class PostCancelMonitorStarter : IPostCancelMonitorStarter
{
    /// <summary>
    /// Initial post-cancel window — 24 hours per 08 §3.4. Public so tests
    /// can assert the stamped <c>MonitoringExpiresAt</c> without mirroring
    /// the constant.
    /// </summary>
    public static readonly TimeSpan InitialWindow = TimeSpan.FromHours(24);

    private readonly AppDbContext _db;
    private readonly IOutboxService _outbox;
    private readonly ILogger<PostCancelMonitorStarter> _logger;

    public PostCancelMonitorStarter(
        AppDbContext db,
        IOutboxService outbox,
        ILogger<PostCancelMonitorStarter> logger)
    {
        _db = db;
        _outbox = outbox;
        _logger = logger;
    }

    public async Task RequestStartAsync(
        Guid transactionId,
        DateTime cancelledAt,
        CancellationToken cancellationToken)
    {
        // PaymentAddress allocation is a side effect of transaction creation
        // (T70/T123: inline at CREATED, swept by EnsurePaymentAddressJob, and
        // deferred to admin approval for FLAGGED rows). A missing row therefore
        // means the inline allocation failed and the sweep had not caught up,
        // or the transaction was flagged at creation and never approved —
        // either way there is nothing to monitor, so bail silently. The cancel
        // handler does not need to know.
        var paymentAddress = await _db.Set<PaymentAddress>()
            .FirstOrDefaultAsync(p => p.TransactionId == transactionId && !p.IsDeleted, cancellationToken);
        if (paymentAddress is null)
        {
            _logger.LogDebug(
                "PostCancelMonitorStarter: no PaymentAddress for transaction {TransactionId} — nothing to monitor.",
                transactionId);
            return;
        }

        // Idempotency — re-cancel (admin cancel after timeout-cancel) hits an
        // address already inside a POST_CANCEL_* window. Keep the existing
        // anchor so window math remains stable.
        if (paymentAddress.MonitoringStatus is MonitoringStatus.POST_CANCEL_24H
            or MonitoringStatus.POST_CANCEL_7D
            or MonitoringStatus.POST_CANCEL_30D
            or MonitoringStatus.STOPPED)
        {
            _logger.LogDebug(
                "PostCancelMonitorStarter: PaymentAddress {PaymentAddressId} already in {State} — no-op.",
                paymentAddress.Id, paymentAddress.MonitoringStatus);
            return;
        }

        paymentAddress.MonitoringStatus = MonitoringStatus.POST_CANCEL_24H;
        paymentAddress.MonitoringExpiresAt = cancelledAt + InitialWindow;

        var contract = KnownStablecoinContracts.ResolveContractAddress(paymentAddress.ExpectedToken);

        await _outbox.PublishAsync(
            new PostCancelMonitorStartRequestedEvent(
                EventId: Guid.NewGuid(),
                TransactionId: transactionId,
                PaymentAddressId: paymentAddress.Id,
                Address: paymentAddress.Address,
                ExpectedToken: paymentAddress.ExpectedToken,
                ExpectedContractAddress: contract,
                CancelledAt: cancelledAt,
                OccurredAt: cancelledAt),
            cancellationToken);

        _logger.LogInformation(
            "PostCancelMonitorStarter: stamped PaymentAddress {PaymentAddressId} → POST_CANCEL_24H (expires {ExpiresAt}) for transaction {TransactionId}",
            paymentAddress.Id, paymentAddress.MonitoringExpiresAt, transactionId);
    }
}
