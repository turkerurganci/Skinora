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
    /// Resolve the indicative market price for the given item identity and
    /// stablecoin denomination. Returning <c>null</c> means the platform has
    /// no comparable price signal — the fraud pre-check treats this as
    /// "deviation unknown" and lets the transaction proceed as <c>CREATED</c>
    /// (per 02 §14.4 wording — flag triggers only when the threshold is
    /// breached, never on missing data).
    /// </summary>
    Task<decimal?> TryGetMarketPriceAsync(
        string itemClassId,
        string? itemInstanceId,
        StablecoinType denomination,
        CancellationToken cancellationToken);
}
