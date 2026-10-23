using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Skinora.API.Monitoring;
using Skinora.API.Services.HotWallet;
using Skinora.Transactions.Application.Reconciliation;
using Skinora.Shared.Enums;
using Skinora.Shared.Notifications;

namespace Skinora.API.Tests.Unit.Monitoring;

/// <summary>
/// T165 — the Grafana side of the notification channels and money alarms:
/// every rule reads a series the backend really publishes, money rules carry
/// <c>category: money</c>, the policy routes that label first to a contact
/// point with BOTH Telegram and e-mail, and the message templates are plain
/// Grafana templates (the T16 Helm-style escaping printed them literally).
/// A rule naming a metric nobody exports never fires and never errors.
/// </summary>
[Trait("Category", "Unit")]
public sealed class NotificationAlertingProvisioningTests
{
    /// <summary>Every rule that is about money (chain, sweep, payout, refund, wallet, fee).</summary>
    public static readonly string[] MoneyRules =
    [
        // T165
        "outbound-awaiting-approval",
        "hot-wallet-threshold-breached",
        "admin-money-alarm-raised",
        // T165 düzeltme turu — daily ledger reconciliation mismatch (05 §3.3)
        "reconciliation-mismatch",
        // existing chain / payment / wallet / fee rules
        "blockchain-sidecar-down",
        "ethereum-finalized-stalled",
        "ethereum-deposit-balance-mismatch",
        "ethereum-usdt-status",
        "ethereum-nonce-conflict",
        "ethereum-journal-behind-chain",
        "ethereum-journal-ack-ahead-of-chain",
        "ethereum-monitor-scan-failing",
        "ethereum-webhook-signature-refused",
        "ethereum-transfer-status-unverified",
        "ethereum-gas-estimate-abnormal",
        "tron-api-errors-high",
        "tron-quota-rejections",
        "tron-quota-projection",
        "ethereum-deposit-native-eth",
        "ethereum-reconciliation-stale",
        "ethereum-transfer-wait",
        "ethereum-wallet-eth-low",
        "outbound-transfer-stuck",
        "ethereum-rpc-latency-high",
        "outbound-transfer-backlog",
        "reconciliation-network-failed",
        "gas-fee-fallback-charged",
        // T169a — refund held by the deposit refund daily limit (⚠ PARA;
        // classified when main was merged into T165)
        "deposit-refund-daily-limit-held",
        // T170 — per-network monitoring (group "Skinora Network Alerts")
        "blockchain-service-degraded",
        "tron-solid-block-stalled",
        "ethereum-rpc-provider-failing",
        "hot-wallet-monitor-stale",
        "tron-energy-delegation-stranded",
    ];

    /// <summary>
    /// Every rule that is deliberately NOT a money alarm. Together with
    /// <see cref="MoneyRules"/> it must name every rule in rules.yml, so a rule
    /// arriving from another branch cannot skip the money/non-money decision
    /// (the T169a refund-limit rule merged in without its <c>category: money</c>).
    /// </summary>
    public static readonly string[] NonMoneyRules =
    [
        "backend-down",
        "high-error-rate-backend",
        "high-response-time-backend",
        "steam-sidecar-down",
        "steam-api-errors-high",
        "notification-channel-misconfigured",
        "notification-delivery-exhausted",
        "db-backup-full-stale",
        "db-backup-log-stale",
        "db-data-size-warning",
        "db-data-size-critical",
        "db-monitor-probe-failing",
    ];

    [Fact]
    public void EveryRule_IsClassified_AsMoneyOrNotMoney()
    {
        var all = Regex.Matches(Read("rules.yml"), @"\n      - uid: ([a-z0-9-]+)\n")
            .Select(m => m.Groups[1].Value)
            .ToList();

        Assert.Equal(all.Count, all.Distinct().Count());
        Assert.Empty(MoneyRules.Intersect(NonMoneyRules));
        Assert.Equal(all.ToHashSet(), MoneyRules.Concat(NonMoneyRules).ToHashSet());
        Assert.Equal(33, MoneyRules.Length);
    }

