namespace Skinora.Users.Application.Reputation;

/// <summary>
/// Read port for the three SystemSettings that drive the 02 §14.2 non-delivery
/// sanction (flag on the first repeat, automatic suspension on the next). Split
/// from <see cref="ICancelCooldownThresholdsProvider"/> on purpose: the cancel
/// cooldown counts every responsible cancellation and blocks for hours, this
/// rule counts only the seller failing a buyer who has already paid and ends in
/// a flag or a suspension — sharing one snapshot would invite one rule's knobs
/// to be read as the other's.
/// </summary>
public interface INonDeliveryAbuseThresholdsProvider
{
    Task<NonDeliveryAbuseThresholds> GetAsync(CancellationToken cancellationToken);
}

/// <summary>Snapshot of the three non-delivery SystemSettings (02 §16.2 "Teslimat ihlali eşikleri").</summary>
/// <param name="WindowDays">non_delivery_window_days — rolling window the events are counted in.</param>
/// <param name="FlagCount">non_delivery_flag_count — the event count that stages an <c>ABNORMAL_BEHAVIOR</c> account flag.</param>
/// <param name="SuspendCount">non_delivery_suspend_count — the event count that suspends the account until an admin lifts it.</param>
public sealed record NonDeliveryAbuseThresholds(int WindowDays, int FlagCount, int SuspendCount)
{
    /// <summary>
    /// The rule runs only when all three values are positive. A zero means the
    /// row is missing or unparseable; treating that as "disabled" keeps a
    /// half-bootstrapped environment from suspending sellers on a default
    /// nobody chose.
    /// </summary>
    public bool IsEnabled => WindowDays > 0 && FlagCount > 0 && SuspendCount > 0;
}
