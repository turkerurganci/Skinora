using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Skinora.API.Monitoring;

namespace Skinora.API.Tests.Unit.Monitoring;

/// <summary>
/// T164 — the database size / backup age series and the four rules that read
/// them. A rule naming a metric nobody exports never fires and never errors
/// (<c>or vector(0)</c>), so each rule is read from the provisioning file and
/// checked against the names the gauges actually carry.
/// </summary>
[Trait("Category", "Unit")]
public sealed class PrometheusDatabaseStorageObserverTests
{
    private const long MiB = 1024 * 1024;

    private static readonly DateTimeOffset Now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    private static PrometheusDatabaseStorageObserver Observer(DatabaseStorageMonitorOptions? options = null) =>
        new(Options.Create(options ?? new DatabaseStorageMonitorOptions()), new FakeTimeProvider(Now));

    private static DatabaseStorageSnapshot Snapshot(
        int edition = 4,
        string recovery = "FULL",
        long used = 700 * MiB,
        long? fullAge = 3_600,
        long? logAge = 300,
        bool historyRead = true) =>
        new(edition, recovery, DataAllocatedBytes: 900 * MiB, DataUsedBytes: used, LogAllocatedBytes: 64 * MiB,
            fullAge, logAge, historyRead);

    [Theory]
    [InlineData(4, null, 10_240L * MiB)]   // Express: the 10 GB per-database cap
    [InlineData(3, null, null)]            // Developer / Enterprise: no cap, no series
    [InlineData(2, null, null)]            // Standard
    [InlineData(3, 2_048L, 2_048L * MiB)]  // configured: watch any edition against a chosen size
    [InlineData(4, 100L, 100L * MiB)]      // configured wins on Express too (test stacks)
    [InlineData(4, 0L, 10_240L * MiB)]     // a non-positive value is ignored
    public void ResolveLimitBytes_Express_Is_10240_MB_Other_Editions_Only_When_Configured(
        int edition, long? configuredMb, long? expected)
    {
        Assert.Equal(expected, PrometheusDatabaseStorageObserver.ResolveLimitBytes(edition, configuredMb));
    }

    [Fact]
    public void Report_Publishes_Sizes_Limit_Recovery_Model_And_Backup_Times()
    {
        Observer().Report(Snapshot());

        Assert.Equal(700d * MiB, PrometheusDatabaseStorageObserver.DataUsedBytes.Value);
        Assert.Equal(900d * MiB, PrometheusDatabaseStorageObserver.DataAllocatedBytes.Value);
        Assert.Equal(64d * MiB, PrometheusDatabaseStorageObserver.LogAllocatedBytes.Value);
        Assert.Equal(10_240d * MiB, PrometheusDatabaseStorageObserver.DataLimitBytes.Value);
        Assert.Equal(1, PrometheusDatabaseStorageObserver.RecoveryModelFull.Value);
        var now = Now.ToUnixTimeSeconds();
        Assert.Equal(now - 3_600, PrometheusDatabaseStorageObserver.LastBackupTimestamp.WithLabels("full").Value);
        Assert.Equal(now - 300, PrometheusDatabaseStorageObserver.LastBackupTimestamp.WithLabels("log").Value);
        Assert.Equal(26 * 3600d, PrometheusDatabaseStorageObserver.BackupAlarmAgeSeconds.WithLabels("full").Value);
        Assert.Equal(45 * 60d, PrometheusDatabaseStorageObserver.BackupAlarmAgeSeconds.WithLabels("log").Value);
    }

    [Fact]
    public void A_Database_Never_Backed_Up_Reads_As_Timestamp_Zero_So_The_Stale_Alarm_Fires()
    {
        Observer().Report(Snapshot(fullAge: null, logAge: null));

        Assert.Equal(0, PrometheusDatabaseStorageObserver.LastBackupTimestamp.WithLabels("full").Value);
        Assert.Equal(0, PrometheusDatabaseStorageObserver.LastBackupTimestamp.WithLabels("log").Value);
    }

    [Fact]
    public void An_Unreadable_History_Keeps_The_Last_Timestamp_It_Ages_Into_The_Alarm()
    {
        Observer().Report(Snapshot(fullAge: 60));
        var before = PrometheusDatabaseStorageObserver.LastBackupTimestamp.WithLabels("full").Value;

        Observer().Report(Snapshot(fullAge: null, historyRead: false));

        Assert.Equal(before, PrometheusDatabaseStorageObserver.LastBackupTimestamp.WithLabels("full").Value);
    }

    [Fact]
    public void Simple_Recovery_Publishes_Zero_The_Log_Rule_Is_Gated_On_It()
    {
        Observer().Report(Snapshot(recovery: "SIMPLE", logAge: null));
        Assert.Equal(0, PrometheusDatabaseStorageObserver.RecoveryModelFull.Value);
    }