    [Theory]
    [MemberData(nameof(MoneyRuleIds))]
    public void MoneyRule_CarriesTheMoneyCategory(string uid)
    {
        Assert.Matches(@"\n        labels:\n          severity: (critical|warning)\n          category: money\n", RuleBlock(uid));
    }

    public static TheoryData<string> MoneyRuleIds()
    {
        var data = new TheoryData<string>();
        foreach (var uid in MoneyRules) data.Add(uid);
        return data;
    }

    [Fact]
    public void OnlyTheListedRules_AreMoneyRules()
    {
        // A new money rule must be added to MoneyRules (and so be tested), a
        // non-money rule must not quietly join the money contact point.
        var text = Read("rules.yml");
        var labelled = Regex.Matches(text, @"- uid: ([a-z0-9-]+)\n(?:(?!\n      - uid: ).)*?category: money", RegexOptions.Singleline)
            .Select(m => m.Groups[1].Value)
            .ToHashSet();

        Assert.Equal(MoneyRules.ToHashSet(), labelled);
    }

    [Fact]
    public void Policy_RoutesMoneyFirst_ToTheMoneyContactPoint_WithoutContinue()
    {
        var text = Read("policies.yml");
        var routes = text[text.IndexOf("    routes:\n", StringComparison.Ordinal)..];

        Assert.StartsWith(
            "    routes:\n      - receiver: money-alarm\n        matchers:\n          - category = money\n",
            routes);
        var moneyRoute = routes[..routes.IndexOf("      - receiver: telegram-critical", StringComparison.Ordinal)];
        Assert.DoesNotContain("continue", moneyRoute);
        Assert.Contains("repeat_interval: 4h", moneyRoute);
    }

