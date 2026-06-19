using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Skinora.Shared.Domain.Seed;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Exceptions;
using Skinora.Shared.Interfaces;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Application.GasFee;
using Skinora.Transactions.Application.History;
using Skinora.Transactions.Domain.Entities;
using Skinora.Transactions.Domain.StateMachine;

namespace Skinora.Transactions.Application.Webhooks;

/// <inheritdoc cref="IAmountValidationService"/>
public sealed class AmountValidationService : IAmountValidationService
{
    private readonly AppDbContext _db;
    private readonly IGasFeeSettingsProvider _gasFeeSettings;
    private readonly IRefundDecisionService _refundDecision;
    private readonly IRefundBlockedAlertService _refundBlockedAlert;
    private readonly IOutboxService _outbox;
    private readonly TimeProvider _clock;
    private readonly ILogger<AmountValidationService> _logger;

    public AmountValidationService(
        AppDbContext db,
        IGasFeeSettingsProvider gasFeeSettings,
        IRefundDecisionService refundDecision,
        IRefundBlockedAlertService refundBlockedAlert,
        IOutboxService outbox,
        TimeProvider clock,
        ILogger<AmountValidationService> logger)
    {
        _db = db;
        _gasFeeSettings = gasFeeSettings;
        _refundDecision = refundDecision;
        _refundBlockedAlert = refundBlockedAlert;
        _outbox = outbox;
        _clock = clock;
        _logger = logger;
    }

    public async Task<AmountValidationOutcome> ValidateConfirmedBuyerPaymentAsync(
        BlockchainTransaction confirmedPayment,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (confirmedPayment.Type != BlockchainTransactionType.BUYER_PAYMENT)
        {
            throw new InvalidOperationException(
                $"AmountValidationService.ValidateConfirmedBuyerPaymentAsync expects BUYER_PAYMENT; got {confirmedPayment.Type}.");
        }

        var paymentAddress = await LoadPaymentAddressAsync(confirmedPayment.PaymentAddressId, cancellationToken);
        if (paymentAddress is null)
        {
            _logger.LogWarning(
                "Amount validation skipped — payment address {PaymentAddressId} missing for BlockchainTransaction {Id}. correlationId={CorrelationId}",
                confirmedPayment.PaymentAddressId, confirmedPayment.Id, correlationId);
            return AmountValidationOutcome.MissingNavigation;
        }

        var transaction = await _db.Set<Transaction>()
            .FirstOrDefaultAsync(t => t.Id == confirmedPayment.TransactionId, cancellationToken);
        if (transaction is null)
        {
            _logger.LogWarning(
                "Amount validation skipped — transaction {TransactionId} missing for BlockchainTransaction {Id}. correlationId={CorrelationId}",
                confirmedPayment.TransactionId, confirmedPayment.Id, correlationId);
            return AmountValidationOutcome.MissingNavigation;
        }

        var settings = await _gasFeeSettings.GetAsync(cancellationToken);
        var gasFee = settings.RefundGasFeeEstimateUsdt;
        var expected = paymentAddress.ExpectedAmount;
        var received = confirmedPayment.Amount;

        // 02 §4.4 + 08 §3.4 tutar doğrulama tablosu — strict equality (no tolerance, 06 §8.3).
        // Branch order: multi-payment is detected by Transaction.Status (state already past
        // ITEM_ESCROWED) regardless of equality, so it must precede the exact/over/under split.
        if (transaction.Status != TransactionStatus.ITEM_ESCROWED)
        {
            return await HandleMultiPaymentAsync(
                confirmedPayment,
                paymentAddress,
                transaction,
                gasFee,
                correlationId,
                cancellationToken);
        }

        // ITEM_ESCROWED — the standard payment window. Even here the state machine can
        // reject ConfirmPayment if the transaction is on emergency hold; that fan-out
        // is handled by the AdvanceStateMachine helper.
        if (received == expected)
        {
            var advanced = await AdvanceStateMachineAsync(transaction, confirmedPayment, correlationId, cancellationToken);
            return advanced ? AmountValidationOutcome.AcceptedExact : AmountValidationOutcome.StateMachineRejected;
        }

        if (received > expected)
        {
            return await HandleOverpaymentAsync(
                confirmedPayment,
                paymentAddress,
                transaction,
                gasFee,
                correlationId,
                cancellationToken);
        }

        return await HandleUnderpaymentAsync(
            confirmedPayment,
            paymentAddress,
            transaction,
            gasFee,
            correlationId,
            cancellationToken);
    }

