using Skinora.Shared.Enums;

namespace Skinora.Transactions.Application.Lifecycle;

/// <summary>
/// Pre-create fraud screen invoked by <c>POST /transactions</c> (T45 / T55 —
/// 02 §14.4, 03 §2.2 step 17, 03 §7.1–§7.2). Evaluates the seller-quoted
/// transaction against the AML rule set and decides whether the transaction
/// should enter the FLAGGED state (admin review) instead of CREATED.
/// </summary>
/// <remarks>
/// <para>
/// The pre-check service runs three rules in priority order, returning the
/// first match (single FraudFlag per FLAGGED transaction):
/// <list type="number">
///   <item><b>PRICE_DEVIATION</b> — quoted vs market price ratio crosses
///         <c>price_deviation_threshold</c>.</item>
///   <item><b>HIGH_VOLUME</b> — seller's recent count or amount crosses
///         <c>high_volume_count_threshold</c> / <c>high_volume_amount_threshold</c>
///         within <c>high_volume_period_hours</c>.</item>
///   <item><b>ABNORMAL_BEHAVIOR</b> (dormant) — never-traded account older
///         than <c>dormant_account_min_age_days</c> attempts a transaction
///         above <c>dormant_account_value_threshold</c>.</item>
/// </list>
/// </para>
/// <para>
/// The decision is persisted on the outgoing <c>Transaction</c> row and the
/// matching <c>FraudFlag</c> row is staged by the orchestrator inside the
/// same SaveChanges so an admin can never observe a FLAGGED transaction
/// without its flag (T54 atomicity contract).
/// </para>
/// </remarks>
public interface IFraudPreCheckService
{
    Task<FraudPreCheckOutcome> EvaluateAsync(
        Guid sellerId,
        string itemClassId,
        string? itemInstanceId,
        StablecoinType stablecoin,
        decimal quotedPrice,
        DateTime nowUtc,
        CancellationToken cancellationToken);
}

/// <summary>Result of the pre-create fraud screen.</summary>
/// <param name="ShouldFlag">
/// <c>true</c> when any rule trips. The caller persists status <c>FLAGGED</c>
/// and stages a <c>FraudFlag</c> row using <see cref="FlagType"/> /
/// <see cref="FlagDetailsJson"/>.
/// </param>
/// <param name="MarketPrice">
/// Resolved market price snapshotted onto <c>Transaction.MarketPriceAtCreation</c>
/// (06 §3.5). <c>null</c> when no market signal is available — the
/// PRICE_DEVIATION rule is then a no-op (02 §14.4 only flags when an actual
/// deviation exists). HIGH_VOLUME and ABNORMAL_BEHAVIOR rules still apply.
/// </param>
/// <param name="FlagType">
/// The flag type written to <c>FraudFlag.Type</c> when <see cref="ShouldFlag"/>
/// is <c>true</c>; <c>null</c> otherwise. The orchestrator surfaces the enum
/// name as the API <c>flagReason</c> field (07 §7.2).
/// </param>
/// <param name="FlagDetailsJson">
/// Serialized 07 §9.3 detail payload — shape depends on <see cref="FlagType"/>
/// (matches <c>PriceDeviationFlagDetail</c>, <c>HighVolumeFlagDetail</c> or
/// <c>AbnormalBehaviorFlagDetail</c> in the Skinora.Fraud module — the pre-check
/// service builds the JSON inline so this assembly stays free of a Fraud
/// project reference). <c>null</c> when no flag was raised.
/// </param>
public sealed record FraudPreCheckOutcome(
    bool ShouldFlag,
    decimal? MarketPrice,
    FraudFlagType? FlagType,
    string? FlagDetailsJson);
