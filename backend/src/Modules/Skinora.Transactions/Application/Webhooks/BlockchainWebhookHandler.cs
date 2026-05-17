using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Domain.Entities;

namespace Skinora.Transactions.Application.Webhooks;

/// <summary>
/// Default <see cref="IBlockchainWebhookHandler"/>. Each method owns a single
/// <see cref="AppDbContext"/> transaction so the BlockchainTransaction row
/// and the T72 amount validation side effects (state-machine flip, refund
/// intent row, outbox events) share the same SaveChanges
/// (mirrors <see cref="Skinora.Steam.Application.Webhooks.SteamWebhookHandler"/>).
///
/// <para>
/// Idempotency is enforced by the <c>UQ_BlockchainTransactions_TxHash</c>
/// unique index (06 §3.8). Duplicate webhook deliveries surface as
/// <see cref="BlockchainWebhookResult.Idempotent"/> after a row-existence
/// lookup, which keeps the happy path free of unique-violation exceptions.
/// </para>
///
/// <para>
/// T72 (this task) wires <see cref="IAmountValidationService"/> into
/// <see cref="HandlePaymentConfirmedAsync"/> and
/// <see cref="HandleWrongTokenIncomingAsync"/>; T73 picks up the refund-intent
/// rows queued at <c>Status=PENDING</c> and performs the TRC-20 broadcast.
/// </para>
/// </summary>
public sealed class BlockchainWebhookHandler : IBlockchainWebhookHandler
{
    // SPAM_TOKEN_INCOMING is recorded as terminal CONFIRMED but the sidecar
    // never runs finality on it (08 §3.4). The Status=CONFIRMED CHECK
    // constraint requires ConfirmationCount >= 20, so we pin to this
    // synthetic floor. The reader treats spam rows as audit-only regardless.
    private const int SpamConfirmationFloor = 20;

    private readonly AppDbContext _db;
    private readonly IAmountValidationService _amountValidation;
    private readonly TimeProvider _clock;
    private readonly ILogger<BlockchainWebhookHandler> _logger;

    public BlockchainWebhookHandler(
        AppDbContext db,
        IAmountValidationService amountValidation,
        TimeProvider clock,
        ILogger<BlockchainWebhookHandler> logger)
    {
        _db = db;
        _amountValidation = amountValidation;
        _clock = clock;
        _logger = logger;
    }

