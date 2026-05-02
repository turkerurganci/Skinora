using Skinora.Shared.Enums;

namespace Skinora.Transactions.Application.Lifecycle;

/// <summary>
/// Pre-create fraud screen invoked by <c>POST /transactions</c> (T45 — 02 §14.4,
/// 03 §2.2 step 17). Compares the seller-quoted price against the latest
/// market price and decides whether the transaction enters the FLAGGED state
/// (admin review) instead of CREATED. The decision is persisted on the
/// outgoing <c>Transaction</c> row but no <c>FraudFlag</c> row is written
/// here — that is the orchestrator's responsibility once the entity is
/// inserted (so <c>TransactionId</c> can be back-filled inside the same
/// SaveChanges).
/// </summary>
public interface IFraudPreCheckService
{
    Task<FraudPreCheckOutcome> EvaluateAsync(
        string itemClassId,
        string? itemInstanceId,
        StablecoinType stablecoin,
        decimal quotedPrice,
        CancellationToken cancellationToken);
}

/// <summary>Result of a single fraud pre-check.</summary>
/// <param name="ShouldFlag">
/// <c>true</c> when the deviation crosses the configured threshold; otherwise
/// the caller persists status <c>CREATED</c> with no flag row.
/// </param>
/// <param name="MarketPrice">
/// Resolved market price snapshotted onto <c>Transaction.MarketPriceAtCreation</c>
/// (06 §3.5). <c>null</c> when no market signal is available — fraud check is
/// then a no-op (02 §14.4 only flags when an actual deviation exists).
/// </param>
/// <param name="DeviationRatio">
/// <c>|quoted − market| / market</c>. <c>null</c> when <see cref="MarketPrice"/>
/// is null or zero.
/// </param>
/// <param name="ThresholdRatio">
/// Configured <c>price_deviation_threshold</c>. <c>null</c> when the setting
/// is unconfigured (rule effectively disabled).
/// </param>
public sealed record FraudPreCheckOutcome(
    bool ShouldFlag,
    decimal? MarketPrice,
    decimal? DeviationRatio,
    decimal? ThresholdRatio);