    public async Task<AmountValidationOutcome> ValidateWrongTokenIncomingAsync(
        BlockchainTransaction wrongTokenIncoming,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (wrongTokenIncoming.Type != BlockchainTransactionType.WRONG_TOKEN_INCOMING)
        {
            throw new InvalidOperationException(
                $"AmountValidationService.ValidateWrongTokenIncomingAsync expects WRONG_TOKEN_INCOMING; got {wrongTokenIncoming.Type}.");
        }

        var paymentAddress = await LoadPaymentAddressAsync(wrongTokenIncoming.PaymentAddressId, cancellationToken);
        if (paymentAddress is null)
        {
            _logger.LogWarning(
                "Wrong-token validation skipped — payment address {PaymentAddressId} missing. correlationId={CorrelationId}",
                wrongTokenIncoming.PaymentAddressId, correlationId);
            return AmountValidationOutcome.MissingNavigation;
        }

        var transaction = await _db.Set<Transaction>()
            .FirstOrDefaultAsync(t => t.Id == wrongTokenIncoming.TransactionId, cancellationToken);
        if (transaction is null || transaction.BuyerId is null)
        {
            _logger.LogWarning(
                "Wrong-token validation skipped — transaction {TransactionId} or buyer missing. correlationId={CorrelationId}",
                wrongTokenIncoming.TransactionId, correlationId);
            return AmountValidationOutcome.MissingNavigation;
        }

        var settings = await _gasFeeSettings.GetAsync(cancellationToken);
        var gasFee = settings.RefundGasFeeEstimateUsdt;
        var received = wrongTokenIncoming.Amount;

        var decision = await _refundDecision.ResolveBuyerRefundAsync(received, gasFee, cancellationToken);
        if (decision.Outcome == RefundOutcome.Block)
        {
            await _refundBlockedAlert.RaiseAsync(transaction.Id, decision, cancellationToken);
            _logger.LogWarning(
                "Wrong-token refund blocked — txHash={TxHash} received={Received} reason={Reason} correlationId={CorrelationId}",
                wrongTokenIncoming.TxHash, received, decision.Reason, correlationId);
            return AmountValidationOutcome.WrongTokenAdminAlert;
        }

        var actualToken = ResolveStablecoinByContract(wrongTokenIncoming.ActualTokenAddress)
            ?? StablecoinType.USDT; // Defensive fallback — sidecar already filtered allowlist.

        var refundRow = QueueRefundIntent(
            transaction.Id,
            BlockchainTransactionType.WRONG_TOKEN_REFUND,
            wrongTokenIncoming.FromAddress,
            received,
            // 06 §3.8 token semantiği — Token = expected for wrong-token rows;
            // ActualTokenAddress carries the wrong contract.
            paymentAddress.ExpectedToken,
            actualTokenAddress: wrongTokenIncoming.ActualTokenAddress);

        await _outbox.PublishAsync(
            new WrongTokenRefundRequestedEvent(
                EventId: Guid.NewGuid(),
                TransactionId: transaction.Id,
                BuyerId: transaction.BuyerId.Value,
                RefundTransactionId: refundRow.Id,
                ExpectedStablecoin: paymentAddress.ExpectedToken,
                ActualStablecoin: actualToken,
                ActualContractAddress: wrongTokenIncoming.ActualTokenAddress ?? string.Empty,
                ReceivedAmount: received,
                SourceAddress: wrongTokenIncoming.FromAddress,
                TxHash: wrongTokenIncoming.TxHash ?? string.Empty,
                OccurredAt: _clock.GetUtcNow().UtcDateTime),
            cancellationToken);

        _logger.LogInformation(
            "Wrong-token refund queued — txHash={TxHash} amount={Amount} actual={Actual} expected={Expected} refundId={RefundId} correlationId={CorrelationId}",
            wrongTokenIncoming.TxHash, received, actualToken, paymentAddress.ExpectedToken, refundRow.Id, correlationId);
        return AmountValidationOutcome.WrongTokenRefundQueued;
    }

