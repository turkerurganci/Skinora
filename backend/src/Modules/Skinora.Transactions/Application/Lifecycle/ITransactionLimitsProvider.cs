namespace Skinora.Transactions.Application.Lifecycle;

/// <summary>
/// Read port over the eligibility-related SystemSetting rows. Exposed as a
/// separate interface from <see cref="ITransactionParamsService"/> because
/// eligibility checks need fields not surfaced by the form-params response
/// (e.g. <c>max_concurrent_transactions</c>, <c>new_account_period_days</c>).
/// Mirrors the T43 <c>IReputationThresholdsProvider</c> pattern — composite
/// thresholds packaged into one round-trip.
/// </summary>
public interface ITransactionLimitsProvider
{
    Task<TransactionLimits> GetAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Snapshot of the SystemSetting rows the eligibility + creation pipeline
/// reads (02 §8, §12.3, §14.3, §16.2). Each field is independently nullable
/// so a partial bootstrap does not nuke unrelated rules — a missing
/// <c>max_concurrent_transactions</c> means "concurrent limit not enforced",
/// not "limit zero".
/// </summary>
public sealed record TransactionLimits(
    int? MaxConcurrent,
    int? NewAccountTransactionLimit,
    int? NewAccountPeriodDays,
    int? PayoutAddressCooldownHours,
    int? AcceptTimeoutMinutes,
    int? PaymentTimeoutMinMinutes,
    int? PaymentTimeoutMaxMinutes,
    int? PaymentTimeoutDefaultMinutes,
    decimal? CommissionRate,
    decimal? MinTransactionAmount,
    decimal? MaxTransactionAmount,
    bool OpenLinkEnabled);
