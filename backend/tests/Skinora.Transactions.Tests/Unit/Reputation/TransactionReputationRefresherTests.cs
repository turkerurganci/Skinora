using Skinora.Transactions.Application.Reputation;
using Skinora.Users.Application.Reputation;
using Xunit;

namespace Skinora.Transactions.Tests.Unit.Reputation;

/// <summary>
/// WP15 — unit coverage for <see cref="TransactionReputationRefresher"/>. Proves
/// the orchestration contract: recompute runs for both parties, cooldown runs
/// only when requested (cancellation-class transitions) and never for COMPLETED,
/// and a null buyer (pre-accept seller cancel) is skipped on both legs.
/// </summary>
[Trait("Category", "Unit")]
public class TransactionReputationRefresherTests
{
    private readonly RecordingAggregator _aggregator = new();
    private readonly RecordingCooldown _cooldown = new();

    private TransactionReputationRefresher CreateSut() => new(_aggregator, _cooldown);

    [Fact]
    public async Task Completed_Recomputes_Both_Parties_And_Skips_Cooldown()
    {
        var seller = Guid.NewGuid();
        var buyer = Guid.NewGuid();

        await CreateSut().RefreshAsync(seller, buyer, evaluateCooldown: false, CancellationToken.None);

        Assert.Equal(new[] { seller, buyer }, _aggregator.RecomputedUserIds);
        Assert.Empty(_cooldown.EvaluatedUserIds);
    }

    [Fact]
    public async Task Cancellation_Recomputes_And_Evaluates_Cooldown_For_Both_Parties()
    {
        var seller = Guid.NewGuid();
        var buyer = Guid.NewGuid();

        await CreateSut().RefreshAsync(seller, buyer, evaluateCooldown: true, CancellationToken.None);

        Assert.Equal(new[] { seller, buyer }, _aggregator.RecomputedUserIds);
        Assert.Equal(new[] { seller, buyer }, _cooldown.EvaluatedUserIds);
    }

    [Fact]
    public async Task Null_Buyer_Is_Skipped_On_Both_Legs()
    {
        var seller = Guid.NewGuid();

        await CreateSut().RefreshAsync(seller, buyerId: null, evaluateCooldown: true, CancellationToken.None);

        Assert.Equal(new[] { seller }, _aggregator.RecomputedUserIds);
        Assert.Equal(new[] { seller }, _cooldown.EvaluatedUserIds);
    }

    private sealed class RecordingAggregator : IReputationAggregator
    {
        public List<Guid> RecomputedUserIds { get; } = [];

        public Task<ReputationSnapshot> RecomputeAsync(Guid userId, CancellationToken cancellationToken)
        {
            RecomputedUserIds.Add(userId);
            return Task.FromResult(new ReputationSnapshot(0, null));
        }
    }

    private sealed class RecordingCooldown : IUserCancelCooldownEvaluator
    {
        public List<Guid> EvaluatedUserIds { get; } = [];

        public Task<CooldownEvaluationResult> EvaluateAsync(Guid userId, CancellationToken cancellationToken)
        {
            EvaluatedUserIds.Add(userId);
            return Task.FromResult(new CooldownEvaluationResult(0, 0, 0, null));
        }
    }
}