    public async Task<AmountValidationOutcome> ValidateLatePaymentDetectedAsync(
        BlockchainTransaction latePayment,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (latePayment.Type != BlockchainTransactionType.BUYER_PAYMENT)
        {
            throw new InvalidOperationException(
                $"AmountValidationService.ValidateLatePaymentDetectedAsync expects BUYER_PAYMENT; got {latePayment.Type}.");
        }

        var paymentAddress = await LoadPaymentAddressAsync(latePayment.PaymentAddressId, cancellationToken);
        if (paymentAddress is null)
        {
            _logger.LogWarning(
                "Late-payment validation skipped — payment address {PaymentAddressId} missing for BlockchainTransaction {Id}. correlationId={CorrelationId}",
                latePayment.PaymentAddressId, latePayment.Id, correlationId);
            return AmountValidationOutcome.MissingNavigation;
        }

        var transaction = await _db.Set<Transaction>()
            .FirstOrDefaultAsync(t => t.Id == latePayment.TransactionId, cancellationToken);
        if (transaction is null || transaction.BuyerId is null)
        {
            _logger.LogWarning(
                "Late-payment validation skipped — transaction {TransactionId} or buyer missing. correlationId={CorrelationId}",
                latePayment.TransactionId, correlationId);
            return AmountValidationOutcome.MissingNavigation;
        }

        var settings = await _gasFeeSettings.GetAsync(cancellationToken);
        var gasFee = settings.RefundGasFeeEstimateUsdt;
        var received = latePayment.Amount;

        // 02 §4.4 / 08 §3.4 — refund decision is the same minimum-threshold
        // rule used for underpayment / wrong-token: net < 2× gas blocks the
        // refund. The post-cancel transaction never advances; this purely
        // queues a refund intent for T73 dispatch.
        var decision = await _refundDecision.ResolveBuyerRefundAsync(received, gasFee, cancellationToken);
        if (decision.Outcome == RefundOutcome.Block)
        {
            await _refundBlockedAlert.RaiseAsync(transaction.Id, decision, cancellationToken);
            _logger.LogWarning(
                "Late-payment refund blocked — txHash={TxHash} received={Received} reason={Reason} correlationId={CorrelationId}",
                latePayment.TxHash, received, decision.Reason, correlationId);
            return AmountValidationOutcome.LatePaymentAdminAlert;
        }

        var refundRow = QueueRefundIntent(
            transaction.Id,
            BlockchainTransactionType.LATE_PAYMENT_REFUND,
            latePayment.FromAddress,
            received,
            paymentAddress.ExpectedToken);

        await _outbox.PublishAsync(
            new LatePaymentRefundRequestedEvent(
                EventId: Guid.NewGuid(),
                TransactionId: transaction.Id,
                BuyerId: transaction.BuyerId.Value,
                RefundTransactionId: refundRow.Id,
                ReceivedAmount: received,
                Stablecoin: paymentAddress.ExpectedToken,
                SourceAddress: latePayment.FromAddress,
                TxHash: latePayment.TxHash ?? string.Empty,
                MonitorState: paymentAddress.MonitoringStatus,
                OccurredAt: _clock.GetUtcNow().UtcDateTime),
            cancellationToken);

        _logger.LogInformation(
            "Late-payment refund queued — txHash={TxHash} received={Received} monitorState={State} refundId={RefundId} correlationId={CorrelationId}",
            latePayment.TxHash, received, paymentAddress.MonitoringStatus, refundRow.Id, correlationId);
        return AmountValidationOutcome.LatePaymentRefundQueued;
    }

