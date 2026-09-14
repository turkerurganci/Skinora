using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Skinora.Shared.Steam;
using Skinora.Transactions.Application.Lifecycle;
using Skinora.Users.Domain.Entities;

namespace Skinora.Transactions.Tests.Unit.Lifecycle;

/// <summary>
/// 08 §2.2a — the two conditions a 0-second escrow hold cannot answer.
///
/// <para>
/// Every case here is a fail-closed one in disguise: the defect this type
/// closes (🔴 <c>Prova-LimitedAccountNeverChecked</c>) was not a wrong answer,
/// it was a question nobody asked, and the cheapest way to reintroduce it is an
/// arm that quietly returns "eligible" when it does not know.
/// </para>
/// </summary>
public sealed class SteamTradeEligibilityCheckerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Missing_SteamAccountCreatedAt_Is_Unknown_Not_Eligible()
    {
        // Null means "never captured" (the column postdates these accounts), and
        // the remedy is a sign-in. Reading it as "old enough" would be the exact
        // fail-open this round removes.
        var probe = new RecordingProbe(SteamAccountLimitedProbeResult.Clear);
        var sut = Build(probe);

        var result = await sut.EvaluateAsync(UserWith(null), CancellationToken.None);

        Assert.Equal(SteamTradeEligibilityStatus.Unknown, result.Status);
        // And it never spends a Steam request on a question it cannot finish.
        Assert.Equal(0, probe.CallCount);
    }

    [Fact]
    public async Task Account_Inside_The_15_Day_Wait_Is_TooNew_With_Remaining_Days()
    {
        var probe = new RecordingProbe(SteamAccountLimitedProbeResult.Clear);
        var sut = Build(probe);

        var result = await sut.EvaluateAsync(
            UserWith(Now.UtcDateTime.AddDays(-10)), CancellationToken.None);

        Assert.Equal(SteamTradeEligibilityStatus.TooNew, result.Status);
        Assert.Equal(5, result.RemainingDays);
        // The cheap condition rejects before the expensive one is asked — the
        // Steam Community budget is 10 requests a minute and shared with
        // delivery verification.
        Assert.Equal(0, probe.CallCount);
    }

    [Fact]
    public async Task RemainingDays_Rounds_Up_So_It_Never_Reports_Zero_While_Blocked()
    {
        var sut = Build(new RecordingProbe(SteamAccountLimitedProbeResult.Clear));

        // 14 days and 1 hour old: 22 hours left, which truncation would print
        // as "0 days" while the account is still blocked.
        var result = await sut.EvaluateAsync(
            UserWith(Now.UtcDateTime.AddDays(-14).AddHours(-1)), CancellationToken.None);

        Assert.Equal(SteamTradeEligibilityStatus.TooNew, result.Status);
        Assert.Equal(1, result.RemainingDays);
    }

    [Fact]
    public async Task Account_Exactly_At_The_Boundary_Proceeds_To_The_Limited_Probe()
    {
        var probe = new RecordingProbe(SteamAccountLimitedProbeResult.Clear);
        var sut = Build(probe);

        var result = await sut.EvaluateAsync(
            UserWith(Now.UtcDateTime.AddDays(-15)), CancellationToken.None);

        Assert.Equal(SteamTradeEligibilityStatus.Eligible, result.Status);
        Assert.Equal(1, probe.CallCount);
    }

    [Fact]
    public async Task Limited_Account_Is_Limited()
    {
        var sut = Build(new RecordingProbe(SteamAccountLimitedProbeResult.Limited));

        var result = await sut.EvaluateAsync(
            UserWith(Now.UtcDateTime.AddYears(-3)), CancellationToken.None);

        Assert.Equal(SteamTradeEligibilityStatus.Limited, result.Status);
        Assert.Null(result.RemainingDays);
    }

    [Fact]
    public async Task Unreadable_Probe_Is_Unknown_Not_Eligible()
    {
        var sut = Build(new RecordingProbe(SteamAccountLimitedProbeResult.Unavailable));

        var result = await sut.EvaluateAsync(
            UserWith(Now.UtcDateTime.AddYears(-3)), CancellationToken.None);

        Assert.Equal(SteamTradeEligibilityStatus.Unknown, result.Status);
    }

    [Fact]
    public async Task Old_And_Unrestricted_Account_Is_Eligible()
    {
        var probe = new RecordingProbe(SteamAccountLimitedProbeResult.Clear);
        var sut = Build(probe);

        var result = await sut.EvaluateAsync(
            UserWith(Now.UtcDateTime.AddYears(-3)), CancellationToken.None);

        Assert.Equal(SteamTradeEligibilityStatus.Eligible, result.Status);
        Assert.Equal("76561198000000001", probe.LastSteamId);
    }

    [Fact]
    public void The_Wait_Is_Steams_Own_Number_And_Is_Not_The_Login_Age_Gate()
    {
        // Pinned because it is NOT a policy knob: 15 days is Steam's rule, and
        // `auth.min_steam_account_age_days` — a configurable anti-fraud gate on
        // the login path — is a different number for a different purpose.
        Assert.Equal(15, SteamTradeEligibilityChecker.SteamTradeEligibilityWaitDays);
    }

    // ---------- helpers ----------

    private static SteamTradeEligibilityChecker Build(ISteamAccountLimitedProbe probe) =>
        new(probe,
            new FakeTimeProvider(Now),
            NullLogger<SteamTradeEligibilityChecker>.Instance);

    private static User UserWith(DateTime? steamAccountCreatedAt) => new()
    {
        Id = Guid.NewGuid(),
        SteamId = "76561198000000001",
        SteamAccountCreatedAt = steamAccountCreatedAt,
    };

    private sealed class RecordingProbe : ISteamAccountLimitedProbe
    {
        private readonly SteamAccountLimitedProbeResult _result;

        public RecordingProbe(SteamAccountLimitedProbeResult result)
        {
            _result = result;
        }

        public int CallCount { get; private set; }

        public string? LastSteamId { get; private set; }

        public Task<SteamAccountLimitedProbeResult> ProbeAsync(
            string steamId64, CancellationToken cancellationToken)
        {
            CallCount++;
            LastSteamId = steamId64;
            return Task.FromResult(_result);
        }
    }
}
