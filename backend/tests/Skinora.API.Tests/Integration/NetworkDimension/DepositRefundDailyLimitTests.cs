using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Skinora.API.Outbox;
using Skinora.API.Services.OutboundApprovals;
using Skinora.API.Tests.Common;
using Skinora.Platform.Domain.Entities;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Persistence.Outbox;
using Skinora.Shared.Persistence;
using Skinora.Shared.Tests.Integration;
using Skinora.Transactions.Application.Networks;
using Skinora.Transactions.Application.PaymentAddresses;
using Skinora.Transactions.Application.Transfers;
using Skinora.Transactions.Domain.Entities;
using Skinora.Users.Domain.Entities;
using static Skinora.API.Tests.Integration.NetworkDimension.NetworkDimensionTestSupport;

namespace Skinora.API.Tests.Integration.NetworkDimension;

/// <summary>
/// T169a — the deposit refund daily limit on the migrated SQL Server schema
/// (proje sahibi kararı 2026-10-21: depozitten iadeye ağ başına günlük 100 $;
/// 08 §3.3c). ⚠ PARA. The settings are the ones the T169a migration seeds,
/// the dispatcher and AD40/AD42 are the real ones, and the concurrency tests
/// take the real row lock (<c>DepositRefundLimitLocks</c>).
/// </summary>
public class DepositRefundDailyLimitTests : IntegrationTestBase
{
    protected override bool UseMigrations => true;

    static DepositRefundDailyLimitTests() => RegisterAllModules();

    private static readonly DateTimeOffset Start = new(2026, 10, 22, 9, 0, 0, TimeSpan.Zero);
    private readonly FakeTimeProvider _clock = new(Start);
    private readonly GatedTransfers _tron = new();
    private User _seller = null!;
    private User _admin = null!;
    private int _seq;

    private static readonly IStablecoinContractRegistry Contracts =
        StablecoinContractRegistry.FromOptions(new StablecoinContractOptions());

