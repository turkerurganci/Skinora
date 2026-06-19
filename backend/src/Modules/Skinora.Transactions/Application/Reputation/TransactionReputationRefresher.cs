using Skinora.Users.Application.Reputation;

namespace Skinora.Transactions.Application.Reputation;

/// <summary>
/// Default <see cref="ITransactionReputationRefresher"/> — thin orchestration
/// over <see cref="IReputationAggregator"/> + <see cref="IUserCancelCooldownEvaluator"/>.
/// Holds no state of its own; both dependencies mutate tracked <c>User</c>
/// entities without saving, so the caller's unit of work commits the projection
/// atomically with the terminal transition (WP15).
/// </summary>
public sealed class TransactionReputationRefresher : ITransactionReputationRefresher
{
    private readonly IReputationAggregator _reputation;
    private readonly IUserCancelCooldownEvaluator _cooldown;

    public TransactionReputationRefresher(
        IReputationAggregator reputation,
        IUserCancelCooldownEvaluator cooldown)
    {
        _reputation = reputation;
        _cooldown = cooldown;
    }

    public async Task RefreshAsync(
        Guid sellerId,
        Guid? buyerId,
        bool evaluateCooldown,
        CancellationToken cancellationToken)
    {
        // Recompute the denormalized snapshot for both parties — a COMPLETED row
        // bumps both counts/rates; a CANCELLED_* row keeps the non-responsible
        // side's denominator fresh too (the aggregator's responsibility map
        // decides what counts).
        await _reputation.RecomputeAsync(sellerId, cancellationToken);
        if (buyerId is { } buyer)
            await _reputation.RecomputeAsync(buyer, cancellationToken);

        if (!evaluateCooldown)
            return;

        // Re-evaluate the cancel cooldown for both parties. The evaluator only
        // stamps CooldownExpiresAt on the party actually responsible for the
        // cancellation (02 §14.2); the other side is a no-op.
        await _cooldown.EvaluateAsync(sellerId, cancellationToken);
        if (buyerId is { } cooldownBuyer)
            await _cooldown.EvaluateAsync(cooldownBuyer, cancellationToken);
    }
}
