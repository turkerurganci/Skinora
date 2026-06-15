using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Application.GasFee;
using Skinora.Transactions.Domain.Entities;

namespace Skinora.Transactions.Application.Transfers;

/// <summary>
/// WP2 refund leg — MediatR notification handler that consumes
/// <see cref="PaymentRefundToBuyerRequestedEvent"/> (published by the three
/// terminal-cancel paths: delivery timeout, admin-cancel AD19, and
/// emergency-hold-release-cancel AD19c) and queues a PENDING
/// <c>BUYER_REFUND</c> <see cref="BlockchainTransaction"/> row. The existing
/// <see cref="OutgoingTransferDispatchJob"/> broadcasts the row and
/// <see cref="OutgoingTransferConfirmationJob"/> drives it to CONFIRMED; no
/// transaction-state transition is needed because the transaction is already
/// in a terminal CANCELLED_* state at publish time (there is no REFUNDED
/// status, and the confirmation job announces only SELLER_PAYOUT).
/// </summary>
/// <remarks>
/// <para>
/// Refund amount (02 §4.6 / §4.7): the buyer receives <c>Price + Commission −
/// gas fee</c>, i.e. <c>TotalAmount − refundGasFeeEstimate</c> — the buyer
/// bears the on-chain gas, the platform's cost is zero. The gas estimate is
/// snapshotted onto <see cref="BlockchainTransaction.GasFee"/> so the 07 §7.5
/// refund breakdown is reconstructable from stored data (originalAmount =
/// Amount + GasFee). When <see cref="IRefundDecisionService"/> blocks the
/// refund (negative net, or below the dust threshold) no row is queued and a
/// <see cref="RefundBlockedAdminAlertEvent"/> is raised instead — identical to
/// the webhook refund paths in <c>AmountValidationService</c>.
/// </para>
/// <para>
/// Idempotency is layered (WP1 F1 money-safety pattern), because the outbox is
/// at-least-once and <see cref="BlockchainTransaction"/> carries no concurrency
/// token: (1) an <c>AnyAsync</c> existence guard short-circuits redelivery;
/// (2) the filtered unique index
/// <c>UQ_BlockchainTransactions_BuyerRefund_TransactionId</c> rejects a
/// concurrent second insert at the database; (3) a
/// <c>catch(DbUpdateException)</c> detaches the rejected row and re-queries —
/// swallowing as an idempotent no-op when the row now exists, re-throwing any
/// unrelated failure unchanged. At most one BUYER_REFUND row per transaction is
/// legitimate: all three publish sites are terminal transitions and a
/// transaction is cancelled exactly once.
/// </para>
/// </remarks>
public sealed class PaymentRefundToBuyerConsumer
    : INotificationHandler<PaymentRefundToBuyerRequestedEvent>
{
    private readonly AppDbContext _db;
    private readonly IGasFeeSettingsProvider _gasFeeSettings;
    private readonly IRefundDecisionService _refundDecision;
    private readonly IRefundBlockedAlertService _refundBlockedAlert;
    private readonly TimeProvider _clock;
    private readonly ILogger<PaymentRefundToBuyerConsumer> _logger;

    public PaymentRefundToBuyerConsumer(
        AppDbContext db,
        IGasFeeSettingsProvider gasFeeSettings,
        IRefundDecisionService refundDecision,
        IRefundBlockedAlertService refundBlockedAlert,
        TimeProvider clock,
        ILogger<PaymentRefundToBuyerConsumer> logger)
    {
        _db = db;
        _gasFeeSettings = gasFeeSettings;
        _refundDecision = refundDecision;
        _refundBlockedAlert = refundBlockedAlert;
        _clock = clock;
        _logger = logger;
    }

    public async Task Handle(
        PaymentRefundToBuyerRequestedEvent notification, CancellationToken cancellationToken)
    {
        var transaction = await _db.Set<Transaction>()
            .FirstOrDefaultAsync(t => t.Id == notification.TransactionId, cancellationToken);
        if (transaction is null)
        {
            _logger.LogWarning(
                "BuyerRefund: transaction {TransactionId} not found — cannot queue refund.",
                notification.TransactionId);
            return;
        }

        // Idempotency layer 1 — never queue a second BUYER_REFUND for the same
        // transaction. A redelivered event finds the row and no-ops.
        var alreadyQueued = await _db.Set<BlockchainTransaction>()
            .AsNoTracking()
            .AnyAsync(
                b => b.TransactionId == transaction.Id
                    && b.Type == BlockchainTransactionType.BUYER_REFUND,
                cancellationToken);
        if (alreadyQueued)
        {
            _logger.LogInformation(
                "BuyerRefund: transaction {TransactionId} already has a BUYER_REFUND row — idempotent skip.",
                transaction.Id);
            return;
        }

        var gasFee = (await _gasFeeSettings.GetAsync(cancellationToken)).RefundGasFeeEstimateUsdt;

        // 02 §4.6 — buyer receives Price + Commission − gas fee = TotalAmount −
        // gasFee. ResolveBuyerRefundAsync applies the negative / dust-threshold
        // guard (09 §14.4); a blocked decision raises an admin alert and queues
        // no row, mirroring the webhook refund paths (AmountValidationService).
        var decision = await _refundDecision.ResolveBuyerRefundAsync(
            transaction.TotalAmount, gasFee, cancellationToken);
        if (decision.Outcome == RefundOutcome.Block)
        {
            // RaiseAsync only Adds the audit + outbox rows to the shared context
            // (mirrors OutboxService — caller commits), so own the SaveChanges.
            await _refundBlockedAlert.RaiseAsync(transaction.Id, decision, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogWarning(
                "BuyerRefund blocked — transaction {TransactionId} totalPaid={TotalPaid} gasFee={GasFee} reason={Reason}; admin alerted, no row queued.",
                transaction.Id, transaction.TotalAmount, gasFee, decision.Reason);
            return;
        }

        var refundRow = new BlockchainTransaction
        {
            Id = Guid.NewGuid(),
            TransactionId = transaction.Id,
            PaymentAddressId = null,           // CK_..._Type_Outbound: NULL for BUYER_REFUND.
            Type = BlockchainTransactionType.BUYER_REFUND,
            TxHash = null,
            FromAddress = string.Empty,        // Hot-wallet address set at broadcast time (T73).
            ToAddress = notification.BuyerRefundAddress,
            Amount = decision.NetRefund,       // Net (TotalAmount − gasFee), 02 §4.6.
            Token = transaction.StablecoinType,
            ActualTokenAddress = null,         // CK_..._Type_Outbound: NULL for BUYER_REFUND.
            GasFee = gasFee,                   // Snapshot the estimate (07 §7.5 reconstruction).
            Status = BlockchainTransactionStatus.PENDING,
            BlockNumber = null,
            ConfirmationCount = 0,
            RetryCount = 0,
            NextAttemptAt = null,              // Eligible for dispatch immediately.
            CreatedAt = _clock.GetUtcNow().UtcDateTime,
        };

        _db.Set<BlockchainTransaction>().Add(refundRow);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Money-safety backstop (WP1 F1). A concurrent redelivery slipped
            // past the AnyAsync guard and inserted the BUYER_REFUND row first;
            // the filtered unique index rejected this one. Detach the rejected
            // row so the scope's DbContext stays clean, then confirm a
            // BUYER_REFUND row now exists before swallowing — any unrelated
            // failure re-throws unchanged.
            _db.Entry(refundRow).State = EntityState.Detached;

            var nowQueued = await _db.Set<BlockchainTransaction>()
                .AsNoTracking()
                .AnyAsync(
                    b => b.TransactionId == transaction.Id
                        && b.Type == BlockchainTransactionType.BUYER_REFUND,
                    cancellationToken);
            if (!nowQueued) throw;

            _logger.LogWarning(
                "BuyerRefund: concurrent insert race for transaction {TransactionId} — refund already queued by another delivery; skipping (idempotent).",
                transaction.Id);
            return;
        }

        _logger.LogInformation(
            "BuyerRefund queued — transaction {TransactionId} refund row {RowId} amount {Amount} {Token} (gasFee {GasFee}).",
            transaction.Id, refundRow.Id, decision.NetRefund, transaction.StablecoinType, gasFee);
    }
}
