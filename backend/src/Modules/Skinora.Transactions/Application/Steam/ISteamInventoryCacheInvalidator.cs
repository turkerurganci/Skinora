namespace Skinora.Transactions.Application.Steam;

/// <summary>
/// Best-effort invalidation hook for the sidecar inventory cache (08 §2.3 —
/// Redis 2-minute TTL drops cached envelopes on transaction create / trade
/// offer terminal events). Modeled as a port so production wires the
/// sidecar-backed implementation while tests stay on the no-op default
/// without spinning up an HTTP client.
/// </summary>
/// <remarks>
/// Failure semantics: implementations <b>must not</b> throw. The cache is an
/// optimization, not a correctness boundary — a missed invalidation only
/// shortens the next response by up to 2 minutes and never breaks the user
/// flow. Callers fire-and-await without surrounding error handling.
/// </remarks>
public interface ISteamInventoryCacheInvalidator
{
    /// <summary>Drop the cached inventory for <paramref name="steamId"/>.</summary>
    Task InvalidateAsync(string steamId, CancellationToken cancellationToken);
}
