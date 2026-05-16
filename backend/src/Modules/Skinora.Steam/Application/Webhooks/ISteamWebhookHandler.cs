namespace Skinora.Steam.Application.Webhooks;

/// <summary>
/// Inbound webhook dispatcher (T68). The controller hands the signature-verified
/// envelope here; this service performs idempotent persistence (TradeOffer
/// upsert) and routes trade events into the transaction state machine.
/// </summary>
/// <remarks>
/// Bot lifecycle events (<c>bot.session_failed</c>, <c>bot.removed_from_pool</c>)
/// are recorded as Information-level Serilog entries. Admin notification
/// integration is forward-deferred to T96 (admin notification system) — see
/// the K-list in <c>TASK_REPORTS/T68_REPORT.md</c>.
/// </remarks>
public interface ISteamWebhookHandler
{
    Task HandleBotEventAsync(
        SteamWebhookEnvelope<BotEventData> envelope,
        string correlationId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Dispatch a trade offer event. Returns a result discriminator the
    /// controller maps to a status code; all paths still respond 200 unless
    /// the envelope itself is malformed, so the sidecar never reaches the
    /// retry/escalation branch on legitimate races.
    /// </summary>
    Task<TradeWebhookResult> HandleTradeEventAsync(
        SteamWebhookEnvelope<TradeOfferEventData> envelope,
        string correlationId,
        CancellationToken cancellationToken);
}

/// <summary>
/// Outcome of a trade webhook dispatch. Each variant maps to a 200 response
/// (the sidecar treats anything other than 401/5xx as terminal) — the
/// discriminator is for logs / tests.
/// </summary>
public enum TradeWebhookResult
{
    /// <summary>State machine advanced or trade row written for the first time.</summary>
    Applied,

    /// <summary>
    /// Idempotent replay — TradeOffer already at terminal state or state
    /// machine refused the trigger because the transaction has moved past
    /// the relevant state. Counts as success.
    /// </summary>
    Idempotent,

    /// <summary>
    /// Sidecar referenced a Transaction or TradeOffer that the backend can
    /// no longer find. Logged at Warning and acknowledged so the sidecar
    /// stops retrying; reconciliation falls to T76 (blockchain) / T69
    /// (failover) follow-ups.
    /// </summary>
    Unknown,
}
