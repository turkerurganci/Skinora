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
    private readonly RecordingNonDelivery _nonDelivery = new();

    private TransactionReputationRefresher CreateSut() => new(_aggregator, _cooldown, _nonDelivery);

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

    [Fact]
    public async Task Refresh_Never_Runs_The_NonDelivery_Sanction()
    {
        // 02 §14.2 — the sanction is keyed to the transaction that went terminal,
        // not to the parties. If a party refresh triggered it, an unrelated
        // completion would re-suspend a seller an admin had just cleared.
        await CreateSut().RefreshAsync(Guid.NewGuid(), Guid.NewGuid(), evaluateCooldown: true, CancellationToken.None);

        Assert.Empty(_nonDelivery.EvaluatedTransactionIds);
    }

    [Fact]
    public async Task EvaluateNonDelivery_Delegates_The_Transaction_Id()
    {
        var transactionId = Guid.NewGuid();

        await CreateSut().EvaluateNonDeliveryAsync(transactionId, CancellationToken.None);

        Assert.Equal(new[] { transactionId }, _nonDelivery.EvaluatedTransactionIds);
        Assert.Empty(_aggregator.RecomputedUserIds);
        Assert.Empty(_cooldown.EvaluatedUserIds);
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

    private sealed class RecordingNonDelivery : INonDeliveryAbuseEvaluator
    {
        public List<Guid> EvaluatedTransactionIds { get; } = [];

        public Task<NonDeliveryAbuseOutcome> EvaluateAsync(Guid transactionId, CancellationToken cancellationToken)
        {
            EvaluatedTransactionIds.Add(transactionId);
            return Task.FromResult(new NonDeliveryAbuseOutcome(NonDeliveryAbuseAction.NotANonDeliveryEvent, 0));
        }
    }
}
