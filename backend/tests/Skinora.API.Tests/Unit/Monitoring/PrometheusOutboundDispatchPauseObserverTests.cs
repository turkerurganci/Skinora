using Skinora.API.Monitoring;
using Skinora.Transactions.Application.Transfers;

namespace Skinora.API.Tests.Unit.Monitoring;

/// <summary>
/// T172a doğrulama S4-4 — the stop switch gauge and the
/// <c>outbound-dispatch-paused</c> rule that reads it. The rule is read from
/// the provisioning file and checked against the gauge's real name (a rule
/// naming a metric nobody exports never fires — <c>or vector(0)</c>).
/// </summary>
[Trait("Category", "Unit")]
public sealed class PrometheusOutboundDispatchPauseObserverTests
{
    [Fact]
    public void A_Report_Sets_The_Gauge()
    {
        IOutboundDispatchPauseObserver observer = new PrometheusOutboundDispatchPauseObserver();

        observer.Report(true);
        Assert.Equal(1, PrometheusOutboundDispatchPauseObserver.Paused.Value);

        observer.Report(false);
        Assert.Equal(0, PrometheusOutboundDispatchPauseObserver.Paused.Value);
    }

    [Fact]
    public void The_Rule_Fires_When_The_Pause_Stays_On_For_30_Minutes()
    {
        var block = RuleBlock("outbound-dispatch-paused");

        Assert.Contains($"max({PrometheusOutboundDispatchPauseObserver.Paused.Name})", block);
        Assert.Matches(@"type: gt\s+params: \[0\]", block);
        Assert.Contains("for: 30m", block);
        Assert.Contains("severity: warning", block);
        Assert.Contains("category: money", block); // T165: money alarms reach Telegram + e-mail
    }

    private static string RuleBlock(string uid)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "docker-compose.yml")))
        {
            dir = dir.Parent;
        }
        if (dir is null) throw new InvalidOperationException("Repository root (docker-compose.yml) not found.");

        var text = File.ReadAllText(Path.Combine(
            dir.FullName, "infra", "grafana", "provisioning", "alerting", "rules.yml")).Replace("\r\n", "\n");
        var start = text.IndexOf($"- uid: {uid}\n", StringComparison.Ordinal);
        Assert.True(start >= 0, $"rules.yml has no rule {uid}");
        var next = text.IndexOf("\n      - uid: ", start + 1, StringComparison.Ordinal);
        return next < 0 ? text[start..] : text[start..next];
    }
}