    private async Task<AmountValidationOutcome> HandleUnderpaymentAsync(
        BlockchainTransaction confirmedPayment,
        PaymentAddress paymentAddress,
        Transaction transaction,
        decimal gasFee,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var received = confirmedPayment.Amount;
        var expected = paymentAddress.ExpectedAmount;

        var decision = await _refundDecision.ResolveBuyerRefundAsync(received, gasFee, cancellationToken);
        if (decision.Outcome == RefundOutcome.Block)
        {
            await _refundBlockedAlert.RaiseAsync(transaction.Id, decision, cancellationToken);
            _logger.LogWarning(
                "Underpayment refund blocked — txHash={TxHash} expected={Expected} received={Received} reason={Reason} correlationId={CorrelationId}",
                confirmedPayment.TxHash, expected, received, decision.Reason, correlationId);
            return AmountValidationOutcome.Underpaid;
        }

        if (transaction.BuyerId is null)
        {
            _logger.LogWarning(
                "Underpayment refund skipped — transaction {TransactionId} has no buyer (impossible at this state). correlationId={CorrelationId}",
                transaction.Id, correlationId);
            return AmountValidationOutcome.MissingNavigation;
        }

        var refundRow = QueueRefundIntent(
            transaction.Id,
            BlockchainTransactionType.INCORRECT_AMOUNT_REFUND,
            confirmedPayment.FromAddress,
            received,
            paymentAddress.ExpectedToken);

        await _outbox.PublishAsync(
            new BuyerPaymentInsufficientEvent(
                EventId: Guid.NewGuid(),
                TransactionId: transaction.Id,
                BuyerId: transaction.BuyerId.Value,
                RefundTransactionId: refundRow.Id,
                ExpectedAmount: expected,
                ReceivedAmount: received,
                Stablecoin: paymentAddress.ExpectedToken,
                SourceAddress: confirmedPayment.FromAddress,
                TxHash: confirmedPayment.TxHash ?? string.Empty,
                OccurredAt: _clock.GetUtcNow().UtcDateTime),
            cancellationToken);

        _logger.LogInformation(
            "Underpayment refund queued — txHash={TxHash} expected={Expected} received={Received} refundId={RefundId} correlationId={CorrelationId}",
            confirmedPayment.TxHash, expected, received, refundRow.Id, correlationId);
        return AmountValidationOutcome.Underpaid;
    }

    private async Task<AmountValidationOutcome> HandleOverpaymentAsync(
        BlockchainTransaction confirmedPayment,
        PaymentAddress paymentAddress,
        Transaction transaction,
        decimal gasFee,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var received = confirmedPayment.Amount;
        var expected = paymentAddress.ExpectedAmount;
        var excess = received - expected;

        // Advance the state machine BEFORE deciding refund — the platform has the
        // expected amount so the buyer has paid in full from the contract's view.
        await AdvanceStateMachineAsync(transaction, confirmedPayment, correlationId, cancellationToken);

        var decision = await _refundDecision.ResolveOverpaymentRefundAsync(expected, received, gasFee, cancellationToken);
        if (decision.Outcome == RefundOutcome.Block)
        {
            await _refundBlockedAlert.RaiseAsync(transaction.Id, decision, cancellationToken);
            _logger.LogWarning(
                "Overpayment refund blocked — txHash={TxHash} expected={Expected} received={Received} reason={Reason} correlationId={CorrelationId}",
                confirmedPayment.TxHash, expected, received, decision.Reason, correlationId);
            return AmountValidationOutcome.AcceptedWithExcessRefund;
        }

        if (transaction.BuyerId is null)
        {
            _logger.LogWarning(
                "Overpayment refund skipped — transaction {TransactionId} has no buyer. correlationId={CorrelationId}",
                transaction.Id, correlationId);
            return AmountValidationOutcome.AcceptedWithExcessRefund;
        }

        var refundRow = QueueRefundIntent(
            transaction.Id,
            BlockchainTransactionType.EXCESS_REFUND,
            confirmedPayment.FromAddress,
            excess,
            paymentAddress.ExpectedToken);

        await _outbox.PublishAsync(
            new BuyerPaymentExcessRefundedEvent(
                EventId: Guid.NewGuid(),
                TransactionId: transaction.Id,
                BuyerId: transaction.BuyerId.Value,
                RefundTransactionId: refundRow.Id,
                ExpectedAmount: expected,
                ReceivedAmount: received,
                ExcessAmount: excess,
                Stablecoin: paymentAddress.ExpectedToken,
                SourceAddress: confirmedPayment.FromAddress,
                TxHash: confirmedPayment.TxHash ?? string.Empty,
                IsMultiPayment: false,
                OccurredAt: _clock.GetUtcNow().UtcDateTime),
            cancellationToken);

        _logger.LogInformation(
            "Overpayment refund queued — txHash={TxHash} expected={Expected} received={Received} excess={Excess} refundId={RefundId} correlationId={CorrelationId}",
            confirmedPayment.TxHash, expected, received, excess, refundRow.Id, correlationId);
        return AmountValidationOutcome.AcceptedWithExcessRefund;
    }

