namespace Skinora.Transactions.Application.Transfers;

/// <summary>
/// T172a doğrulama S4-4 — visibility of the dispatcher's stop switch
/// (<see cref="OutboundDispatchPause"/>). The dispatcher reports it at the start
/// of every run; the API publishes it as a Prometheus gauge and the
/// <c>outbound-dispatch-paused</c> rule alarms when it stays on: a pause
/// forgotten after a restore holds every payout, refund and sweep.
/// </summary>
public interface IOutboundDispatchPauseObserver
{
    /// <param name="paused">true while the dispatcher sends nothing (a missing row counts as paused).</param>
    void Report(bool paused);
}

/// <summary>Observer that records nothing (tests, hosts without metrics).</summary>
public sealed class NullOutboundDispatchPauseObserver : IOutboundDispatchPauseObserver
{
    public static readonly NullOutboundDispatchPauseObserver Instance = new();

    public void Report(bool paused)
    {
    }
}
