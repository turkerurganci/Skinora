using Microsoft.Extensions.Logging.Abstractions;
using Skinora.API.BackgroundJobs;
using Skinora.Shared.BackgroundJobs;

namespace Skinora.API.Tests.Unit.BackgroundJobs;

/// <summary>
/// Unit coverage for <see cref="CronSettingChangePropagator"/> (WP14). Verifies
/// it routes a settings change to the matching <see cref="ICronJobReconfigurer"/>,
/// no-ops for keys that do not drive a cron job, and swallows reconfigurer
/// failures (the authoritative DB write already committed).
/// </summary>
[Trait("Category", "Unit")]
public sealed class CronSettingChangePropagatorTests
{
    private sealed class FakeReconfigurer : ICronJobReconfigurer
    {
        private readonly bool _throw;
        public FakeReconfigurer(string key, bool @throw = false)
        {
            CronSettingKey = key;
            _throw = @throw;
        }

        public string CronSettingKey { get; }
        public List<string> Calls { get; } = new();

        public void Reconfigure(string cronExpression)
        {
            Calls.Add(cronExpression);
            if (_throw)
                throw new InvalidOperationException("hangfire storage unavailable");
        }
    }

    private static CronSettingChangePropagator Create(params ICronJobReconfigurer[] reconfigurers)
        => new(reconfigurers, NullLogger<CronSettingChangePropagator>.Instance);

    [Fact]
    public async Task PropagateAsync_MatchingKey_ReconfiguresWithNewValue()
    {
        var recon = new FakeReconfigurer("reconciliation.schedule_cron");
        var hot = new FakeReconfigurer("hot_wallet.monitor_cron");
        var propagator = Create(recon, hot);

        await propagator.PropagateAsync("hot_wallet.monitor_cron", "*/30 * * * *", CancellationToken.None);

        Assert.Equal(new[] { "*/30 * * * *" }, hot.Calls);
        Assert.Empty(recon.Calls);
    }

    [Fact]
    public async Task PropagateAsync_NonCronKey_DoesNothing()
    {
        var recon = new FakeReconfigurer("reconciliation.schedule_cron");
        var propagator = Create(recon);

        await propagator.PropagateAsync("commission_rate", "0.03", CancellationToken.None);

        Assert.Empty(recon.Calls);
    }

    [Fact]
    public async Task PropagateAsync_ReconfigurerThrows_IsSwallowed()
    {
        var recon = new FakeReconfigurer("reconciliation.schedule_cron", @throw: true);
        var propagator = Create(recon);

        // Must not surface — the admin's write already succeeded; a failed
        // re-register is logged and the job keeps its previous schedule.
        var ex = await Record.ExceptionAsync(() =>
            propagator.PropagateAsync("reconciliation.schedule_cron", "0 5 * * *", CancellationToken.None));

        Assert.Null(ex);
        Assert.Single(recon.Calls);
    }
}
