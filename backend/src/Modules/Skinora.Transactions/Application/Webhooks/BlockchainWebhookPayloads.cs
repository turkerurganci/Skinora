using System.Text.Json.Serialization;

namespace Skinora.Transactions.Application.Webhooks;

/// <summary>
/// Inbound webhook envelopes from the blockchain sidecar (T71 — 08 §3.4,
/// 05 §3.3). The signature envelope (timestamp / nonce / X-Signature) is
/// handled by <c>WebhookSignatureMiddleware</c> upstream — controllers see
/// only the JSON body. Wire shapes mirror
/// <c>sidecar-blockchain/src/webhook/WebhookPayloads.ts</c>.
/// </summary>
public static class BlockchainWebhookEvents
{
    public const string PaymentDetected = "payment.detected";
    public const string PaymentConfirmed = "payment.confirmed";
    public const string WrongTokenIncoming = "payment.wrong_token";
    public const string SpamTokenIncoming = "payment.spam_token";
    public const string LatePaymentDetected = "payment.late_detected";
    public const string PostCancelMonitorStateChanged = "monitor.post_cancel_state_changed";
}

/// <summary>
/// Post-cancel monitor state values (06 §2.16 / 08 §3.4). Wire-format mirror
/// of <see cref="Skinora.Shared.Enums.MonitoringStatus"/> so payloads can be
/// validated before the enum parse.
/// </summary>
public static class PostCancelMonitorStates
{
    public const string PostCancel24h = "POST_CANCEL_24H";
    public const string PostCancel7d = "POST_CANCEL_7D";
    public const string PostCancel30d = "POST_CANCEL_30D";
    public const string Stopped = "STOPPED";
}

/// <summary>
/// Outer envelope shared by every blockchain sidecar webhook.
/// </summary>
public sealed class BlockchainWebhookEnvelope<TData>
{
    [JsonPropertyName("event")]
    public string Event { get; set; } = string.Empty;

    [JsonPropertyName("timestamp")]
    public string Timestamp { get; set; } = string.Empty;

    [JsonPropertyName("data")]
    public TData Data { get; set; } = default!;
}

/// <summary>
/// Phase 1 first sighting — the deposit address received a Transfer from
/// the expected token contract.
/// </summary>
public sealed class PaymentDetectedData
{
    [JsonPropertyName("paymentAddressId")]
    public Guid PaymentAddressId { get; set; }

    [JsonPropertyName("transactionId")]
    public Guid TransactionId { get; set; }

    [JsonPropertyName("txHash")]
    public string TxHash { get; set; } = string.Empty;

    /// <summary>
    /// On-chain log index of this Transfer event within <c>txHash</c>
    /// (08 §3.4 — WP10). With <c>txHash</c> forms the per-event dedup key;
    /// the common single-transfer transaction reports 0.
    /// </summary>
    [JsonPropertyName("eventIndex")]
    public int EventIndex { get; set; }

    [JsonPropertyName("fromAddress")]
    public string FromAddress { get; set; } = string.Empty;

    [JsonPropertyName("toAddress")]
    public string ToAddress { get; set; } = string.Empty;

    [JsonPropertyName("contractAddress")]
    public string ContractAddress { get; set; } = string.Empty;

    [JsonPropertyName("tokenSymbol")]
    public string TokenSymbol { get; set; } = string.Empty;

    /// <summary>Decimal string with 6 fraction digits — parse with <c>decimal.Parse</c>.</summary>
    [JsonPropertyName("amount")]
    public string Amount { get; set; } = string.Empty;

    [JsonPropertyName("blockTimestampMs")]
    public long BlockTimestampMs { get; set; }

    [JsonPropertyName("detectedAt")]
    public string DetectedAt { get; set; } = string.Empty;
}

/// <summary>
/// Finality reached — <c>currentSolidBlock - txBlock >= 20</c>. Backend flips
/// the BlockchainTransaction row to <c>Status=CONFIRMED</c> with
/// <c>BlockNumber</c> + <c>ConfirmedAt</c>.
/// </summary>
public sealed class PaymentConfirmedData
{
    [JsonPropertyName("paymentAddressId")]
    public Guid PaymentAddressId { get; set; }

    [JsonPropertyName("transactionId")]
    public Guid TransactionId { get; set; }

    [JsonPropertyName("txHash")]
    public string TxHash { get; set; } = string.Empty;

    /// <summary>On-chain log index — matches the DETECTED row's (TxHash, EventIndex) (08 §3.4 — WP10).</summary>
    [JsonPropertyName("eventIndex")]
    public int EventIndex { get; set; }

    [JsonPropertyName("blockNumber")]
    public long BlockNumber { get; set; }

    [JsonPropertyName("confirmationCount")]
    public int ConfirmationCount { get; set; }

    [JsonPropertyName("confirmedAt")]
    public string ConfirmedAt { get; set; } = string.Empty;
}

/// <summary>
/// Phase 2 hit — the deposit address received a supported stablecoin that is
/// different from the one the buyer was billed for.
/// </summary>
public sealed class WrongTokenIncomingData
{
    [JsonPropertyName("paymentAddressId")]
    public Guid PaymentAddressId { get; set; }

    [JsonPropertyName("transactionId")]
    public Guid TransactionId { get; set; }

    [JsonPropertyName("txHash")]
    public string TxHash { get; set; } = string.Empty;

    /// <summary>On-chain log index of this Transfer event within <c>txHash</c> (08 §3.4 — WP10).</summary>
    [JsonPropertyName("eventIndex")]
    public int EventIndex { get; set; }