    protected override async Task SeedAsync(AppDbContext context)
    {
        _seller = new User { Id = Guid.NewGuid(), SteamId = "76561198000001201", SteamDisplayName = "T169a Seller" };
        _admin = new User { Id = Guid.NewGuid(), SteamId = "76561198000001202", SteamDisplayName = "T169a Admin" };
        context.Set<User>().AddRange(_seller, _admin);
        // The approval gate is not under test here: open it (T169 tests own it).
        await context.SaveChangesAsync();
        await context.Set<SystemSetting>().Where(s => s.Key == OutboundApprovalGate.AutoSendKey)
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.Value, "true"));
    }

    private async Task<BlockchainTransaction> SeedRefundAsync(decimal amount,
        BlockchainTransactionType type = BlockchainTransactionType.EXCESS_REFUND)
    {
        await using var ctx = CreateContext();
        var tx = NewTransaction(_seller.Id, network: BlockchainNetwork.TRON);
        ctx.Set<Transaction>().Add(tx);
        await ctx.SaveChangesAsync();
        // One deposit per refund (unique per network and address).
        var deposit = NewPaymentAddress(tx.Id, BlockchainNetwork.TRON, $"TDepositT169a{++_seq:D21}", _seq);
        ctx.Set<PaymentAddress>().Add(deposit);
        await ctx.SaveChangesAsync();
        ctx.Set<BlockchainTransaction>().Add(
            NewInbound(tx.Id, deposit.Id, BlockchainNetwork.TRON, "0xin" + Guid.NewGuid().ToString("N")));
        var row = new BlockchainTransaction
        {
            Id = Guid.NewGuid(),
            TransactionId = tx.Id,
            Network = BlockchainNetwork.TRON,
            Type = type,
            FromAddress = TronAddress,
            ToAddress = SellerWallet,
            Amount = amount,
            GasFee = 2.80m,
            Token = StablecoinType.USDT,
            Status = BlockchainTransactionStatus.PENDING,
            CreatedAt = Start.UtcDateTime.AddSeconds(_seq),
        };
        ctx.Set<BlockchainTransaction>().Add(row);
        await ctx.SaveChangesAsync();
        return row;
    }

    private OutgoingTransferDispatchJob Dispatcher(AppDbContext ctx) =>
        new(
            ctx,
            new FakeBlockchainServiceRegistry().With(BlockchainNetwork.TRON, transfers: _tron),
            AlwaysHealthyBlockchainNetworks.Instance,
            new OneMinuteRetryPolicy(),
            new OutboxService(ctx),
            Contracts,
            new BlockchainNetworkSettingsProvider(ctx),
            NullOutboundQueueObserver.Instance,
            new StubPlatformWalletAddressProvider(),
            NullDepositRefundLimitObserver.Instance,
            _clock,
            NullLogger<OutgoingTransferDispatchJob>.Instance);

    private async Task DispatchAsync()
    {
        await using var ctx = CreateContext();
        await Dispatcher(ctx).ExecuteAsync();
    }

    private OutboundApprovalService Service(AppDbContext ctx) =>
        new(ctx, Contracts, new BlockchainNetworkSettingsProvider(ctx), _clock, NullLogger<OutboundApprovalService>.Instance);

    private async Task<BlockchainTransaction> ReloadAsync(Guid id)
    {
        await using var ctx = CreateContext();
        return await ctx.Set<BlockchainTransaction>().AsNoTracking().IgnoreQueryFilters().SingleAsync(b => b.Id == id);
    }

    private async Task SetLimitAsync(string value)
    {
        await using var ctx = CreateContext();
        await ctx.Set<SystemSetting>().Where(s => s.Key == "blockchain.tron.deposit_refund_daily_limit_usdt")
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.Value, value));
    }

    // =================================================================
    // The migrated settings
    // =================================================================

    [Fact]
    public async Task The_Migration_Seeds_100_Per_Network_And_Moves_The_Tron_Refund_Fallback_To_2_80()
    {
        await using var ctx = CreateContext();
        var rows = await ctx.Set<SystemSetting>().AsNoTracking()
            .Where(s => s.Key == "blockchain.tron.deposit_refund_daily_limit_usdt"
                || s.Key == "blockchain.ethereum.deposit_refund_daily_limit_usdt"
                || s.Key == "blockchain.refund_gas_fee_estimate_usdt")
            .ToDictionaryAsync(s => s.Key, s => s);
        Assert.Equal("100", rows["blockchain.tron.deposit_refund_daily_limit_usdt"].Value);
        Assert.Equal("100", rows["blockchain.ethereum.deposit_refund_daily_limit_usdt"].Value);
        Assert.True(rows["blockchain.tron.deposit_refund_daily_limit_usdt"].IsConfigured);
        // 2.0 (T74) → 2.60 (T169) → 2.80 (T169a): the earlier migrations wrote the
        // previous values, each later one moved the row while it still held them.
        Assert.Equal("2.80", rows["blockchain.refund_gas_fee_estimate_usdt"].Value);

        var provider = new BlockchainNetworkSettingsProvider(ctx);
        Assert.Equal(100m, (await provider.GetAsync(BlockchainNetwork.TRON, CancellationToken.None)).DepositRefundDailyLimitUsdt);
        Assert.Equal(100m, (await provider.GetAsync(BlockchainNetwork.ETHEREUM, CancellationToken.None)).DepositRefundDailyLimitUsdt);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("abc")]
    public async Task A_Bad_Limit_Row_Falls_Back_To_100_And_Never_Lifts_The_Limit(string value)
    {
        await SetLimitAsync(value);
        await using var ctx = CreateContext();
        var settings = await new BlockchainNetworkSettingsProvider(ctx).GetAsync(BlockchainNetwork.TRON, CancellationToken.None);
        Assert.Equal(100m, settings.DepositRefundDailyLimitUsdt);
    }

    // =================================================================
    // Seeded limit → hold → AD40 → AD42 → sent
    // =================================================================

    [Fact]
    public async Task On_The_Seeded_100_The_Second_Refund_Waits_AD40_Lists_It_And_AD42_Lets_It_Go()
    {
        var first = await SeedRefundAsync(60m);
        var second = await SeedRefundAsync(50m, BlockchainTransactionType.BUYER_REFUND);

        await DispatchAsync();

        Assert.Equal(first.Id, Assert.Single(_tron.Broadcasts).BlockchainTransactionId);
        var held = await ReloadAsync(second.Id);
        Assert.Equal(BlockchainTransactionStatus.PENDING, held.Status);
        Assert.Equal(0, held.RetryCount);
        Assert.Equal(Start.UtcDateTime, held.DepositRefundLimitHeldAt);
        await using (var ctx = CreateContext())
        {
            // The admin alarm went to the outbox with the hold, once.
            var alert = await ctx.Set<OutboxMessage>().AsNoTracking()
                .SingleAsync(m => m.EventType == OutboxEventTypeName.For(typeof(DepositRefundLimitHeldEvent)));
            Assert.Contains(second.Id.ToString(), alert.Payload);

            var queue = await Service(ctx).ListPendingAsync(CancellationToken.None);
            var tron = Assert.Single(queue.DepositRefundLimits, l => l.Network == BlockchainNetwork.TRON);
            Assert.Equal(100m, tron.LimitUsdt);
            Assert.Equal(60m, tron.UsedLast24hUsdt);
            var listed = Assert.Single(queue.LimitHeldRows);
            Assert.Equal(second.Id, listed.BlockchainTransactionId);
            Assert.Equal(50m, listed.Amount);
            Assert.Equal(Start.UtcDateTime, listed.HeldAt);
        }

        _clock.Advance(TimeSpan.FromMinutes(3));
        await using (var ctx = CreateContext())
        {
            var result = await Service(ctx).ReleaseDailyLimitAsync(_admin.Id, second.Id, "10.0.0.7", CancellationToken.None);
            Assert.Null(result.Error);
            Assert.Equal(60m, result.Value!.UsedLast24hUsdt);
            Assert.Equal(100m, result.Value.LimitUsdt);

            var audit = await ctx.Set<AuditLog>().AsNoTracking()
                .SingleAsync(a => a.Action == AuditAction.DEPOSIT_REFUND_LIMIT_RELEASED);
            Assert.Equal(_admin.Id, audit.ActorId);
            Assert.Equal(second.Id.ToString("D"), audit.EntityId);
            Assert.Equal("10.0.0.7", audit.IpAddress);
            Assert.Contains("\"usedLast24hUsdt\":\"60\"", audit.NewValue);
            Assert.Contains("\"limitUsdt\":\"100\"", audit.NewValue);

            // Twice is refused and writes nothing more.
            Assert.Equal(LimitReleaseError.AlreadyReleased,
                (await Service(ctx).ReleaseDailyLimitAsync(_admin.Id, second.Id, null, CancellationToken.None)).Error);
        }

        // Due at once (NextAttemptAt cleared): the next tick sends it.
        await DispatchAsync();

        Assert.Equal(second.Id, _tron.Broadcasts[^1].BlockchainTransactionId);
        var sent = await ReloadAsync(second.Id);
        Assert.Equal(BlockchainTransactionStatus.DETECTED, sent.Status);
        Assert.Equal(_admin.Id, sent.DepositRefundLimitReleasedByAdminId);
        Assert.Equal(_clock.GetUtcNow().UtcDateTime, sent.DepositRefundCountedAt);
        await using (var ctx = CreateContext())
        {
            Assert.Equal(110m, await DepositRefundDailyLimit.UsedAsync(
                ctx, BlockchainNetwork.TRON, _clock.GetUtcNow().UtcDateTime, null, CancellationToken.None));
        }
    }

    [Fact]
    public async Task AD42_Refuses_A_Row_The_Limit_Does_Not_Hold()
    {
        var waiting = await SeedRefundAsync(10m); // never weighed yet
        await using var ctx = CreateContext();
        var sut = Service(ctx);

        Assert.Equal(LimitReleaseError.RowNotHeld,
            (await sut.ReleaseDailyLimitAsync(_admin.Id, waiting.Id, null, CancellationToken.None)).Error);
        Assert.Equal(LimitReleaseError.RowNotFound,
            (await sut.ReleaseDailyLimitAsync(_admin.Id, Guid.NewGuid(), null, CancellationToken.None)).Error);
        Assert.Equal(0, await ctx.Set<AuditLog>().CountAsync(a => a.Action == AuditAction.DEPOSIT_REFUND_LIMIT_RELEASED));
        Assert.Null((await ReloadAsync(waiting.Id)).DepositRefundLimitReleasedAt);
    }

    // =================================================================
    // Concurrency — two dispatchers never both cross the limit
    // =================================================================

    [Fact]
    public async Task A_Refund_Being_Broadcast_Already_Counts_For_A_Second_Dispatcher()
    {
        // 70 + 60 > 100. While the first dispatcher is inside the 70's
        // broadcast, a second dispatcher (another host, another tick) runs a
        // whole pass. The 70 was stamped and committed BEFORE its broadcast, so
        // the second dispatcher reads 70 and holds the 60. Stamping after the
        // broadcast would let both go out: 130.
        var a = await SeedRefundAsync(70m);
        var b = await SeedRefundAsync(60m);
        decimal? usedWhileBroadcasting = null;
        _tron.DuringFirstBroadcast = async () =>
        {
            // What any other session reads while the 70 is being signed.
            await using (var reader = CreateContext())
            {
                usedWhileBroadcasting = await DepositRefundDailyLimit.UsedAsync(
                    reader, BlockchainNetwork.TRON, _clock.GetUtcNow().UtcDateTime, null, CancellationToken.None);
            }
            await using var other = CreateContext();
            await Dispatcher(other).ExecuteAsync();
        };

        await DispatchAsync();

        Assert.Equal(70m, usedWhileBroadcasting);

        Assert.DoesNotContain(_tron.Broadcasts, c => c.BlockchainTransactionId == b.Id);
        Assert.All(_tron.Broadcasts, c => Assert.Equal(a.Id, c.BlockchainTransactionId));
        Assert.NotNull((await ReloadAsync(b.Id)).DepositRefundLimitHeldAt);
    }

    [Fact]
    public async Task The_Check_Waits_For_Another_Session_Holding_The_Network_Lock()
    {
        var refund = await SeedRefundAsync(30m);

        await using var holder = CreateContext();
        await using var lockTx = await holder.Database.BeginTransactionAsync();
        await DepositRefundDailyLimit.LockAsync(holder, BlockchainNetwork.TRON, Start.UtcDateTime, CancellationToken.None);

        var dispatching = DispatchAsync();
        var finishedFirst = await Task.WhenAny(dispatching, Task.Delay(TimeSpan.FromSeconds(2)));
        Assert.NotSame(dispatching, finishedFirst);   // blocked on the network's lock row
        Assert.Empty(_tron.Broadcasts);

        await lockTx.CommitAsync();                   // lock released
        await dispatching;

        Assert.Equal(refund.Id, Assert.Single(_tron.Broadcasts).BlockchainTransactionId);
    }

    [Fact]
    public async Task Another_Networks_Lock_Does_Not_Block_This_One()
    {
        var refund = await SeedRefundAsync(30m);

        await using var holder = CreateContext();
        await using var lockTx = await holder.Database.BeginTransactionAsync();
        await DepositRefundDailyLimit.LockAsync(holder, BlockchainNetwork.ETHEREUM, Start.UtcDateTime, CancellationToken.None);

        var dispatching = DispatchAsync();
        Assert.Same(dispatching, await Task.WhenAny(dispatching, Task.Delay(TimeSpan.FromSeconds(20))));
        await dispatching;

        Assert.Equal(refund.Id, Assert.Single(_tron.Broadcasts).BlockchainTransactionId);
        await lockTx.RollbackAsync();
    }

    private sealed class OneMinuteRetryPolicy : ITransferRetryPolicy
    {
        public Task<int> GetMaxAttemptsAsync(CancellationToken cancellationToken) => Task.FromResult(4);

        public Task<TimeSpan?> GetRetryDelayAsync(int retryCount, CancellationToken cancellationToken) =>
            Task.FromResult<TimeSpan?>(TimeSpan.FromMinutes(1));
    }

    /// <summary>Records broadcasts; can run a callback inside the first one (another dispatcher's pass).</summary>
    private sealed class GatedTransfers : IBlockchainTransferClient
    {
        private readonly object _gate = new();
        private bool _inFirst;

        public List<TransferBroadcastRequest> Broadcasts { get; } = [];

        public Func<Task>? DuringFirstBroadcast { get; set; }

        public async Task<TransferBroadcastResult> BroadcastAsync(
            TransferBroadcastRequest request, CancellationToken cancellationToken)
        {
            Func<Task>? during = null;
            lock (_gate)
            {
                Broadcasts.Add(request);
                if (!_inFirst && DuringFirstBroadcast is not null)
                {
                    _inFirst = true;
                    during = DuringFirstBroadcast;
                }
            }
            if (during is not null) await during();
            return new TransferBroadcastResult(
                TransferBroadcastStatus.Success, "tron-sent-" + Guid.NewGuid().ToString("N"), null, null);
        }

        public Task<TransferStatusResult> GetStatusAsync(string txHash, CancellationToken cancellationToken) =>
            Task.FromResult(new TransferStatusResult(TransferStatusOutcome.Pending, null, null, null, null));

        public Task<TransferStatusResult> GetStatusAsync(
            string txHash, TransferStatusExpectation? expected, CancellationToken cancellationToken) =>
            GetStatusAsync(txHash, cancellationToken);

        public Task<TransferVerifyResult> VerifyTransferAsync(
            TransferVerifyRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new TransferVerifyResult(TransferVerifyOutcome.Match, 1));
    }
}
