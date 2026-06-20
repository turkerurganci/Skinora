using Skinora.API.Monitoring;

namespace Skinora.API.Tests.Unit.Monitoring;

/// <summary>
/// WP16 — edge-detection coverage for <see cref="PlatformHealthMonitorState"/>.
/// The probe must alert exactly once on healthy → degraded and once on
/// degraded → healthy, never on every failing poll (05 §4.4, 02 §3.3).
/// </summary>
[Trait("Category", "Unit")]
public sealed class PlatformHealthMonitorStateTests
{
    private const string Component = PlatformComponents.Steam;
    private const int Threshold = 3;

    [Fact]
    public void Failures_Below_Threshold_Do_Not_Alert()
    {
        var state = new PlatformHealthMonitorState();

        Assert.Equal(HealthTransition.None, state.Record(Component, healthy: false, Threshold));
        Assert.Equal(HealthTransition.None, state.Record(Component, healthy: false, Threshold));
        Assert.Equal(2, state.ConsecutiveFailures(Component));
    }

    [Fact]
    public void Crossing_Threshold_Reports_Degraded_Once()
    {
        var state = new PlatformHealthMonitorState();

        state.Record(Component, healthy: false, Threshold);
        state.Record(Component, healthy: false, Threshold);
        Assert.Equal(HealthTransition.Degraded, state.Record(Component, healthy: false, Threshold));

        // Still failing → already degraded → no repeat alert.
        Assert.Equal(HealthTransition.None, state.Record(Component, healthy: false, Threshold));
        Assert.Equal(HealthTransition.None, state.Record(Component, healthy: false, Threshold));
    }

    [Fact]
    public void Recovery_After_Degraded_Reports_Recovered_Once()
    {
        var state = new PlatformHealthMonitorState();
        for (var i = 0; i < Threshold; i++) state.Record(Component, healthy: false, Threshold);

        Assert.Equal(HealthTransition.Recovered, state.Record(Component, healthy: true, Threshold));
        Assert.Equal(0, state.ConsecutiveFailures(Component));

        // Already healthy → no repeat recovery alert.
        Assert.Equal(HealthTransition.None, state.Record(Component, healthy: true, Threshold));
    }

    [Fact]
    public void Healthy_Without_Prior_Outage_Is_Noise_Free()
    {
        var state = new PlatformHealthMonitorState();
        Assert.Equal(HealthTransition.None, state.Record(Component, healthy: true, Threshold));
    }

    [Fact]
    public void A_Single_Recovery_Resets_The_Failure_Run()
    {
        var state = new PlatformHealthMonitorState();
        state.Record(Component, healthy: false, Threshold);
        state.Record(Component, healthy: false, Threshold);

        // Brief blip recovers before the threshold → counter resets, no alert.
        Assert.Equal(HealthTransition.None, state.Record(Component, healthy: true, Threshold));

        // The next failure run must start from zero.
        Assert.Equal(HealthTransition.None, state.Record(Component, healthy: false, Threshold));
        Assert.Equal(1, state.ConsecutiveFailures(Component));
    }

    [Fact]
    public void Revert_Degraded_Lets_Next_Failing_Probe_Redetect()
    {
        var state = new PlatformHealthMonitorState();
        for (var i = 0; i < Threshold; i++) state.Record(Component, healthy: false, Threshold);
        // Degraded was reported but its durable alert failed → revert.
        state.Revert(Component, HealthTransition.Degraded);

        // The next failing probe must re-report Degraded (alert not swallowed).
        Assert.Equal(HealthTransition.Degraded, state.Record(Component, healthy: false, Threshold));
    }

    [Fact]
    public void Revert_Recovered_Lets_Next_Healthy_Probe_Redetect()
    {
        var state = new PlatformHealthMonitorState();
        for (var i = 0; i < Threshold; i++) state.Record(Component, healthy: false, Threshold);
        state.Record(Component, healthy: true, Threshold); // Recovered
        // Recovery alert failed to persist → revert.
        state.Revert(Component, HealthTransition.Recovered);

        // The next healthy probe must re-report Recovered.
        Assert.Equal(HealthTransition.Recovered, state.Record(Component, healthy: true, Threshold));
    }

    [Fact]
    public void Revert_None_Is_A_Noop()
    {
        var state = new PlatformHealthMonitorState();
        state.Record(Component, healthy: false, Threshold); // below threshold, None
        state.Revert(Component, HealthTransition.None);

        // Counter untouched; still below threshold.
        Assert.Equal(1, state.ConsecutiveFailures(Component));
        Assert.Equal(HealthTransition.None, state.Record(Component, healthy: false, Threshold));
    }

    [Fact]
    public void Components_Are_Tracked_Independently()
    {
        var state = new PlatformHealthMonitorState();
        for (var i = 0; i < Threshold; i++)
            state.Record(PlatformComponents.Steam, healthy: false, Threshold);

        // Blockchain is still healthy — Steam's outage must not bleed across.
        Assert.Equal(HealthTransition.None, state.Record(PlatformComponents.Blockchain, healthy: true, Threshold));
        Assert.Equal(0, state.ConsecutiveFailures(PlatformComponents.Blockchain));
        Assert.Equal(Threshold, state.ConsecutiveFailures(PlatformComponents.Steam));
    }
}