    private async Task<AmountValidationOutcome> HandleMultiPaymentAsync(
        BlockchainTransaction confirmedPayment,
        PaymentAddress paymentAddress,
        Transaction transaction,
        decimal gasFee,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var received = confirmedPayment.Amount;

        // 02 §4.4 — payments arriving after the transaction left ITEM_ESCROWED
        // are refunded in full (the state machine never advances; multi-payment
        // is otherwise identical to underpayment from the refund-decision view).
        var decision = await _refundDecision.ResolveBuyerRefundAsync(received, gasFee, cancellationToken);
        if (decision.Outcome == RefundOutcome.Block)
        {
            await _refundBlockedAlert.RaiseAsync(transaction.Id, decision, cancellationToken);
            _logger.LogWarning(
                "Multi-payment refund blocked — txHash={TxHash} received={Received} state={State} reason={Reason} correlationId={CorrelationId}",
                confirmedPayment.TxHash, received, transaction.Status, decision.Reason, correlationId);
            return AmountValidationOutcome.MultiPaymentRefunded;
        }

        if (transaction.BuyerId is null)
        {
            _logger.LogWarning(
                "Multi-payment refund skipped — transaction {TransactionId} has no buyer. correlationId={CorrelationId}",
                transaction.Id, correlationId);
            return AmountValidationOutcome.MissingNavigation;
        }

        var refundRow = QueueRefundIntent(
            transaction.Id,
            BlockchainTransactionType.EXCESS_REFUND,
            confirmedPayment.FromAddress,
            received,
            paymentAddress.ExpectedToken);

        await _outbox.PublishAsync(
            new BuyerPaymentExcessRefundedEvent(
                EventId: Guid.NewGuid(),
                TransactionId: transaction.Id,
                BuyerId: transaction.BuyerId.Value,
                RefundTransactionId: refundRow.Id,
                ExpectedAmount: paymentAddress.ExpectedAmount,
                ReceivedAmount: received,
                ExcessAmount: received,
                Stablecoin: paymentAddress.ExpectedToken,
                SourceAddress: confirmedPayment.FromAddress,
                TxHash: confirmedPayment.TxHash ?? string.Empty,
                IsMultiPayment: true,
                OccurredAt: _clock.GetUtcNow().UtcDateTime),
            cancellationToken);

        _logger.LogInformation(
            "Multi-payment refund queued — txHash={TxHash} received={Received} state={State} refundId={RefundId} correlationId={CorrelationId}",
            confirmedPayment.TxHash, received, transaction.Status, refundRow.Id, correlationId);
        return AmountValidationOutcome.MultiPaymentRefunded;
    }

    private async Task<bool> AdvanceStateMachineAsync(
        Transaction transaction,
        BlockchainTransaction confirmedPayment,
        string correlationId,
        CancellationToken cancellationToken)
    {
        // 09 §9.2 — caller-side state machine: instantiate without expected
        // RowVersion (single-write contract; the outer handler owns the unit
        // of work and EF Core's optimistic concurrency token still kicks in at
        // SaveChanges time).
        var machine = new TransactionStateMachine(transaction);

        if (!machine.CanFire(TransactionTrigger.ConfirmPayment))
        {
            _logger.LogWarning(
                "ConfirmPayment fire skipped — transaction {TransactionId} state {State} (onHold={OnHold}) does not permit the trigger. correlationId={CorrelationId}",
                transaction.Id, transaction.Status, transaction.IsOnHold, correlationId);
            return false;
        }

        var previousStatus = transaction.Status;
        try
        {
            machine.Fire(TransactionTrigger.ConfirmPayment);
        }
        catch (DomainException ex)
        {
            _logger.LogWarning(ex,
                "ConfirmPayment fire rejected — transaction {TransactionId} state {State}. correlationId={CorrelationId}",
                transaction.Id, transaction.Status, correlationId);
            return false;
        }

        // WP15 — audit-trail row (06 §3.6). Payment confirmation is a SYSTEM
        // transition (driven by the on-chain finality webhook).
        TransactionHistoryRecorder.Record(
            _db, transaction, previousStatus, TransactionTrigger.ConfirmPayment,
            ActorType.SYSTEM, SeedConstants.SystemUserId, _clock.GetUtcNow().UtcDateTime);

        // PaymentReceivedEvent — T44 K2 wiring: realtime push (T61) consumes
        // this event; the surrounding SaveChanges commits both the state
        // transition and the outbox row in a single transaction.
        await _outbox.PublishAsync(
            new PaymentReceivedEvent(
                EventId: Guid.NewGuid(),
                TransactionId: transaction.Id,
                Amount: confirmedPayment.Amount,
                Stablecoin: confirmedPayment.Token,
                TxHash: confirmedPayment.TxHash ?? string.Empty,
                OccurredAt: _clock.GetUtcNow().UtcDateTime),
            cancellationToken);

        _logger.LogInformation(
            "ConfirmPayment fired — transaction {TransactionId} → PAYMENT_RECEIVED, txHash={TxHash} correlationId={CorrelationId}",
            transaction.Id, confirmedPayment.TxHash, correlationId);
        return true;
    }

