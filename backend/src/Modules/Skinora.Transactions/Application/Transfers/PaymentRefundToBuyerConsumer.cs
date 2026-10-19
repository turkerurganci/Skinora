using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Application.GasFee;
using Skinora.Transactions.Domain.Calculations;
using Skinora.Transactions.Domain.Entities;

namespace Skinora.Transactions.Application.Transfers;

/// <summary>
/// WP2 refund leg — MediatR notification handler that consumes
/// <see cref="PaymentRefundToBuyerRequestedEvent"/> (published by the terminal
/// unwind paths: delivery timeout, admin-cancel AD19,
/// emergency-hold-release-cancel AD19c, buyer-favour dispute resolution AD29,
/// and settlement reversal per 02 §4.5.1) and queues a PENDING
/// <c>BUYER_REFUND</c> <see cref="BlockchainTransaction"/> row. The existing
/// <see cref="OutgoingTransferDispatchJob"/> broadcasts the row and
/// <see cref="OutgoingTransferConfirmationJob"/> drives it to CONFIRMED; no
/// transaction-state transition is needed because the transaction is already in
/// a terminal state at publish time — <c>CANCELLED_*</c> on the cancel paths,
/// <c>REFUNDED</c> on the dispute-resolution and settlement-reversal paths — and
/// the confirmation job announces only SELLER_PAYOUT.
/// </summary>
/// <remarks>
/// <para>
/// Refund amount (02 §4.6 / §4.7): the buyer receives <c>Price + Commission −
/// gas fee</c>, i.e. <c>TotalAmount − refundGasFeeEstimate</c> — the buyer
/// bears the on-chain gas, the platform's cost is zero. T149: on TRON
/// <c>TotalAmount</c> also carries the network fee line, returned in full
/// unless the deposit has already been swept
/// (<c>FinancialCalculator.CalculateRefundableTotal</c>). The gas estimate is
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
/// legitimate: every publish site is a terminal transition and a transaction is
/// unwound exactly once.
/// </para>
/// </remarks>
public sealed class PaymentRefundToBuyerConsumer
    : INotificationHandler<PaymentRefundToBuyerRequestedEvent>
{
    private readonly AppDbContext _db;
    private readonly IChargedGasFeeResolver _chargedGasFee;
    private readonly IRefundDecisionService _refundDecision;
    private readonly IRefundBlockedAlertService _refundBlockedAlert;
    private readonly TimeProvider _clock;
    private readonly ILogger<PaymentRefundToBuyerConsumer> _logger;

    public PaymentRefundToBuyerConsumer(
        AppDbContext db,
        IChargedGasFeeResolver chargedGasFee,
        IRefundDecisionService refundDecision,
        IRefundBlockedAlertService refundBlockedAlert,
        TimeProvider clock,
        ILogger<PaymentRefundToBuyerConsumer> logger)
    {
        _db = db;
        _chargedGasFee = chargedGasFee;
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

        // Runtime pre-send estimate of the actual refund transfer (deposit →
        // buyer); the static refund setting only as fallback
        // (Prova-GasFeeChargedIsFixedGuess — owner decision 2026-09-02). The
        // deposit address is the refund's sender (RefundService broadcasts
        // deposit-sourced); without a PaymentAddress row there is no sender to
        // price and the resolver charges the static setting instead — a
        // hot-wallet estimate would price another account's resources.
        var deposit = await _db.Set<PaymentAddress>()
            .AsNoTracking()
            .Where(p => p.TransactionId == transaction.Id)
            .Select(p => new { p.Address, p.Network })
            .FirstOrDefaultAsync(cancellationToken);
        var depositAddress = deposit?.Address;

        // T148 — same-network rule (11 §F8 K3): the refund leaves on the
        // network the money arrived on. The deposit's network is that fact;
        // without a deposit row the transaction's own network (chosen at
        // acceptance, T147 — set on every accepted row by
        // CK_Transactions_Network_Accepted) stands in; the TRON fallback is
        // only a defensive read for rows that predate F8 (06 §1.4).
        var network = deposit?.Network ?? transaction.Network ?? BlockchainNetwork.TRON;

        // T149 (11 §F8 K8, 02 §4.6, 08 §3.1a) — what the refund returns before
        // its own gas: the whole TotalAmount (price + commission + the TRON
        // network fee line) while the deposit has not been swept — the line
        // paid for a sweep that never happened; once the sweep has run
        // (broadcast or confirmed) the line was spent on it and is not
        // returned (öneriyle kaydedildi — ⚠ PARA). Price and commission are
        // returned in full either way.
        var sweepHasRun = await _db.Set<BlockchainTransaction>()
            .AsNoTracking()
            .AnyAsync(
                b => b.TransactionId == transaction.Id
                    && b.Type == BlockchainTransactionType.SWEEP
                    && (b.Status == BlockchainTransactionStatus.CONFIRMED
                        || b.Status == BlockchainTransactionStatus.DETECTED
                        || (b.Status == BlockchainTransactionStatus.PENDING && b.TxHash != null)),
                cancellationToken);
        var refundable = FinancialCalculator.CalculateRefundableTotal(
            transaction.TotalAmount, transaction.NetworkFeeAmount, sweepHasRun);

        // T158 (f) — after the sweep (or while it is in flight) an Ethereum
        // refund leaves the hot wallet, so that is the sender priced; the
        // dispatcher asks RefundSourceResolver the same question when it sends.
        //
        // ⚠ PARA — the one case the two answers differ (T158 doğrulaması,
        // yönetici kararı): a refund queued while the sweep is in flight
        // (WaitForSweep) is priced HERE with the hot-wallet fee, and the row
        // waits. If that sweep then FAILS, the dispatcher sends from the
        // deposit instead — a fuel-sponsored send that costs more than the
        // hot-wallet one. The buyer still receives exactly this row's Amount;
        // the gas difference is absorbed by the platform (the fuel wallet pays
        // it). Deliberately not re-priced at dispatch: the dispatcher runs on
        // every attempt, so a re-price there would also rewrite the amount of
        // a row the service may already have signed (T152: one request id, one
        // signed transaction — a timed-out send stays PENDING), a money-amount
        // mutation on a possibly-attempted row. It happens only when a sweep
        // fails — a platform-side failure, rare — and the platform bears what
        // its own failure costs.
        var source = await RefundSourceResolver.ResolveAsync(
            _db, network, BlockchainTransactionType.BUYER_REFUND, transaction.Id, cancellationToken);
        var resolvedGasFee = source == RefundSource.Deposit
            ? await _chargedGasFee.ResolveRefundFeeAsync(
                network,
                depositAddress,
                notification.BuyerRefundAddress,
                refundable,
                transaction.StablecoinType,
                cancellationToken)
            : await _chargedGasFee.ResolveHotWalletRefundFeeAsync(
                network,
                notification.BuyerRefundAddress,
                refundable,
                transaction.StablecoinType,
                cancellationToken);
        var gasFee = resolvedGasFee.FeeUsdt;

        // 02 §4.6 — buyer receives Price + Commission (+ the network fee line
        // before the sweep) − gas fee. ResolveBuyerRefundAsync applies the
        // negative / dust-threshold guard with the network's own multiplier
        // (09 §14.4, T149); a blocked decision raises an admin alert and queues
        // no row, mirroring the webhook refund paths (AmountValidationService).
        var decision = await _refundDecision.ResolveBuyerRefundAsync(
            network, refundable, gasFee, cancellationToken);
        if (decision.Outcome == RefundOutcome.Block)
        {
            // RaiseAsync only Adds the audit + outbox rows to the shared context
            // (mirrors OutboxService — caller commits), so own the SaveChanges.
            await _refundBlockedAlert.RaiseAsync(transaction.Id, decision, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogWarning(
                "BuyerRefund blocked — transaction {TransactionId} refundable={Refundable} (total {TotalPaid}, network fee {NetworkFee}, swept {Swept}) gasFee={GasFee} reason={Reason}; admin alerted, no row queued.",
                transaction.Id, refundable, transaction.TotalAmount, transaction.NetworkFeeAmount, sweepHasRun, gasFee, decision.Reason);
            return;
        }

        var refundRow = new BlockchainTransaction
        {
            Id = Guid.NewGuid(),
            TransactionId = transaction.Id,
            Network = network,
            PaymentAddressId = null,           // CK_..._Type_Outbound: NULL for BUYER_REFUND.
            Type = BlockchainTransactionType.BUYER_REFUND,
            TxHash = null,
            FromAddress = string.Empty,        // Deposit address set at dispatch (OutgoingTransferDispatchJob).
            ToAddress = notification.BuyerRefundAddress,
            Amount = decision.NetRefund,       // Net (refundable − gasFee), 02 §4.6 / T149.
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
