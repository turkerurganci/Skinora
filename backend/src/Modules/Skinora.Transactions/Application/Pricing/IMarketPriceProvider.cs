using Skinora.Shared.Enums;

namespace Skinora.Transactions.Application.Pricing;

/// <summary>
/// Read port for the latest tradeable market price of a CS2 item, used by the
/// fraud pre-check during transaction creation (T45 — 02 §14.4, 03 §2.2 step 17).
/// The real implementation calls the Steam Market Price API (T81 — 11 plan)
/// and is swapped in via DI without touching the fraud pre-check pipeline.
/// </summary>
public interface IMarketPriceProvider
{
    /// <summary>
    /// Resolve the indicative market price for the given Steam
    /// <paramref name="marketHashName"/> (e.g. "AK-47 | Redline (Field-Tested)")
    /// and stablecoin denomination. Returning <c>null</c> means the platform has
    /// no comparable price signal — the fraud pre-check treats this as
    /// "deviation unknown" and lets the transaction proceed as <c>CREATED</c>
    /// (per 02 §14.4 wording — flag triggers only when the threshold is
    /// breached, never on missing data).
    /// </summary>
    /// <remarks>
    /// The underlying Steam Market stack (T81) keys strictly on
    /// <c>marketHashName</c>; the display item name is not a valid key. The
    /// <paramref name="denomination"/> is accepted for seam completeness but the
    /// stack quotes a single pinned currency (USD), treated 1:1 with the
    /// stablecoin (WP4a owner decision; the wide deviation threshold absorbs the
    /// micro-variance — 08 §7.3).
    /// </remarks>
    Task<decimal?> TryGetMarketPriceAsync(
        string marketHashName,
        StablecoinType denomination,
        CancellationToken cancellationToken);
}