    public async Task<BlockchainWebhookResult> HandlePaymentDetectedAsync(
        BlockchainWebhookEnvelope<PaymentDetectedData> envelope,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var data = envelope.Data;
        if (data is null || string.IsNullOrWhiteSpace(data.TxHash))
        {
            _logger.LogWarning(
                "PaymentDetected payload missing required fields. correlationId={CorrelationId}",
                correlationId);
            return BlockchainWebhookResult.Invalid;
        }

        if (!TryParseAmount(data.Amount, out var amount) || !TryParseToken(data.TokenSymbol, out var token))
        {
            _logger.LogWarning(
                "PaymentDetected rejected — amount={Amount} token={Token} unparseable. correlationId={CorrelationId}",
                data.Amount, data.TokenSymbol, correlationId);
            return BlockchainWebhookResult.Invalid;
        }

        if (await ExistsByTxHashAsync(data.TxHash, cancellationToken))
        {
            _logger.LogInformation(
                "PaymentDetected for {TxHash} already recorded — idempotent ack. correlationId={CorrelationId}",
                data.TxHash, correlationId);
            return BlockchainWebhookResult.Idempotent;
        }

        var paymentAddress = await LoadPaymentAddressAsync(data.PaymentAddressId, cancellationToken);
        if (paymentAddress is null)
        {
            _logger.LogWarning(
                "PaymentDetected references unknown PaymentAddress {PaymentAddressId} — dropping. correlationId={CorrelationId}",
                data.PaymentAddressId, correlationId);
            return BlockchainWebhookResult.Unknown;
        }

        var entity = new BlockchainTransaction
        {
            Id = Guid.NewGuid(),
            TransactionId = paymentAddress.TransactionId,
            PaymentAddressId = paymentAddress.Id,
            Type = BlockchainTransactionType.BUYER_PAYMENT,
            TxHash = data.TxHash,
            FromAddress = data.FromAddress,
            ToAddress = data.ToAddress,
            Amount = amount,
            Token = token,
            ActualTokenAddress = null,
            Status = BlockchainTransactionStatus.DETECTED,
            BlockNumber = null,
            ConfirmationCount = 0,
            CreatedAt = _clock.GetUtcNow().UtcDateTime,
        };

        _db.Set<BlockchainTransaction>().Add(entity);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "PaymentDetected recorded: txHash={TxHash} amount={Amount} {Token} transactionId={TransactionId} correlationId={CorrelationId}",
            data.TxHash, amount, token, paymentAddress.TransactionId, correlationId);
        return BlockchainWebhookResult.Applied;
    }

    public async Task<BlockchainWebhookResult> HandlePaymentConfirmedAsync(
        BlockchainWebhookEnvelope<PaymentConfirmedData> envelope,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var data = envelope.Data;
        if (data is null || string.IsNullOrWhiteSpace(data.TxHash))
        {
            _logger.LogWarning(
                "PaymentConfirmed payload missing required fields. correlationId={CorrelationId}",
                correlationId);
            return BlockchainWebhookResult.Invalid;
        }
        if (data.ConfirmationCount < 0 || data.BlockNumber <= 0)
        {
            _logger.LogWarning(
                "PaymentConfirmed rejected — invalid finality values blockNumber={BlockNumber} confirmations={Confirmations}. correlationId={CorrelationId}",
                data.BlockNumber, data.ConfirmationCount, correlationId);
            return BlockchainWebhookResult.Invalid;
        }

        var existing = await _db.Set<BlockchainTransaction>()
            .FirstOrDefaultAsync(b => b.TxHash == data.TxHash, cancellationToken);

        if (existing is null)
        {
            // Race condition or replay where PaymentDetected was never seen —
            // accept and create the row directly at CONFIRMED. PaymentAddress
            // must exist; otherwise treat as Unknown.
            var paymentAddress = await LoadPaymentAddressAsync(data.PaymentAddressId, cancellationToken);
            if (paymentAddress is null)
            {
                _logger.LogWarning(
                    "PaymentConfirmed references unknown PaymentAddress {PaymentAddressId} and has no prior DETECTED row — dropping. correlationId={CorrelationId}",
                    data.PaymentAddressId, correlationId);
                return BlockchainWebhookResult.Unknown;
            }

            // Without a prior DETECTED row we lack Amount/From/To from the
            // sidecar — those came in the PaymentDetected envelope only.
            // Per 06 §3.8 CHECK constraints the row needs them populated, so
            // we refuse rather than write a partial record. Sidecar will not
            // retry PaymentConfirmed without first emitting PaymentDetected
            // (MonitorRegistry contract).
            _logger.LogWarning(
                "PaymentConfirmed for {TxHash} arrived without prior DETECTED row — dropping. correlationId={CorrelationId}",
                data.TxHash, correlationId);
            return BlockchainWebhookResult.Unknown;
        }

        if (existing.Status == BlockchainTransactionStatus.CONFIRMED)
        {
            _logger.LogInformation(
                "PaymentConfirmed for {TxHash} already CONFIRMED — idempotent ack. correlationId={CorrelationId}",
                data.TxHash, correlationId);
            return BlockchainWebhookResult.Idempotent;
        }

        existing.Status = BlockchainTransactionStatus.CONFIRMED;
        existing.BlockNumber = data.BlockNumber;
        existing.ConfirmationCount = Math.Max(data.ConfirmationCount, 20);
        existing.ConfirmedAt = _clock.GetUtcNow().UtcDateTime;

        // T72 — amount validation pipeline runs INSIDE this unit of work so
        // the CONFIRMED flip + state-machine fire + refund-intent row + outbox
        // events all commit atomically via the single SaveChangesAsync below.
        var validationOutcome = await _amountValidation.ValidateConfirmedBuyerPaymentAsync(
            existing,
            correlationId,
            cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "PaymentConfirmed applied: txHash={TxHash} block={Block} confirmations={Confirmations} transactionId={TransactionId} outcome={Outcome} correlationId={CorrelationId}",
            data.TxHash, data.BlockNumber, existing.ConfirmationCount, existing.TransactionId, validationOutcome, correlationId);
        return BlockchainWebhookResult.Applied;
    }

    public async Task<BlockchainWebhookResult> HandleWrongTokenIncomingAsync(
        BlockchainWebhookEnvelope<WrongTokenIncomingData> envelope,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var data = envelope.Data;
        if (data is null
            || string.IsNullOrWhiteSpace(data.TxHash)
            || string.IsNullOrWhiteSpace(data.ActualContractAddress))
        {
            _logger.LogWarning(
                "WrongTokenIncoming payload missing required fields. correlationId={CorrelationId}",
                correlationId);
            return BlockchainWebhookResult.Invalid;
        }

        if (!TryParseAmount(data.Amount, out var amount)
            || !TryParseToken(data.ActualTokenSymbol, out var actualToken))
        {
            _logger.LogWarning(
                "WrongTokenIncoming rejected — amount={Amount} token={Token} unparseable. correlationId={CorrelationId}",
                data.Amount, data.ActualTokenSymbol, correlationId);
            return BlockchainWebhookResult.Invalid;
        }

        if (await ExistsByTxHashAsync(data.TxHash, cancellationToken))
        {
            _logger.LogInformation(
                "WrongTokenIncoming for {TxHash} already recorded — idempotent ack. correlationId={CorrelationId}",
                data.TxHash, correlationId);
            return BlockchainWebhookResult.Idempotent;
        }

        var paymentAddress = await LoadPaymentAddressAsync(data.PaymentAddressId, cancellationToken);
        if (paymentAddress is null)
        {
            _logger.LogWarning(
                "WrongTokenIncoming references unknown PaymentAddress {PaymentAddressId} — dropping. correlationId={CorrelationId}",
                data.PaymentAddressId, correlationId);
            return BlockchainWebhookResult.Unknown;
        }

        var entity = new BlockchainTransaction
        {
            Id = Guid.NewGuid(),
            TransactionId = paymentAddress.TransactionId,
            PaymentAddressId = paymentAddress.Id,
            Type = BlockchainTransactionType.WRONG_TOKEN_INCOMING,
            TxHash = data.TxHash,
            FromAddress = data.FromAddress,
            ToAddress = data.ToAddress,
            Amount = amount,
            // Token carries the *expected* stablecoin per 06 §3.8 note:
            // ActualTokenAddress is the discriminator for the unexpected one.
            Token = paymentAddress.ExpectedToken,
            ActualTokenAddress = data.ActualContractAddress,
            Status = BlockchainTransactionStatus.DETECTED,
            BlockNumber = null,
            ConfirmationCount = 0,
            CreatedAt = _clock.GetUtcNow().UtcDateTime,
        };

        _db.Set<BlockchainTransaction>().Add(entity);

        // T72 — wrong-token validation: classify against refund threshold and
        // either queue a WRONG_TOKEN_REFUND PENDING row or raise the admin
        // alert. Shares the SaveChangesAsync below with the incoming row write.
        var validationOutcome = await _amountValidation.ValidateWrongTokenIncomingAsync(
            entity,
            correlationId,
            cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogWarning(
            "WrongTokenIncoming recorded: txHash={TxHash} actual={ActualToken} expected={ExpectedToken} transactionId={TransactionId} outcome={Outcome} correlationId={CorrelationId}",
            data.TxHash, actualToken, paymentAddress.ExpectedToken, paymentAddress.TransactionId, validationOutcome, correlationId);
        return BlockchainWebhookResult.Applied;
    }

    public async Task<BlockchainWebhookResult> HandleSpamTokenIncomingAsync(
        BlockchainWebhookEnvelope<SpamTokenIncomingData> envelope,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var data = envelope.Data;
        if (data is null
            || string.IsNullOrWhiteSpace(data.TxHash)
            || string.IsNullOrWhiteSpace(data.ActualContractAddress))
        {
            _logger.LogWarning(
                "SpamTokenIncoming payload missing required fields. correlationId={CorrelationId}",
                correlationId);
            return BlockchainWebhookResult.Invalid;
        }

        if (!TryParseAmount(data.Amount, out var amount))
        {
            _logger.LogWarning(
                "SpamTokenIncoming rejected — amount={Amount} unparseable. correlationId={CorrelationId}",
                data.Amount, correlationId);
            return BlockchainWebhookResult.Invalid;
        }

        if (await ExistsByTxHashAsync(data.TxHash, cancellationToken))
        {
            _logger.LogInformation(
                "SpamTokenIncoming for {TxHash} already recorded — idempotent ack. correlationId={CorrelationId}",
                data.TxHash, correlationId);
            return BlockchainWebhookResult.Idempotent;
        }

        var paymentAddress = await LoadPaymentAddressAsync(data.PaymentAddressId, cancellationToken);
        if (paymentAddress is null)
        {
            _logger.LogWarning(
                "SpamTokenIncoming references unknown PaymentAddress {PaymentAddressId} — dropping. correlationId={CorrelationId}",
                data.PaymentAddressId, correlationId);
            return BlockchainWebhookResult.Unknown;
        }

        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var entity = new BlockchainTransaction
        {
            Id = Guid.NewGuid(),
            TransactionId = paymentAddress.TransactionId,
            PaymentAddressId = paymentAddress.Id,
            Type = BlockchainTransactionType.SPAM_TOKEN_INCOMING,
            TxHash = data.TxHash,
            FromAddress = data.FromAddress,
            ToAddress = data.ToAddress,
            Amount = amount,
            Token = paymentAddress.ExpectedToken,
            ActualTokenAddress = data.ActualContractAddress,
            // 06 §3.8 — spam tokens land at terminal CONFIRMED so the row is
            // immutable audit; ConfirmationCount=20 satisfies the
            // CK_BlockchainTransactions_Status_Confirmed CHECK constraint.
            Status = BlockchainTransactionStatus.CONFIRMED,
            BlockNumber = null,
            ConfirmationCount = SpamConfirmationFloor,
            ConfirmedAt = nowUtc,
            CreatedAt = nowUtc,
        };

        _db.Set<BlockchainTransaction>().Add(entity);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogWarning(
            "SpamTokenIncoming recorded (terminal, no refund): txHash={TxHash} contract={Contract} transactionId={TransactionId} correlationId={CorrelationId}",
            data.TxHash, data.ActualContractAddress, paymentAddress.TransactionId, correlationId);
        return BlockchainWebhookResult.Applied;
    }

    public async Task<BlockchainWebhookResult> HandleLatePaymentDetectedAsync(
        BlockchainWebhookEnvelope<LatePaymentDetectedData> envelope,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var data = envelope.Data;
        if (data is null || string.IsNullOrWhiteSpace(data.TxHash))
        {
            _logger.LogWarning(
                "LatePaymentDetected payload missing required fields. correlationId={CorrelationId}",
                correlationId);
            return BlockchainWebhookResult.Invalid;
        }

        if (!TryParseAmount(data.Amount, out var amount) || !TryParseToken(data.TokenSymbol, out var token))
        {
            _logger.LogWarning(
                "LatePaymentDetected rejected — amount={Amount} token={Token} unparseable. correlationId={CorrelationId}",
                data.Amount, data.TokenSymbol, correlationId);
            return BlockchainWebhookResult.Invalid;
        }

        if (await ExistsByTxHashAsync(data.TxHash, cancellationToken))
        {
            _logger.LogInformation(
                "LatePaymentDetected for {TxHash} already recorded — idempotent ack. correlationId={CorrelationId}",
                data.TxHash, correlationId);
            return BlockchainWebhookResult.Idempotent;
        }

        var paymentAddress = await LoadPaymentAddressAsync(data.PaymentAddressId, cancellationToken);
        if (paymentAddress is null)
        {
            _logger.LogWarning(
                "LatePaymentDetected references unknown PaymentAddress {PaymentAddressId} — dropping. correlationId={CorrelationId}",
                data.PaymentAddressId, correlationId);
            return BlockchainWebhookResult.Unknown;
        }

        // Defensive — sidecar should only have an active post-cancel monitor for
        // POST_CANCEL_* state. ACTIVE monitor late-detect would be a sidecar bug;
        // accept and log so the row is not lost, but raise the inconsistency.
        if (paymentAddress.MonitoringStatus is MonitoringStatus.ACTIVE or MonitoringStatus.STOPPED)
        {
            _logger.LogWarning(
                "LatePaymentDetected for PaymentAddress {PaymentAddressId} arrived in {State} state — sidecar/backend drift. correlationId={CorrelationId}",
                paymentAddress.Id, paymentAddress.MonitoringStatus, correlationId);
        }

        var entity = new BlockchainTransaction
        {
            Id = Guid.NewGuid(),
            TransactionId = paymentAddress.TransactionId,
            PaymentAddressId = paymentAddress.Id,
            Type = BlockchainTransactionType.BUYER_PAYMENT,
            TxHash = data.TxHash,
            FromAddress = data.FromAddress,
            ToAddress = data.ToAddress,
            Amount = amount,
            Token = token,
            ActualTokenAddress = null,
            Status = BlockchainTransactionStatus.DETECTED,
            BlockNumber = null,
            ConfirmationCount = 0,
            CreatedAt = _clock.GetUtcNow().UtcDateTime,
        };

        _db.Set<BlockchainTransaction>().Add(entity);

        // Late-payment branch reuses the T72 validation infrastructure: gas-fee
        // settings, refund decision threshold, blocked alert. Refund row is
        // queued as LATE_PAYMENT_REFUND and the LatePaymentRefundRequestedEvent
        // commits in the same SaveChanges as the incoming row.
        var validationOutcome = await _amountValidation.ValidateLatePaymentDetectedAsync(
            entity,
            correlationId,
            cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "LatePaymentDetected recorded: txHash={TxHash} amount={Amount} {Token} transactionId={TransactionId} monitorState={MonitorState} outcome={Outcome} correlationId={CorrelationId}",
            data.TxHash, amount, token, paymentAddress.TransactionId, data.MonitorState, validationOutcome, correlationId);
        return BlockchainWebhookResult.Applied;
    }

    public async Task<BlockchainWebhookResult> HandlePostCancelMonitorStateChangedAsync(
        BlockchainWebhookEnvelope<PostCancelMonitorStateChangedData> envelope,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var data = envelope.Data;
        if (data is null
            || data.PaymentAddressId == Guid.Empty
            || string.IsNullOrWhiteSpace(data.NewState))
        {
            _logger.LogWarning(
                "PostCancelMonitorStateChanged payload missing required fields. correlationId={CorrelationId}",
                correlationId);
            return BlockchainWebhookResult.Invalid;
        }

        if (!TryParseMonitoringStatus(data.NewState, out var newState))
        {
            _logger.LogWarning(
                "PostCancelMonitorStateChanged rejected — unknown newState={NewState}. correlationId={CorrelationId}",
                data.NewState, correlationId);
            return BlockchainWebhookResult.Invalid;
        }

        var paymentAddress = await LoadPaymentAddressAsync(data.PaymentAddressId, cancellationToken);
        if (paymentAddress is null)
        {
            _logger.LogWarning(
                "PostCancelMonitorStateChanged references unknown PaymentAddress {PaymentAddressId} — dropping. correlationId={CorrelationId}",
                data.PaymentAddressId, correlationId);
            return BlockchainWebhookResult.Unknown;
        }

        if (paymentAddress.MonitoringStatus == newState)
        {
            _logger.LogInformation(
                "PostCancelMonitorStateChanged for PaymentAddress {PaymentAddressId} already at {State} — idempotent ack. correlationId={CorrelationId}",
                paymentAddress.Id, newState, correlationId);
            return BlockchainWebhookResult.Idempotent;
        }

        DateTime? newExpiresAt = null;
        if (!string.IsNullOrWhiteSpace(data.NewStateExpiresAt))
        {
            if (!TryParseUtc(data.NewStateExpiresAt, out var parsed))
            {
                _logger.LogWarning(
                    "PostCancelMonitorStateChanged rejected — unparsable newStateExpiresAt={Value}. correlationId={CorrelationId}",
                    data.NewStateExpiresAt, correlationId);
                return BlockchainWebhookResult.Invalid;
            }
            newExpiresAt = parsed;
        }

        var previousStatus = paymentAddress.MonitoringStatus;
        paymentAddress.MonitoringStatus = newState;
        paymentAddress.MonitoringExpiresAt = newState == MonitoringStatus.STOPPED ? null : newExpiresAt;

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "PostCancelMonitorStateChanged applied: paymentAddressId={PaymentAddressId} {Previous} → {New} expiresAt={ExpiresAt} correlationId={CorrelationId}",
            paymentAddress.Id, previousStatus, newState, paymentAddress.MonitoringExpiresAt, correlationId);
        return BlockchainWebhookResult.Applied;
    }

    private static bool TryParseMonitoringStatus(string value, out MonitoringStatus status)
        => Enum.TryParse(value, ignoreCase: false, out status);

    private static bool TryParseUtc(string value, out DateTime result)
        => DateTime.TryParse(
            value,
            CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
            out result);

    private Task<PaymentAddress?> LoadPaymentAddressAsync(Guid id, CancellationToken cancellationToken)
        => _db.Set<PaymentAddress>().FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    private Task<bool> ExistsByTxHashAsync(string txHash, CancellationToken cancellationToken)
        => _db.Set<BlockchainTransaction>().AnyAsync(b => b.TxHash == txHash, cancellationToken);

    private static bool TryParseAmount(string raw, out decimal amount)
        => decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out amount)
            && amount >= 0m;

    private static bool TryParseToken(string symbol, out StablecoinType token)
        => Enum.TryParse(symbol, ignoreCase: false, out token);
}