    [Fact]
    public void MoneyContactPoint_HasTelegramAndEmail()
    {
        var text = Read("contactpoints.yml");
        var start = text.IndexOf("    name: money-alarm\n", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var block = text[start..];

        Assert.Contains("type: telegram", block);
        Assert.Contains("chatid: '${TELEGRAM_CHAT_ID}'", block);
        Assert.Contains("type: email", block);
        Assert.Contains("addresses: '${ALERT_EMAIL_TO}'", block);
    }

    [Fact]
    public void ContactPointTemplates_ArePlainGrafanaTemplates()
    {
        var text = Read("contactpoints.yml");

        // The T16 {{ `{{ … }}` }} escaping made Grafana print the template text.
        Assert.DoesNotContain("{{ `", text);
        Assert.Contains("{{ if .Alerts.Firing }}", text);
        // Grafana's provisioning interpolates $NAME: only the three placeholders
        // the render step substitutes may contain "$".
        var dollars = Regex.Matches(text, @"\$\{?[A-Za-z_]+\}?").Select(m => m.Value).ToHashSet();
        Assert.Equal(new HashSet<string> { "${TELEGRAM_BOT_TOKEN}", "${TELEGRAM_CHAT_ID}", "${ALERT_EMAIL_TO}" }, dollars);
    }

    [Fact]
    public void T165Group_HasNoDollarSign()
    {
        var text = Read("rules.yml");
        var start = text.IndexOf("    name: Skinora Notification And Money Alerts", StringComparison.Ordinal);
        var end = text.IndexOf("    name: Skinora Critical Alerts", StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        Assert.DoesNotContain("$", text[start..end]);
    }

    [Fact]
    public void AwaitingApprovalRule_ReadsThePublishedGauge()
    {
        var observer = new PrometheusOutboundQueueObserver();
        observer.ReportAwaitingApproval(BlockchainNetwork.TRON, 3, 120);
        Assert.Equal(3, PrometheusOutboundQueueObserver.AwaitingApprovalRows.WithLabels("TRON").Value);
        Assert.Equal(120, PrometheusOutboundQueueObserver.AwaitingApprovalOldestSeconds.WithLabels("TRON").Value);

        var block = RuleBlock("outbound-awaiting-approval");
        Assert.Contains($"max by (network) ({PrometheusOutboundQueueObserver.AwaitingApprovalRows.Name}) or vector(0)", block);
        Assert.Matches(@"type: gt\s+params: \[0\]", block);
    }

    [Fact]
    public void HotWalletRule_ReadsThePublishedGauge()
    {
        var observer = new PrometheusHotWalletThresholdObserver();
        observer.Report(BlockchainNetwork.TRON, "USDT", HotWalletMonitorService.DirectionUpper, breached: true);
        Assert.Equal(1, PrometheusHotWalletThresholdObserver.Breached.WithLabels("TRON", "USDT", "upper").Value);
        observer.Report(BlockchainNetwork.TRON, "USDT", HotWalletMonitorService.DirectionUpper, breached: false);
        Assert.Equal(0, PrometheusHotWalletThresholdObserver.Breached.WithLabels("TRON", "USDT", "upper").Value);

        var block = RuleBlock("hot-wallet-threshold-breached");
        Assert.Contains($"max by (network, asset, direction) ({PrometheusHotWalletThresholdObserver.Breached.Name}) or vector(0)", block);
        Assert.Matches(@"type: gt\s+params: \[0\]", block);
    }

    [Fact]
    public void HotWalletGauges_ArePerNetwork_ForEthereumToo()
    {
        // T170 — EthereumHotWalletUsdtLimitUnmonitored: the same series carry
        // Ethereum's hot wallet, with the balance and the limit beside the flag.
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero));
        var observer = new PrometheusHotWalletThresholdObserver(clock);
        observer.Report(BlockchainNetwork.ETHEREUM, "USDT", HotWalletMonitorService.DirectionUpper, breached: true);
        observer.ReportBalance(BlockchainNetwork.ETHEREUM, "USDT", 4500m);
        observer.ReportThreshold(BlockchainNetwork.ETHEREUM, "USDT", HotWalletMonitorService.DirectionUpper, 4000m);

        Assert.Equal(1, PrometheusHotWalletThresholdObserver.Breached.WithLabels("ETHEREUM", "USDT", "upper").Value);
        Assert.Equal(4500, PrometheusHotWalletThresholdObserver.Balance.WithLabels("ETHEREUM", "USDT").Value);
        Assert.Equal(4000, PrometheusHotWalletThresholdObserver.Threshold.WithLabels("ETHEREUM", "USDT", "upper").Value);
        Assert.Equal("skinora_backend_hot_wallet_balance", PrometheusHotWalletThresholdObserver.Balance.Name);
        Assert.Equal("skinora_backend_hot_wallet_threshold", PrometheusHotWalletThresholdObserver.Threshold.Name);

        observer.ReportThreshold(BlockchainNetwork.ETHEREUM, "USDT", HotWalletMonitorService.DirectionUpper, null);
        Assert.DoesNotContain(
            PrometheusHotWalletThresholdObserver.Threshold.GetAllLabelValues(),
            l => l.SequenceEqual(new[] { "ETHEREUM", "USDT", "upper" }));
        observer.Report(BlockchainNetwork.ETHEREUM, "USDT", HotWalletMonitorService.DirectionUpper, breached: false);
    }