    [Fact]
    public void Backup_Thresholds_Are_Configurable_Non_Positive_Falls_Back_To_The_Default()
    {
        Observer(new DatabaseStorageMonitorOptions { FullBackupMaxAgeHours = 50, LogBackupMaxAgeMinutes = 5 })
            .Report(Snapshot());
        Assert.Equal(50 * 3600d, PrometheusDatabaseStorageObserver.BackupAlarmAgeSeconds.WithLabels("full").Value);
        Assert.Equal(5 * 60d, PrometheusDatabaseStorageObserver.BackupAlarmAgeSeconds.WithLabels("log").Value);

        Observer(new DatabaseStorageMonitorOptions { FullBackupMaxAgeHours = 0, LogBackupMaxAgeMinutes = -1 })
            .Report(Snapshot());
        Assert.Equal(26 * 3600d, PrometheusDatabaseStorageObserver.BackupAlarmAgeSeconds.WithLabels("full").Value);
        Assert.Equal(45 * 60d, PrometheusDatabaseStorageObserver.BackupAlarmAgeSeconds.WithLabels("log").Value);
    }

    [Theory]
    [InlineData("db-data-size-warning", "0.7", "warning")]
    [InlineData("db-data-size-critical", "0.85", "critical")]
    public void The_Size_Rules_Divide_Used_By_The_Published_Limit(string uid, string ratio, string severity)
    {
        var block = RuleBlock(uid);

        Assert.Contains(
            $"(max({PrometheusDatabaseStorageObserver.DataUsedBytes.Name}) / max({PrometheusDatabaseStorageObserver.DataLimitBytes.Name})) or vector(0)",
            block);
        Assert.Matches($@"type: gt\s+params: \[{ratio.Replace(".", @"\.")}\]", block);
        Assert.Contains($"severity: {severity}", block);
    }

    [Fact]
    public void The_Full_Backup_Rule_Divides_The_Age_By_The_Published_Threshold()
    {
        var block = RuleBlock("db-backup-full-stale");

        Assert.Contains(
            $"((time() - max by (type) ({PrometheusDatabaseStorageObserver.LastBackupTimestamp.Name}{{type=\"full\"}})) / max by (type) ({PrometheusDatabaseStorageObserver.BackupAlarmAgeSeconds.Name}{{type=\"full\"}})) or vector(0)",
            block);
        Assert.Matches(@"type: gt\s+params: \[1\]", block);
        Assert.Contains("severity: critical", block);
    }

    [Fact]
    public void The_Log_Backup_Rule_Only_Counts_In_Full_Recovery()
    {
        var block = RuleBlock("db-backup-log-stale");

        Assert.Contains(
            $"(((time() - max by (type) ({PrometheusDatabaseStorageObserver.LastBackupTimestamp.Name}{{type=\"log\"}})) / max by (type) ({PrometheusDatabaseStorageObserver.BackupAlarmAgeSeconds.Name}{{type=\"log\"}})) and on() (max({PrometheusDatabaseStorageObserver.RecoveryModelFull.Name}) == 1)) or vector(0)",
            block);
        Assert.Matches(@"type: gt\s+params: \[1\]", block);
        Assert.Contains("severity: warning", block);
    }

    [Fact]
    public async Task The_Service_Publishes_Nothing_For_A_Non_SqlServer_Database_And_Survives_A_Failing_Probe()
    {
        var services = new ServiceCollection();
        var probe = new StubProbe();
        services.AddSingleton<IDatabaseStorageProbe>(probe);
        await using var provider = services.BuildServiceProvider();
        var sut = new DatabaseStorageMonitorService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Observer(),
            Options.Create(new DatabaseStorageMonitorOptions { IntervalSeconds = 1 }),
            NullLogger<DatabaseStorageMonitorService>.Instance);

        probe.Next = () => null;
        Assert.False(await sut.ProbeOnceAsync(CancellationToken.None));

        probe.Next = () => throw new InvalidOperationException("server gone");
        Assert.False(await sut.ProbeOnceAsync(CancellationToken.None));

        probe.Next = () => Snapshot(used: 123 * MiB);
        Assert.True(await sut.ProbeOnceAsync(CancellationToken.None));
        Assert.Equal(123d * MiB, PrometheusDatabaseStorageObserver.DataUsedBytes.Value);

        // The interval never drops under 30 s, whatever is configured.
        Assert.Equal(TimeSpan.FromSeconds(DatabaseStorageMonitorOptions.MinimumIntervalSeconds), sut.Interval);
    }

    private sealed class StubProbe : IDatabaseStorageProbe
    {
        public Func<DatabaseStorageSnapshot?> Next { get; set; } = () => null;

        public Task<DatabaseStorageSnapshot?> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(Next());
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
        // The last rule of a group ends where the next group starts.
        var group = text.IndexOf("\n  - orgId: ", start + 1, StringComparison.Ordinal);
        if (group >= 0 && (next < 0 || group < next)) next = group;
        return next < 0 ? text[start..] : text[start..next];
    }
}
