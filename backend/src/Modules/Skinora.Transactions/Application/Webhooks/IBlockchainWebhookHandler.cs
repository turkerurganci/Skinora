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

    /// <summary>
    /// Late buyer transfer arrived at a cancelled transaction's deposit
    /// address (T75 — 02 §4.4). Backend persists the incoming row and
    /// queues a <c>LATE_PAYMENT_REFUND</c> intent for the existing T73
    /// transfer pipeline.
    /// </summary>
    Task<BlockchainWebhookResult> HandleLatePaymentDetectedAsync(
        BlockchainWebhookEnvelope<LatePaymentDetectedData> envelope,
        string correlationId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Sidecar advanced the post-cancel state machine (T75 — 06 §2.16).
    /// Backend mirrors <c>PaymentAddress.MonitoringStatus</c> /
    /// <c>MonitoringExpiresAt</c> and raises an admin notification when
    /// the terminal <c>STOPPED</c> is reached.
    /// </summary>
    Task<BlockchainWebhookResult> HandlePostCancelMonitorStateChangedAsync(
        BlockchainWebhookEnvelope<PostCancelMonitorStateChangedData> envelope,
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