    private BlockchainTransaction QueueRefundIntent(
        Guid transactionId,
        BlockchainTransactionType type,
        string sourceAddress,
        decimal amount,
        StablecoinType token,
        string? actualTokenAddress = null)
    {
        // 06 §3.8 type-dependent CHECK constraints — outbound transfers
        // (BUYER_REFUND / EXCESS_REFUND / WRONG_TOKEN_REFUND / INCORRECT_AMOUNT_REFUND /
        // LATE_PAYMENT_REFUND) MUST carry PaymentAddressId NULL. The
        // destination is the source address (FromAddress on the source row =
        // ToAddress on the refund row, 02 §4.6).
        var refund = new BlockchainTransaction
        {
            Id = Guid.NewGuid(),
            TransactionId = transactionId,
            PaymentAddressId = null,
            Type = type,
            TxHash = null,
            FromAddress = string.Empty, // Hot-wallet address set at T73 broadcast time.
            ToAddress = sourceAddress,
            Amount = amount,
            Token = token,
            ActualTokenAddress = actualTokenAddress,
            Status = BlockchainTransactionStatus.PENDING,
            BlockNumber = null,
            ConfirmationCount = 0,
            RetryCount = 0,
            CreatedAt = _clock.GetUtcNow().UtcDateTime,
        };

        _db.Set<BlockchainTransaction>().Add(refund);
        return refund;
    }

    private Task<PaymentAddress?> LoadPaymentAddressAsync(Guid? id, CancellationToken cancellationToken) =>
        id is null
            ? Task.FromResult<PaymentAddress?>(null)
            : _db.Set<PaymentAddress>().FirstOrDefaultAsync(p => p.Id == id.Value, cancellationToken);

    // Allowlist resolution mirrors the sidecar's classifyToken (T71 — 08 §3.4).
    // Backend uses contract-address equality without case-folding because Tron
    // addresses ship as case-sensitive base58 (T70 derivation).
    private static StablecoinType? ResolveStablecoinByContract(string? contractAddress)
    {
        if (string.IsNullOrWhiteSpace(contractAddress)) return null;
        if (contractAddress.Equals(KnownStablecoinContracts.Usdt, StringComparison.Ordinal))
            return StablecoinType.USDT;
        if (contractAddress.Equals(KnownStablecoinContracts.Usdc, StringComparison.Ordinal))
            return StablecoinType.USDC;
        return null;
    }
}

/// <summary>
/// MVP allowlist of TRC-20 stablecoin contract addresses (08 §3.3). Mirrors
/// the sidecar's <c>STABLECOIN_CONTRACTS_*</c> env block; kept in code on the
/// backend side because the sidecar is the authoritative source and a
/// migration round-trip is overkill for two constants.
/// </summary>
/// <remarks>
/// If the sidecar config diverges from these constants the validator path
/// degrades safely — unknown contract addresses fall back to <c>USDT</c>
/// rather than crashing, and the row's <c>ActualTokenAddress</c> already
/// carries the raw contract for downstream investigation.
/// </remarks>
public static class KnownStablecoinContracts
{
    public const string Usdt = "TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t";
    public const string Usdc = "TEkxiTehnzSmSe2XqrBj4w32RUN966rdz8";

    /// <summary>
    /// Resolve the canonical TRC-20 contract address for a backend
    /// stablecoin enum value (T75 dispatcher uses this to satisfy the
    /// sidecar's <c>expectedContract</c> field). Sidecar's own allowlist
    /// is the source of truth; this is a backend mirror for cases where
    /// the sidecar is not in the call chain.
    /// </summary>
    public static string ResolveContractAddress(StablecoinType token) => token switch
    {
        StablecoinType.USDT => Usdt,
        StablecoinType.USDC => Usdc,
        _ => throw new ArgumentOutOfRangeException(nameof(token), token, "Unsupported stablecoin."),
    };
}
