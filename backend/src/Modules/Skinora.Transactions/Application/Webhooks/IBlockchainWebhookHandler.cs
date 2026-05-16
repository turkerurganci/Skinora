namespace Skinora.Transactions.Application.Webhooks;

/// <summary>
/// Inbound webhook dispatcher for the blockchain sidecar (T71 — 08 §3.4).
/// Each handler performs idempotent persistence of a
/// <see cref="Skinora.Transactions.Domain.Entities.BlockchainTransaction"/>
/// row (06 §3.8).
/// </summary>
/// <remarks>
/// Transaction state-machine advancement (<c>PAYMENT_RECEIVED</c>) and
/// refund dispatch are forward-deferred to T72 (amount validation +
/// state trigger) and T73 (TRC-20 transfer execution) respectively — T71
/// records the ledger fact only.
/// </remarks>
public interface IBlockchainWebhookHandler
{
    Task<BlockchainWebhookResult> HandlePaymentDetectedAsync(
        BlockchainWebhookEnvelope<PaymentDetectedData> envelope,
        string correlationId,
        CancellationToken cancellationToken);

    Task<BlockchainWebhookResult> HandlePaymentConfirmedAsync(
        BlockchainWebhookEnvelope<PaymentConfirmedData> envelope,
        string correlationId,
        CancellationToken cancellationToken);

    Task<BlockchainWebhookResult> HandleWrongTokenIncomingAsync(
        BlockchainWebhookEnvelope<WrongTokenIncomingData> envelope,
        string correlationId,
        CancellationToken cancellationToken);

    Task<BlockchainWebhookResult> HandleSpamTokenIncomingAsync(
        BlockchainWebhookEnvelope<SpamTokenIncomingData> envelope,
        string correlationId,
        CancellationToken cancellationToken);
}

/// <summary>
/// Outcome of a blockchain webhook dispatch. Each variant maps to a 200
/// response (the sidecar treats anything other than 401/5xx as terminal) —
/// the discriminator is for logs / tests.
/// </summary>
public enum BlockchainWebhookResult
{
    /// <summary>Row created or status advanced.</summary>
    Applied,

    /// <summary>Same TxHash already recorded — no-op acknowledge.</summary>
    Idempotent,

    /// <summary>
    /// Payload references a PaymentAddress that the backend can no longer
    /// find. Logged at Warning and acknowledged so the sidecar stops
    /// retrying; the BlockchainTransaction row is not written.
    /// </summary>
    Unknown,

    /// <summary>
    /// Payload rejected because a required field was missing/invalid
    /// (decimal parse failure, malformed GUID etc.). Logged at Warning.
    /// </summary>
    Invalid,
}
