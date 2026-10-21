using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Skinora.API.Configuration;
using Skinora.API.Monitoring;
using Skinora.Shared.Enums;
using Skinora.Transactions.Application.GasFee;

namespace Skinora.API.Tests.Unit.Monitoring;

/// <summary>
/// T160b — the fallback counter and the <c>gas-fee-fallback-charged</c> rule
/// that reads it. The rule is read from the provisioning file and checked
/// against the counter's real name (a rule naming a metric nobody exports
/// never fires and never errors — <c>or vector(0)</c>).
/// </summary>
[Trait("Category", "Unit")]
public sealed class PrometheusGasFeeFallbackObserverTests
{
    [Fact]
    public void Every_Series_Exists_At_Zero_Before_The_First_Fallback()
    {
        // increase() cannot see a counter whose first sample is already 1, so
        // a series born by the first fallback would never page.
        _ = new PrometheusGasFeeFallbackObserver();

        foreach (var network in Enum.GetValues<BlockchainNetwork>())
            foreach (var flow in new[] { ChargedGasFeeResolver.RefundFlow, ChargedGasFeeResolver.HotWalletRefundFlow, ChargedGasFeeResolver.PayoutFlow })
                foreach (var reason in Enum.GetValues<GasFeeFallbackReason>())
                    foreach (var basis in new[] { "measured", "static" })
                    {
                        Assert.Contains(
                            PrometheusGasFeeFallbackObserver.FallbackCharged.GetAllLabelValues(),
                            labels => labels.SequenceEqual(
                                [network.ToString(), flow, PrometheusGasFeeFallbackObserver.Label(reason), basis]));
                    }
    }

    [Fact]
    public void B2_The_Host_Builds_The_Observer_At_Startup_Not_On_The_First_Refund()
    {
        // T160b düzeltme turu (B2): without a startup consumer the observer is
        // built by the first ChargedGasFeeResolver, i.e. by the first refund
        // or payout; a fallback then would create its series at 1 and
        // increase() would never see it. A hosted service takes the observer
        // as a dependency; the host builds hosted services at startup.
        var services = new ServiceCollection();
        services.AddTransactionsModule(new ConfigurationBuilder().AddInMemoryCollection().Build());

        var primer = Assert.Single(
            services,
            d => d.ServiceType == typeof(IHostedService)
                && d.ImplementationType == typeof(GasFeeFallbackSeriesPrimer));
        using var provider = services.BuildServiceProvider();
        var started = (GasFeeFallbackSeriesPrimer)ActivatorUtilities.CreateInstance(provider, primer.ImplementationType!);

        Assert.IsType<PrometheusGasFeeFallbackObserver>(started.Observer);
        Assert.Same(provider.GetRequiredService<IGasFeeFallbackObserver>(), started.Observer);
    }

    [Fact]
    public void A_Fallback_Increments_Its_Network_Flow_Reason_And_Basis()
    {
        IGasFeeFallbackObserver observer = new PrometheusGasFeeFallbackObserver();
        var series = PrometheusGasFeeFallbackObserver.FallbackCharged
            .WithLabels("ETHEREUM", ChargedGasFeeResolver.RefundFlow, "unavailable", "static");
        var other = PrometheusGasFeeFallbackObserver.FallbackCharged
            .WithLabels("ETHEREUM", ChargedGasFeeResolver.RefundFlow, "unavailable", "measured");
        var before = series.Value;
        var otherBefore = other.Value;

        observer.FallbackCharged(
            BlockchainNetwork.ETHEREUM, ChargedGasFeeResolver.RefundFlow, GasFeeFallbackReason.Unavailable, measured: false);

        Assert.Equal(before + 1, series.Value);
        Assert.Equal(otherBefore, other.Value);
    }

    [Fact]
    public void The_Rule_Fires_On_Any_Increase_Of_The_Counter_Per_Network_Flow_And_Basis()
    {
        var block = RuleBlock("gas-fee-fallback-charged");

        Assert.Contains(
            $"sum by (network, flow, basis) (increase({PrometheusGasFeeFallbackObserver.FallbackCharged.Name}[1h]))",
            block);
        Assert.Matches(@"type: gt\s+params: \[0\]", block);
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