    [Fact]
    public void HotWalletMonitorStaleRule_ReadsTheLastCheckTimestamp_SeededByTheFirstAttempt()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero));
        var observer = new PrometheusHotWalletThresholdObserver(clock);
        var label = BlockchainNetwork.ETHEREUM.ToString();

        // The first attempt seeds the series; a later failed attempt does not
        // move it (the age keeps growing), a completed check does.
        observer.ReportAttempt(BlockchainNetwork.ETHEREUM);
        var seeded = PrometheusHotWalletThresholdObserver.LastCheck.WithLabels(label).Value;
        Assert.True(seeded > 0);
        clock.Advance(TimeSpan.FromHours(3));
        observer.ReportAttempt(BlockchainNetwork.ETHEREUM);
        Assert.Equal(seeded, PrometheusHotWalletThresholdObserver.LastCheck.WithLabels(label).Value);
        observer.ReportChecked(BlockchainNetwork.ETHEREUM);
        Assert.Equal(clock.GetUtcNow().ToUnixTimeSeconds(), PrometheusHotWalletThresholdObserver.LastCheck.WithLabels(label).Value);

        var block = RuleBlock("hot-wallet-monitor-stale");
        Assert.Contains($"(time() - max by (network) ({PrometheusHotWalletThresholdObserver.LastCheck.Name})) or vector(0)", block);
        Assert.Matches(@"type: gt\s+params: \[7200\]", block);
        Assert.Contains("category: money", block);
    }

    [Fact]
    public void BlockchainServiceDegradedRule_ReadsTheProbeOutageState_PerNetwork()
    {
        // T170 — the probe's debounced outage reached only the admin panel.
        PlatformHealthMetrics.Publish(PlatformComponents.BlockchainEthereum, healthy: false, inOutage: true);
        Assert.Equal(1, PlatformHealthMetrics.BlockchainServiceDegraded.WithLabels("ETHEREUM").Value);
        Assert.Equal(0, PlatformHealthMetrics.ComponentUp.WithLabels(PlatformComponents.BlockchainEthereum).Value);
        PlatformHealthMetrics.Publish(PlatformComponents.BlockchainEthereum, healthy: true, inOutage: false);
        Assert.Equal(0, PlatformHealthMetrics.BlockchainServiceDegraded.WithLabels("ETHEREUM").Value);
        Assert.Equal(1, PlatformHealthMetrics.ComponentUp.WithLabels(PlatformComponents.BlockchainEthereum).Value);

        // Steam has an up series but no network outage series.
        PlatformHealthMetrics.Publish(PlatformComponents.Steam, healthy: true, inOutage: false);
        Assert.DoesNotContain(
            PlatformHealthMetrics.BlockchainServiceDegraded.GetAllLabelValues(),
            l => l.Single() == PlatformComponents.Steam);

        var block = RuleBlock("blockchain-service-degraded");
        Assert.Contains($"max by (network) ({PlatformHealthMetrics.BlockchainServiceDegraded.Name}) or vector(0)", block);
        Assert.Matches(@"type: gt\s+params: \[0\]", block);
        Assert.Contains("severity: critical", block);
    }

    [Fact]
    public void NetworkAlertGroup_HasNoDollarSign()
    {
        // Grafana's provisioning interpolates $NAME — the T170 group must not
        // carry one (a PromQL regex anchor would be eaten).
        var text = Read("rules.yml");
        var start = text.IndexOf("    name: Skinora Network Alerts", StringComparison.Ordinal);
        Assert.True(start >= 0);
        Assert.DoesNotContain("$", text[start..]);
    }

    [Fact]
    public void ReconciliationMismatchRule_ReadsThePublishedGauge_PerNetworkAndScope()
    {
        // T165 düzeltme turu (doğrulama bulgu 1, ⚠ PARA): the mismatch reached
        // only the panel. The observer publishes the count per network and
        // scope; the rule fires above 0 (critical, money) and resolves at 0.
        var observer = new PrometheusReconciliationRunObserver(TimeProvider.System);
        observer.ReportMismatches(BlockchainNetwork.ETHEREUM, ReconciliationScope.HotWallet, 2);
        observer.ReportMismatches(BlockchainNetwork.TRON, ReconciliationScope.HotWallet, 0);
        Assert.Equal(2, PrometheusReconciliationRunObserver.Mismatches.WithLabels("ETHEREUM", "HotWallet").Value);
        Assert.Equal(0, PrometheusReconciliationRunObserver.Mismatches.WithLabels("TRON", "HotWallet").Value);
        observer.ReportMismatches(BlockchainNetwork.ETHEREUM, ReconciliationScope.HotWallet, 0);
        Assert.Equal(0, PrometheusReconciliationRunObserver.Mismatches.WithLabels("ETHEREUM", "HotWallet").Value);
        Assert.Equal("skinora_backend_reconciliation_mismatches", PrometheusReconciliationRunObserver.Mismatches.Name);

        var block = RuleBlock("reconciliation-mismatch");
        Assert.Contains($"max by (network, scope) ({PrometheusReconciliationRunObserver.Mismatches.Name}) or vector(0)", block);
        Assert.Matches(@"type: gt\s+params: \[0\]", block);
        Assert.Contains("for: 0s", block);
        Assert.Contains("severity: critical", block);
    }

    [Fact]
    public void MoneyEmailSubject_HasNoTrailingNewline()
    {
        // T165 düzeltme turu (doğrulama bulgu 3): a literal `|` block kept its
        // final newline — the subject arrived as "[Skinora] PARA ALARMI (1)=0A".
        var text = Read("contactpoints.yml");
        var block = text[text.IndexOf("    name: money-alarm\n", StringComparison.Ordinal)..];
        Assert.Matches(@"\n          subject: >-\n            \{\{ if \.Alerts\.Firing \}\}[^\n]*\{\{ end \}\}\n        disableResolveMessage", block);
    }

    [Fact]
    public void AdminMoneyAlarmRule_FiresForAnHourAfterTheLatestAlarm()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero));
        new PrometheusAdminMoneyAlarmObserver(clock).Raised("notifications.refund-blocked-admin");
        Assert.Equal(clock.GetUtcNow().ToUnixTimeSeconds(),
            PrometheusAdminMoneyAlarmObserver.LastRaised.WithLabels("notifications.refund-blocked-admin").Value);

        var block = RuleBlock("admin-money-alarm-raised");
        Assert.Contains($"(time() - max by (kind) ({PrometheusAdminMoneyAlarmObserver.LastRaised.Name})) or vector(1e12)", block);
        Assert.Matches(@"type: lt\s+params: \[3600\]", block);
        Assert.Contains("severity: critical", block);
    }

    [Fact]
    public void ChannelMisconfiguredRule_ReadsTheStateTheReporterPublishes()
    {
        var channels = NotificationChannelAvailability.FromConfiguration(key => key switch
        {
            "Telegram:Provider" => "telegram",
            _ => null,
        });
        NotificationChannelStatusReporter.Report(channels, isDevelopment: false, NullLogger.Instance);

        Assert.Equal(1, NotificationChannelStatusReporter.ChannelState.WithLabels("TELEGRAM", "misconfigured").Value);
        Assert.Equal(0, NotificationChannelStatusReporter.ChannelState.WithLabels("TELEGRAM", "live").Value);
        Assert.Equal(1, NotificationChannelStatusReporter.ChannelState.WithLabels("EMAIL", "stub").Value);

        var block = RuleBlock("notification-channel-misconfigured");
        Assert.Contains($"max by (channel) ({NotificationChannelStatusReporter.ChannelState.Name}{{state=\"misconfigured\"}}) or vector(0)", block);
        Assert.DoesNotContain("category: money", block);
    }

    [Fact]
    public void DeliveryExhaustedRule_ReadsThePublishedTimestamp()
    {
        var block = RuleBlock("notification-delivery-exhausted");
        Assert.Contains($"(time() - max by (channel) ({PrometheusNotificationAdminAlertSink.LastExhausted.Name})) or vector(1e12)", block);
        Assert.Matches(@"type: lt\s+params: \[3600\]", block);
    }

    private static string RuleBlock(string uid)
    {
        var text = Read("rules.yml");
        var start = text.IndexOf($"- uid: {uid}\n", StringComparison.Ordinal);
        Assert.True(start >= 0, $"rules.yml has no rule {uid}");
        var next = text.IndexOf("\n      - uid: ", start + 1, StringComparison.Ordinal);
        return next < 0 ? text[start..] : text[start..next];
    }

    private static string Read(string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "docker-compose.yml")))
        {
            dir = dir.Parent;
        }
        if (dir is null) throw new InvalidOperationException("Repository root (docker-compose.yml) not found.");
        return File.ReadAllText(Path.Combine(dir.FullName, "infra", "grafana", "provisioning", "alerting", file))
            .Replace("\r\n", "\n");
    }
}
