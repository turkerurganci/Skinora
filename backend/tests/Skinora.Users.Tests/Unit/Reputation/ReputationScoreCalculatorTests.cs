using Skinora.Users.Application.Reputation;

namespace Skinora.Users.Tests.Unit.Reputation;

/// <summary>
/// Verifies <see cref="ReputationScoreCalculator"/> against the 06 §3.1
/// example table verbatim — every row in that table must round-trip through
/// the calculator with the documented thresholds (30 / 3).
/// </summary>
public class ReputationScoreCalculatorTests
{
    private static readonly DateTime NowUtc = new(2026, 5, 1, 12, 0, 0, DateTimeKind.Utc);

    private static ReputationScoreCalculator BuildCalculator(int minAgeDays = 30, int minTx = 3)
    {
        var thresholds = new StubThresholds(new ReputationThresholds(minAgeDays, minTx));
        return new ReputationScoreCalculator(thresholds);
    }

    [Theory]
    // 06 §3.1 example table — every row reproduced verbatim
    [InlineData("Tipik aktif kullanıcı", 24, 0.9600, 180, 4.8)]
    [InlineData("Mükemmel kullanıcı", 50, 1.0000, 365, 5.0)]
    [InlineData("Yeni başlayan", 5, 0.8000, 90, 4.0)]
    [InlineData("Düşük başarı", 10, 0.5000, 365, 2.5)]
    public async Task Compute_When_Eligible_Returns_Composite_Score(
        string scenario, int completedCount, double rateInput, int accountAgeDays, double expectedScore)
    {
        var calc = BuildCalculator();
        var createdAt = NowUtc.AddDays(-accountAgeDays);

        var score = await calc.ComputeAsync(
            completedCount, (decimal)rateInput, createdAt, NowUtc, CancellationToken.None);

        Assert.True(score.HasValue, $"{scenario}: score should be non-null");
        Assert.Equal((decimal)expectedScore, score!.Value);
    }

    [Theory]
    // Threshold-failure rows from the same 06 §3.1 example table.
    [InlineData("Az işlem (CompletedTx < 3)", 2, 1.0000, 365)]
    [InlineData("Yeni hesap (accountAge < 30)", 5, 1.0000, 10)]
    public async Task Compute_When_Threshold_Fails_Returns_Null(
        string scenario, int completedCount, double rateInput, int accountAgeDays)
    {
        var calc = BuildCalculator();
        var createdAt = NowUtc.AddDays(-accountAgeDays);

        var score = await calc.ComputeAsync(
            completedCount, (decimal)rateInput, createdAt, NowUtc, CancellationToken.None);

        Assert.True(score is null, $"{scenario}: score should be null");
    }

    [Fact]
    public async Task Compute_When_Rate_Is_Null_Returns_Null()
    {
        var calc = BuildCalculator();
        var createdAt = NowUtc.AddDays(-365);

        var score = await calc.ComputeAsync(0, null, createdAt, NowUtc, CancellationToken.None);

        Assert.Null(score);
    }

    [Fact]
    public async Task Compute_Truncates_Toward_Zero_Per_06_8_3_Financial_Rounding()
    {
        // 0.964 × 5 = 4.82 → 4.8 with MidpointRounding.ToZero (06 §8.3).
        var calc = BuildCalculator();
        var createdAt = NowUtc.AddDays(-365);

        var score = await calc.ComputeAsync(10, 0.964m, createdAt, NowUtc, CancellationToken.None);

        Assert.Equal(4.8m, score);
    }

    [Fact]
    public async Task Compute_Picks_Up_Threshold_Updates_On_Each_Call()
    {
        // Admins can tune thresholds without a restart — the calculator must
        // re-read the provider on every invocation, not memoize the first value.
        var thresholds = new MutableThresholds(new ReputationThresholds(30, 3));
        var calc = new ReputationScoreCalculator(thresholds);
        var createdAt = NowUtc.AddDays(-20);

        // 20-day-old account, threshold 30 → null
        Assert.Null(await calc.ComputeAsync(5, 1.0m, createdAt, NowUtc, CancellationToken.None));

        // Admin lowers the threshold to 10 → same input now yields 5.0
        thresholds.Set(new ReputationThresholds(10, 3));
        Assert.Equal(5.0m, await calc.ComputeAsync(5, 1.0m, createdAt, NowUtc, CancellationToken.None));
    }

    private sealed class StubThresholds : IReputationThresholdsProvider
    {
        private readonly ReputationThresholds _value;
        public StubThresholds(ReputationThresholds value) => _value = value;
        public Task<ReputationThresholds> GetAsync(CancellationToken _) => Task.FromResult(_value);
    }

    private sealed class MutableThresholds : IReputationThresholdsProvider
    {
        private ReputationThresholds _value;
        public MutableThresholds(ReputationThresholds value) => _value = value;
        public void Set(ReputationThresholds value) => _value = value;
        public Task<ReputationThresholds> GetAsync(CancellationToken _) => Task.FromResult(_value);
    }
}
