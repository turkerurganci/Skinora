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
