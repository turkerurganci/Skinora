using Skinora.API.Monitoring;
using Skinora.Shared.Enums;
using Skinora.Transactions.Application.Transfers;

namespace Skinora.API.Tests.Unit.Monitoring;

/// <summary>
/// T169a — the deposit refund daily limit gauges and the
/// <c>deposit-refund-daily-limit-held</c> rule that reads them. The rule is
/// read from the provisioning file and checked against the gauge's real name
/// (a rule naming a metric nobody exports never fires — <c>or vector(0)</c>).
/// </summary>
[Trait("Category", "Unit")]
public sealed class PrometheusDepositRefundLimitObserverTests
{
    [Fact]
    public void A_Report_Sets_The_Networks_Series()
    {
        IDepositRefundLimitObserver observer = new PrometheusDepositRefundLimitObserver();

        observer.Report(BlockchainNetwork.ETHEREUM, 2, 87.65m, 100m);
        observer.Report(BlockchainNetwork.TRON, 0, 12.5m, 75m);

        Assert.Equal(2, PrometheusDepositRefundLimitObserver.HeldRows.WithLabels("ETHEREUM").Value);
        Assert.Equal(87.65, PrometheusDepositRefundLimitObserver.UsedUsdt.WithLabels("ETHEREUM").Value, 6);
        Assert.Equal(100, PrometheusDepositRefundLimitObserver.LimitUsdt.WithLabels("ETHEREUM").Value);
        Assert.Equal(0, PrometheusDepositRefundLimitObserver.HeldRows.WithLabels("TRON").Value);
        Assert.Equal(75, PrometheusDepositRefundLimitObserver.LimitUsdt.WithLabels("TRON").Value);
    }

    [Fact]
    public void The_Rule_Fires_While_Any_Refund_Is_Held_Per_Network()
    {
        var block = RuleBlock("deposit-refund-daily-limit-held");

        Assert.Contains(
            $"max by (network) ({PrometheusDepositRefundLimitObserver.HeldRows.Name})",
            block);
        Assert.Matches(@"type: gt\s+params: \[0\]", block);
        Assert.Contains("for: 0m", block);
        Assert.Contains("severity: warning", block);
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
