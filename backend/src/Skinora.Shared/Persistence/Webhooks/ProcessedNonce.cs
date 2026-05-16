using Skinora.Shared.Domain;

namespace Skinora.Shared.Persistence.Webhooks;

/// <summary>
/// Replay-protection marker for inbound webhook callbacks (05 §3.4, 09 §11.3).
/// A row is inserted by <c>WebhookSignatureMiddleware</c> the first time a
/// (Source, Nonce) pair is seen; subsequent requests with the same pair are
/// rejected with 401. Rows expire — <c>ProcessedNonceCleanupJob</c> hard-purges
/// entries past <see cref="ExpiresAt"/> so the table does not grow unbounded.
/// </summary>
/// <remarks>
/// Append-only per 06 §4.2: the row is a one-shot marker; updates make no
/// semantic sense and would defeat the replay guarantee.
/// </remarks>
public class ProcessedNonce : IAppendOnly
{
    public Guid Id { get; set; }

    /// <summary>
    /// Source discriminator (e.g. <c>steam-sidecar</c>). Lets a future
    /// blockchain or notification sidecar share the table without nonce
    /// collisions.
    /// </summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>
    /// The <c>X-Nonce</c> header value sent by the caller. UUID v4 string per
    /// 09 §17.5 but the column is opaque text so other formats remain valid.
    /// </summary>
    public string Nonce { get; set; } = string.Empty;

    /// <summary>UTC timestamp at which the nonce was accepted.</summary>
    public DateTime ProcessedAt { get; set; }

    /// <summary>UTC timestamp after which the row may be purged.</summary>
    public DateTime ExpiresAt { get; set; }
}
