namespace Skinora.Realtime.Application.Countdown;

/// <summary>
/// Tunables for the <see cref="CountdownSyncBroadcaster"/>
/// (T61 — 07 §11.1 RT1 <c>CountdownSync</c>, 04 §7.3 detail-page countdown).
/// </summary>
public sealed class CountdownSyncOptions
{
    public const string SectionName = "Realtime:CountdownSync";

    /// <summary>
    /// Interval between successive sweeps over active transactions.
    /// 07 §11.1 mandates a 30-second cadence.
    /// </summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Set to <c>false</c> to disable the broadcaster entirely (e.g. integration
    /// tests where the broadcaster is exercised manually).
    /// </summary>
    public bool Enabled { get; set; } = true;
}
