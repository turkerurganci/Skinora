using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Skinora.API.Monitoring;
using Skinora.Shared.Enums;

namespace Skinora.API.Tests.Unit.Monitoring;

/// <summary>
/// T152 düzeltme turu — the per-network outbound queue gauges and the
/// <c>outbound-transfer-stuck</c> rule that reads them. A rule naming a metric
/// nobody exports never fires and never errors (<c>or vector(0)</c>), so the
/// rule is read from the provisioning file and checked against the names the
/// gauges actually carry.
/// </summary>
[Trait("Category", "Unit")]
public sealed class PrometheusOutboundQueueObserverTests
{
    [Fact]
    public void Report_Sets_Each_Networks_Series_And_Publishes_The_Threshold_Only_Where_One_Exists()
    {
        var observer = new PrometheusOutboundQueueObserver();

        observer.Report(BlockchainNetwork.ETHEREUM, 4, 12_000d, 10_800d);
        observer.Report(BlockchainNetwork.TRON, 1, 60d, null);

        Assert.Equal(4, PrometheusOutboundQueueObserver.PendingRows.WithLabels("ETHEREUM").Value);
        Assert.Equal(12_000d, PrometheusOutboundQueueObserver.OldestPendingSeconds.WithLabels("ETHEREUM").Value);
        Assert.Equal(10_800d, PrometheusOutboundQueueObserver.AlarmSeconds.WithLabels("ETHEREUM").Value);
        Assert.Equal(1, PrometheusOutboundQueueObserver.PendingRows.WithLabels("TRON").Value);
        Assert.DoesNotContain(
            PrometheusOutboundQueueObserver.AlarmSeconds.GetAllLabelValues(),
            labels => labels.SequenceEqual(["TRON"]));
    }

    [Fact]
    public void The_Stuck_Rule_Divides_The_Oldest_Age_By_The_Published_Threshold_Per_Network()
    {
        var block = RuleBlock("outbound-transfer-stuck");

        Assert.Contains(
            $"max by (network) ({PrometheusOutboundQueueObserver.OldestPendingSeconds.Name}) / max by (network) ({PrometheusOutboundQueueObserver.AlarmSeconds.Name})",
            block);
        Assert.Matches(@"type: gt\s+params: \[1\]", block);
    }

    [Fact]
    public void T158a_Publishes_The_Row_Count_Threshold_Per_Network_Default_25_And_Configurable()
    {
        new PrometheusOutboundQueueObserver().Report(BlockchainNetwork.TRON, 3, 10d, null);
        Assert.Equal(25d, PrometheusOutboundQueueObserver.PendingRowsAlarmThreshold.WithLabels("TRON").Value);

        new PrometheusOutboundQueueObserver(Options.Create(new OutboundQueueAlarmOptions { PendingRowsThreshold = 40 }))
            .Report(BlockchainNetwork.ETHEREUM, 3, 10d, 10_800d);
        Assert.Equal(40d, PrometheusOutboundQueueObserver.PendingRowsAlarmThreshold.WithLabels("ETHEREUM").Value);

        // A non-positive value would page on an empty queue — the default stands.
        new PrometheusOutboundQueueObserver(Options.Create(new OutboundQueueAlarmOptions { PendingRowsThreshold = 0 }))
            .Report(BlockchainNetwork.ETHEREUM, 3, 10d, 10_800d);
        Assert.Equal(25d, PrometheusOutboundQueueObserver.PendingRowsAlarmThreshold.WithLabels("ETHEREUM").Value);
    }

    [Fact]
    public void T158a_The_Backlog_Rule_Divides_The_Row_Count_By_The_Published_Threshold_Per_Network()
    {
        var block = RuleBlock("outbound-transfer-backlog");

        Assert.Contains(
            $"max by (network) ({PrometheusOutboundQueueObserver.PendingRows.Name}) / max by (network) ({PrometheusOutboundQueueObserver.PendingRowsAlarmThreshold.Name})",
            block);
        Assert.Matches(@"type: gt\s+params: \[1\]", block);
    }

    [Fact]
    public void T158i_The_Reconciliation_Gauge_Marks_A_Failed_Round_Until_A_Later_One_Completes()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 19, 3, 0, 0, TimeSpan.Zero));
        var observer = new PrometheusReconciliationRunObserver(clock);

        observer.Report(BlockchainNetwork.ETHEREUM, succeeded: false);
        Assert.Equal(1, PrometheusReconciliationRunObserver.LastRunFailed.WithLabels("ETHEREUM").Value);

        observer.Report(BlockchainNetwork.ETHEREUM, succeeded: true);
        Assert.Equal(0, PrometheusReconciliationRunObserver.LastRunFailed.WithLabels("ETHEREUM").Value);
        Assert.Equal(clock.GetUtcNow().ToUnixTimeSeconds(),
            PrometheusReconciliationRunObserver.LastSuccess.WithLabels("ETHEREUM").Value);

        var block = RuleBlock("reconciliation-network-failed");
        Assert.Contains($"max by (network) ({PrometheusReconciliationRunObserver.LastRunFailed.Name})", block);
        Assert.Matches(@"type: gt\s+params: \[0\]", block);
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
