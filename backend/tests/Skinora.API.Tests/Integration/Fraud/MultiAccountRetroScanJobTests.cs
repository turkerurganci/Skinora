using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Skinora.API.Services.Fraud;
using Skinora.Fraud.Infrastructure.Persistence;
using Skinora.Platform.Infrastructure.Persistence;
using Skinora.Shared.Persistence;
using Skinora.Shared.Tests.Integration;
using Skinora.Transactions.Infrastructure.Persistence;
using Skinora.Users.Application.MultiAccount;
using Skinora.Users.Domain.Entities;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.API.Tests.Integration.Fraud;

/// <summary>
/// WP4b — <see cref="MultiAccountRetroScanJob"/> coverage. The detector itself
/// (flagging + idempotency) is covered by <c>MultiAccountDetectorTests</c>; this
/// suite verifies the job's OWN behaviour against a real DB with a recording
/// fake detector: the candidate filter (only non-deleted, non-deactivated,
/// wallet-bearing users), the per-user loop + outcome aggregation, and per-user
/// fault isolation.
/// </summary>
public class MultiAccountRetroScanJobTests : IntegrationTestBase
{
    static MultiAccountRetroScanJobTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        TransactionsModuleDbRegistration.RegisterTransactionsModule();
        FraudModuleDbRegistration.RegisterFraudModule();
        PlatformModuleDbRegistration.RegisterPlatformModule();
    }

    private static int _steamCounter = 1;

    protected override Task SeedAsync(AppDbContext context) => Task.CompletedTask;

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Scans_Only_Active_WalletBearing_Users()
    {
        var withPayout = await InsertUserAsync(payout: "TPayoutAddrAAA00000000000000000000A");
        var withRefund = await InsertUserAsync(refund: "TRefundAddrBBB00000000000000000000B");
        var noWallet = await InsertUserAsync();
        var deactivated = await InsertUserAsync(payout: "TDeactivatedCCC0000000000000000000C", deactivated: true);
        var deleted = await InsertUserAsync(payout: "TDeletedDDD00000000000000000000000D", deleted: true);

        var detector = new FakeMultiAccountDetector();
        detector.Results[withPayout] = MultiAccountEvaluationStatus.Flagged;
        detector.Results[withRefund] = MultiAccountEvaluationStatus.NoSignal;

        var outcome = await NewJob(detector).ExecuteAsync(CancellationToken.None);

        // Only the two active, wallet-bearing users are evaluated.
        Assert.Equal(
            new HashSet<Guid> { withPayout, withRefund },
            detector.Evaluated.ToHashSet());
        Assert.DoesNotContain(noWallet, detector.Evaluated);
        Assert.DoesNotContain(deactivated, detector.Evaluated);
        Assert.DoesNotContain(deleted, detector.Evaluated);

        Assert.Equal(2, outcome.Scanned);
        Assert.Equal(1, outcome.Flagged);
        Assert.Equal(1, outcome.NoSignal);
        Assert.Equal(0, outcome.Failed);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task PerUser_Failure_Does_Not_Abort_The_Sweep()
    {
        var a = await InsertUserAsync(payout: "TUserAAA000000000000000000000000001");
        var b = await InsertUserAsync(payout: "TUserBBB000000000000000000000000002");
        var c = await InsertUserAsync(payout: "TUserCCC000000000000000000000000003");

        var detector = new FakeMultiAccountDetector();
        detector.ThrowFor.Add(a);
        detector.Results[b] = MultiAccountEvaluationStatus.AlreadyFlagged;
        detector.Results[c] = MultiAccountEvaluationStatus.Flagged;

        var outcome = await NewJob(detector).ExecuteAsync(CancellationToken.None);

        // The throwing user is logged + counted, but b and c are still processed.
        Assert.Equal(new HashSet<Guid> { a, b, c }, detector.Evaluated.ToHashSet());
        Assert.Equal(3, outcome.Scanned);
        Assert.Equal(1, outcome.Failed);
        Assert.Equal(1, outcome.AlreadyFlagged);
        Assert.Equal(1, outcome.Flagged);
    }

    private MultiAccountRetroScanJob NewJob(IMultiAccountDetector detector) =>
        new(Context, detector, NullLogger<MultiAccountRetroScanJob>.Instance);

    private async Task<Guid> InsertUserAsync(
        string? payout = null,
        string? refund = null,
        bool deactivated = false,
        bool deleted = false)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            SteamId = $"765611985400{_steamCounter++:D5}",
            SteamDisplayName = "RetroScanUser",
            DefaultPayoutAddress = payout,
            DefaultRefundAddress = refund,
            IsDeactivated = deactivated,
            DeactivatedAt = deactivated ? DateTime.UtcNow : null,
            IsDeleted = deleted,
            DeletedAt = deleted ? DateTime.UtcNow : null,
        };
        Context.Set<User>().Add(user);
        await Context.SaveChangesAsync();
        return user.Id;
    }

    private sealed class FakeMultiAccountDetector : IMultiAccountDetector
    {
        public List<Guid> Evaluated { get; } = new();
        public Dictionary<Guid, MultiAccountEvaluationStatus> Results { get; } = new();
        public HashSet<Guid> ThrowFor { get; } = new();

        public Task<MultiAccountEvaluationResult> EvaluateAsync(
            Guid userId, CancellationToken cancellationToken)
        {
            Evaluated.Add(userId);
            if (ThrowFor.Contains(userId))
                throw new InvalidOperationException("simulated detector fault");

            var status = Results.TryGetValue(userId, out var s)
                ? s
                : MultiAccountEvaluationStatus.NoSignal;

            return Task.FromResult(status switch
            {
                MultiAccountEvaluationStatus.Flagged => MultiAccountEvaluationResult.Flagged(
                    MultiAccountMatchType.WALLET_PAYOUT, "addr", 1, 0, Guid.NewGuid()),
                MultiAccountEvaluationStatus.AlreadyFlagged => MultiAccountEvaluationResult.AlreadyFlagged(),
                _ => MultiAccountEvaluationResult.NoSignal(),
            });
        }
    }
}