    [JsonPropertyName("fromAddress")]
    public string FromAddress { get; set; } = string.Empty;

    [JsonPropertyName("toAddress")]
    public string ToAddress { get; set; } = string.Empty;

    [JsonPropertyName("expectedContractAddress")]
    public string ExpectedContractAddress { get; set; } = string.Empty;

    [JsonPropertyName("actualContractAddress")]
    public string ActualContractAddress { get; set; } = string.Empty;

    [JsonPropertyName("actualTokenSymbol")]
    public string ActualTokenSymbol { get; set; } = string.Empty;

    [JsonPropertyName("amount")]
    public string Amount { get; set; } = string.Empty;

    [JsonPropertyName("blockTimestampMs")]
    public long BlockTimestampMs { get; set; }

    [JsonPropertyName("detectedAt")]
    public string DetectedAt { get; set; } = string.Empty;
}

/// <summary>
/// Late payment detected at a cancelled transaction's deposit address
/// (T75 — 02 §4.4, 08 §3.4 gecikmeli ödeme). Carries the same transfer
/// fields as <see cref="PaymentDetectedData"/> plus the post-cancel state
/// in which the transfer was observed. Backend persists a
/// <c>BUYER_PAYMENT</c> row + queues a <c>LATE_PAYMENT_REFUND</c> refund
/// intent (T73 pipeline reused).
/// </summary>
public sealed class LatePaymentDetectedData
{
    [JsonPropertyName("paymentAddressId")]
    public Guid PaymentAddressId { get; set; }

    [JsonPropertyName("transactionId")]
    public Guid TransactionId { get; set; }

    [JsonPropertyName("txHash")]
    public string TxHash { get; set; } = string.Empty;

    /// <summary>On-chain log index of this Transfer event within <c>txHash</c> (08 §3.4 — WP10).</summary>
    [JsonPropertyName("eventIndex")]
    public int EventIndex { get; set; }

    [JsonPropertyName("fromAddress")]
    public string FromAddress { get; set; } = string.Empty;

    [JsonPropertyName("toAddress")]
    public string ToAddress { get; set; } = string.Empty;

    [JsonPropertyName("contractAddress")]
    public string ContractAddress { get; set; } = string.Empty;

    [JsonPropertyName("tokenSymbol")]
    public string TokenSymbol { get; set; } = string.Empty;

    [JsonPropertyName("amount")]
    public string Amount { get; set; } = string.Empty;

    [JsonPropertyName("blockTimestampMs")]
    public long BlockTimestampMs { get; set; }

    [JsonPropertyName("detectedAt")]
    public string DetectedAt { get; set; } = string.Empty;

    /// <summary>Post-cancel state at the moment of detection — audit field.</summary>
    [JsonPropertyName("monitorState")]
    public string MonitorState { get; set; } = string.Empty;
}

/// <summary>
/// Sidecar post-cancel monitor state-machine advanced (T75 — 06 §2.16).
/// Backend mirrors <c>PaymentAddress.MonitoringStatus</c> and
/// <c>MonitoringExpiresAt</c>; on the <c>STOPPED</c> terminal a
/// notification + audit log are emitted for the admin.
/// </summary>
public sealed class PostCancelMonitorStateChangedData
{
    [JsonPropertyName("paymentAddressId")]
    public Guid PaymentAddressId { get; set; }

    [JsonPropertyName("transactionId")]
    public Guid TransactionId { get; set; }

    [JsonPropertyName("address")]
    public string Address { get; set; } = string.Empty;

    [JsonPropertyName("previousState")]
    public string PreviousState { get; set; } = string.Empty;

    [JsonPropertyName("newState")]
    public string NewState { get; set; } = string.Empty;

    /// <summary>Null when <c>NewState == STOPPED</c>.</summary>
    [JsonPropertyName("newStateExpiresAt")]
    public string? NewStateExpiresAt { get; set; }

    [JsonPropertyName("cancelledAt")]
    public string CancelledAt { get; set; } = string.Empty;

    [JsonPropertyName("changedAt")]
    public string ChangedAt { get; set; } = string.Empty;
}

/// <summary>
/// Phase 2 hit — the deposit address received a token not on the platform
/// allowlist. Recorded as terminal <c>CONFIRMED</c>; no refund attempted
/// (08 §3.4 spam policy).
/// </summary>
public sealed class SpamTokenIncomingData
{
    [JsonPropertyName("paymentAddressId")]
    public Guid PaymentAddressId { get; set; }

    [JsonPropertyName("transactionId")]
    public Guid TransactionId { get; set; }

    [JsonPropertyName("txHash")]
    public string TxHash { get; set; } = string.Empty;

    /// <summary>On-chain log index of this Transfer event within <c>txHash</c> (08 §3.4 — WP10).</summary>
    [JsonPropertyName("eventIndex")]
    public int EventIndex { get; set; }

    [JsonPropertyName("fromAddress")]
    public string FromAddress { get; set; } = string.Empty;

    [JsonPropertyName("toAddress")]
    public string ToAddress { get; set; } = string.Empty;

    [JsonPropertyName("expectedContractAddress")]
    public string ExpectedContractAddress { get; set; } = string.Empty;

    [JsonPropertyName("actualContractAddress")]
    public string ActualContractAddress { get; set; } = string.Empty;

    [JsonPropertyName("amount")]
    public string Amount { get; set; } = string.Empty;

    [JsonPropertyName("blockTimestampMs")]
    public long BlockTimestampMs { get; set; }

    [JsonPropertyName("detectedAt")]
    public string DetectedAt { get; set; } = string.Empty;
}
