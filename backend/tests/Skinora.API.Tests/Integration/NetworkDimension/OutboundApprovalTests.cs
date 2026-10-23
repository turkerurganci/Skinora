using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Skinora.API.Outbox;
using Skinora.API.Services.OutboundApprovals;
using Skinora.API.Tests.Common;
using Skinora.Platform.Domain.Entities;
using Skinora.Shared.Enums;
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
/// T169 — the payout/refund launch gate and its admin approval (AD40/AD41,
/// 07 §9.35; proje sahibi kararı 2026-10-21: test boyunca bütün ödemeler admin
/// onayı gerektirir). ⚠ PARA. Runs on the migrated SQL Server schema, so the
/// gate is exactly as the T169 migration seeds it (closed) and the dispatcher
/// is the real one.
/// </summary>
public class OutboundApprovalTests : IntegrationTestBase
{
    protected override bool UseMigrations => true;

    static OutboundApprovalTests() => RegisterAllModules();

    private static readonly DateTimeOffset Start = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);
    private readonly FakeTimeProvider _clock = new(Start);
    private readonly RecordingTransfers _tron = new();
    private User _seller = null!;
    private User _admin = null!;

    private static readonly IStablecoinContractRegistry Contracts =
        StablecoinContractRegistry.FromOptions(new StablecoinContractOptions());

    protected override async Task SeedAsync(AppDbContext context)
    {
        _seller = new User { Id = Guid.NewGuid(), SteamId = "76561198000001101", SteamDisplayName = "T169 Seller" };
        _admin = new User { Id = Guid.NewGuid(), SteamId = "76561198000001102", SteamDisplayName = "T169 Admin" };
        context.Set<User>().AddRange(_seller, _admin);
        await context.SaveChangesAsync();
    }

    private int _seq;

    private async Task<BlockchainTransaction> SeedRowAsync(
        BlockchainTransactionType type,
        BlockchainTransactionStatus status = BlockchainTransactionStatus.PENDING,
        string? txHash = null)
    {
        await using var ctx = CreateContext();
        var tx = NewTransaction(_seller.Id, network: BlockchainNetwork.TRON);
        ctx.Set<Transaction>().Add(tx);
        await ctx.SaveChangesAsync();

        // Refunds and sweeps leave the deposit: the dispatcher resolves it from
        // the transaction's BUYER_PAYMENT row; only a sweep row names it (CHECKs).
        Guid? depositId = null;
        if (type != BlockchainTransactionType.SELLER_PAYOUT)
        {
            var deposit = NewPaymentAddress(tx.Id, BlockchainNetwork.TRON, TronAddress, ++_seq);
            ctx.Set<PaymentAddress>().Add(deposit);
            await ctx.SaveChangesAsync();
            depositId = deposit.Id;
            var inbound = NewInbound(tx.Id, deposit.Id, BlockchainNetwork.TRON, "0xin" + Guid.NewGuid().ToString("N"));
            ctx.Set<BlockchainTransaction>().Add(inbound);
            await ctx.SaveChangesAsync();
        }

        var row = new BlockchainTransaction
        {
            Id = Guid.NewGuid(),
            TransactionId = tx.Id,
            Network = BlockchainNetwork.TRON,
            Type = type,
            PaymentAddressId = type == BlockchainTransactionType.SWEEP ? depositId : null,
            TxHash = txHash,
            FromAddress = type == BlockchainTransactionType.SELLER_PAYOUT ? string.Empty : TronAddress,
            ToAddress = SellerWallet,
            Amount = 40m + (++_seq),
            Token = StablecoinType.USDT,
            Status = status,
            ConfirmedAt = status == BlockchainTransactionStatus.CONFIRMED ? Start.UtcDateTime : null,
            ConfirmationCount = status == BlockchainTransactionStatus.CONFIRMED ? 20 : 0,
            // Seconds apart so "oldest first" is a defined order.
            CreatedAt = Start.UtcDateTime.AddSeconds(_seq),
        };
        ctx.Set<BlockchainTransaction>().Add(row);
        await ctx.SaveChangesAsync();
        return row;
    }

    private OutboundApprovalService Service(AppDbContext ctx) =>
        new(ctx, Contracts, _clock, NullLogger<OutboundApprovalService>.Instance);

    private async Task DispatchAsync()
    {
        var registry = new FakeBlockchainServiceRegistry().With(BlockchainNetwork.TRON, transfers: _tron);
        await using var ctx = CreateContext();
        var dispatch = new OutgoingTransferDispatchJob(
            ctx, registry, AlwaysHealthyBlockchainNetworks.Instance, new OneMinuteRetryPolicy(),
            new OutboxService(ctx), Contracts,
            new BlockchainNetworkSettingsProvider(ctx), NullOutboundQueueObserver.Instance,
            new StubPlatformWalletAddressProvider(), _clock, NullLogger<OutgoingTransferDispatchJob>.Instance);
        await dispatch.ExecuteAsync();
    }

    // =================================================================
    // The migrated gate
    // =================================================================

    [Fact]
    public async Task The_Migrated_Database_Starts_With_The_Gate_Closed()
    {
        await using var ctx = CreateContext();
        var row = await ctx.Set<SystemSetting>().AsNoTracking()
            .SingleAsync(s => s.Key == OutboundApprovalGate.AutoSendKey);
        Assert.Equal("false", row.Value);
        Assert.True(row.IsConfigured);
        Assert.False(await OutboundApprovalGate.IsAutoSendEnabledAsync(ctx, CancellationToken.None));
    }

    [Fact]
    public async Task Gate_Closed_Payout_And_Refund_Wait_For_An_Approval_Then_Leave_Unchanged()
    {
        var payout = await SeedRowAsync(BlockchainTransactionType.SELLER_PAYOUT);
        var refund = await SeedRowAsync(BlockchainTransactionType.BUYER_REFUND);

        await DispatchAsync();
        Assert.Empty(_tron.Broadcasts);

        // AD40 lists both, oldest first.
        await using (var ctx = CreateContext())
        {
            var queue = await Service(ctx).ListPendingAsync(CancellationToken.None);
            Assert.False(queue.AutoSendEnabled);
            Assert.Equal([payout.Id, refund.Id], queue.Rows.Select(r => r.BlockchainTransactionId).ToArray());
            Assert.Equal(payout.Amount, queue.Rows[0].Amount);
            Assert.Equal(StablecoinType.USDT, queue.Rows[0].ChainToken);
        }

        // AD41 approves the payout only.
        _clock.Advance(TimeSpan.FromMinutes(5));
        await using (var ctx = CreateContext())
        {
            var result = await Service(ctx).ApproveAsync(_admin.Id, payout.Id, "10.0.0.9", CancellationToken.None);
            Assert.Null(result.Error);
            Assert.Equal(_admin.Id, result.Value!.ApprovedByAdminId);
        }

        await DispatchAsync();

        var sent = Assert.Single(_tron.Broadcasts);
        Assert.Equal(payout.Id, sent.BlockchainTransactionId);
        Assert.Equal(payout.Amount, sent.Amount);           // the approved amount is what leaves
        Assert.Equal(payout.ToAddress, sent.ToAddress);
        await using (var ctx = CreateContext())
        {
            var paid = await ctx.Set<BlockchainTransaction>().AsNoTracking().SingleAsync(b => b.Id == payout.Id);
            Assert.Equal(BlockchainTransactionStatus.DETECTED, paid.Status);
            Assert.Equal(Start.UtcDateTime.AddMinutes(5), paid.ApprovedAt);
            Assert.Equal(_admin.Id, paid.ApprovedByAdminId);
            var held = await ctx.Set<BlockchainTransaction>().AsNoTracking().SingleAsync(b => b.Id == refund.Id);
            Assert.Equal(BlockchainTransactionStatus.PENDING, held.Status);
            Assert.Equal(0, held.RetryCount);

            var audit = await ctx.Set<AuditLog>().AsNoTracking()
                .SingleAsync(a => a.Action == AuditAction.OUTBOUND_TRANSFER_APPROVED);
            Assert.Equal(_admin.Id, audit.ActorId);
            Assert.Equal(ActorType.ADMIN, audit.ActorType);
            Assert.Equal(nameof(BlockchainTransaction), audit.EntityType);
            Assert.Equal(payout.Id.ToString("D"), audit.EntityId);
            Assert.Equal("10.0.0.9", audit.IpAddress);
            Assert.Contains("\"type\":\"SELLER_PAYOUT\"", audit.NewValue);
            Assert.Contains("\"network\":\"TRON\"", audit.NewValue);
            Assert.Contains($"\"toAddress\":\"{SellerWallet}\"", audit.NewValue);
        }
    }

    [Fact]
    public async Task The_Sweep_Never_Waits_And_Is_Not_Listed()
    {
        var sweep = await SeedRowAsync(BlockchainTransactionType.SWEEP);

        await using (var ctx = CreateContext())
        {
            var queue = await Service(ctx).ListPendingAsync(CancellationToken.None);
            Assert.Empty(queue.Rows);
            var refused = await Service(ctx).ApproveAsync(_admin.Id, sweep.Id, null, CancellationToken.None);
            Assert.Equal(ApprovalError.RowNotApprovable, refused.Error);
        }

        await DispatchAsync();

        Assert.Equal(sweep.Id, Assert.Single(_tron.Broadcasts).BlockchainTransactionId);
    }

    [Fact]
    public async Task Approval_Is_Once_Only_Open_Rows_Only_And_Every_Refusal_Writes_Nothing()
    {
        var payout = await SeedRowAsync(BlockchainTransactionType.SELLER_PAYOUT);
        var detected = await SeedRowAsync(BlockchainTransactionType.BUYER_REFUND,
            BlockchainTransactionStatus.DETECTED, txHash: "tron-hash-" + Guid.NewGuid().ToString("N"));

        await using var ctx = CreateContext();
        var sut = Service(ctx);
        Assert.Null((await sut.ApproveAsync(_admin.Id, payout.Id, null, CancellationToken.None)).Error);

        Assert.Equal(ApprovalError.AlreadyApproved,
            (await sut.ApproveAsync(_admin.Id, payout.Id, null, CancellationToken.None)).Error);
        Assert.Equal(ApprovalError.RowNotOpen,
            (await sut.ApproveAsync(_admin.Id, detected.Id, null, CancellationToken.None)).Error);
        Assert.Equal(ApprovalError.RowNotFound,
            (await sut.ApproveAsync(_admin.Id, Guid.NewGuid(), null, CancellationToken.None)).Error);

        Assert.Equal(1, await ctx.Set<AuditLog>().CountAsync(a => a.Action == AuditAction.OUTBOUND_TRANSFER_APPROVED));
        var untouched = await ctx.Set<BlockchainTransaction>().AsNoTracking().SingleAsync(b => b.Id == detected.Id);
        Assert.Null(untouched.ApprovedAt);
    }

    [Fact]
    public async Task Gate_Open_Rows_Leave_Without_Approval_And_The_Queue_Says_So()
    {
        await using (var ctx = CreateContext())
        {
            await ctx.Set<SystemSetting>().Where(s => s.Key == OutboundApprovalGate.AutoSendKey)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.Value, "true"));
        }
        var payout = await SeedRowAsync(BlockchainTransactionType.SELLER_PAYOUT);

        await using (var ctx = CreateContext())
        {
            Assert.True((await Service(ctx).ListPendingAsync(CancellationToken.None)).AutoSendEnabled);
        }
        await DispatchAsync();

        Assert.Equal(payout.Id, Assert.Single(_tron.Broadcasts).BlockchainTransactionId);
    }

    // =================================================================
    // T172a — the dispatcher's stop switch (outbound.dispatch_paused, ⚠ PARA)
    // =================================================================

    private async Task SetSettingAsync(string key, string value)
    {
        await using var ctx = CreateContext();
        await ctx.Set<SystemSetting>().Where(s => s.Key == key)
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.Value, value));
    }

    [Fact]
    public async Task T172a_The_Migrated_Database_Starts_With_Dispatch_Not_Paused()
    {
        await using var ctx = CreateContext();
        var row = await ctx.Set<SystemSetting>().AsNoTracking()
            .SingleAsync(s => s.Key == OutboundDispatchPause.PausedKey);
        Assert.Equal("false", row.Value);
        Assert.True(row.IsConfigured);
        Assert.Equal("bool", row.DataType);
        Assert.False(await OutboundDispatchPause.IsPausedAsync(ctx, CancellationToken.None));
    }

    [Fact]
    public async Task T172a_Paused_Nothing_Leaves_Approved_Payout_Sweep_Or_Gate_Open_And_No_Retry_Is_Spent()
    {
        // After a restore: the gate open (worst case), an approved payout, a
        // sweep — every kind the dispatcher can send. Paused, none of them goes.
        await SetSettingAsync(OutboundApprovalGate.AutoSendKey, "true");
        await SetSettingAsync(OutboundDispatchPause.PausedKey, "true");
        var payout = await SeedRowAsync(BlockchainTransactionType.SELLER_PAYOUT);
        var refund = await SeedRowAsync(BlockchainTransactionType.BUYER_REFUND);
        var sweep = await SeedRowAsync(BlockchainTransactionType.SWEEP);
        await using (var ctx = CreateContext())
        {
            Assert.Null((await Service(ctx).ApproveAsync(_admin.Id, payout.Id, null, CancellationToken.None)).Error);
        }

        await DispatchAsync();
        await DispatchAsync();

        Assert.Empty(_tron.Broadcasts);
        await using (var ctx = CreateContext())
        {
            var rows = await ctx.Set<BlockchainTransaction>().AsNoTracking()
                .Where(b => b.Id == payout.Id || b.Id == refund.Id || b.Id == sweep.Id).ToListAsync();
            Assert.All(rows, r =>
            {
                Assert.Equal(BlockchainTransactionStatus.PENDING, r.Status);
                Assert.Equal(0, r.RetryCount);
                Assert.Null(r.TxHash);
                Assert.Null(r.NextAttemptAt);
            });
        }

        // Unpaused, they leave on the next run.
        await SetSettingAsync(OutboundDispatchPause.PausedKey, "false");
        await DispatchAsync();
        Assert.Equal(3, _tron.Broadcasts.Count);
    }

    [Theory]
    [InlineData("TRUE")]
    [InlineData("yes")]   // unparsable: an operator tried to set it — honoured as paused
    [InlineData("")]
    public async Task T172a_Any_Value_But_False_Reads_As_Paused(string value)
    {
        await SetSettingAsync(OutboundDispatchPause.PausedKey, value);
        await SeedRowAsync(BlockchainTransactionType.SWEEP);

        await DispatchAsync();

        Assert.Empty(_tron.Broadcasts);
    }

    [Fact]
    public async Task T172a_Pausing_Mid_Run_Stops_The_Very_Next_Row()
    {
        // Two sweeps in one run; the switch is turned on while the first one
        // is being sent. The second is not sent.
        var first = await SeedRowAsync(BlockchainTransactionType.SWEEP);
        var second = await SeedRowAsync(BlockchainTransactionType.SWEEP);
        _tron.OnBroadcast = () => SetSettingAsync(OutboundDispatchPause.PausedKey, "true");

        await DispatchAsync();

        Assert.Equal(first.Id, Assert.Single(_tron.Broadcasts).BlockchainTransactionId);
        await using var ctx = CreateContext();
        var held = await ctx.Set<BlockchainTransaction>().AsNoTracking().SingleAsync(b => b.Id == second.Id);
        Assert.Equal(BlockchainTransactionStatus.PENDING, held.Status);
        Assert.Equal(0, held.RetryCount);
    }

    private sealed class OneMinuteRetryPolicy : ITransferRetryPolicy
    {
        public Task<int> GetMaxAttemptsAsync(CancellationToken cancellationToken) => Task.FromResult(4);

        public Task<TimeSpan?> GetRetryDelayAsync(int retryCount, CancellationToken cancellationToken) =>
            Task.FromResult<TimeSpan?>(TimeSpan.FromMinutes(1));
    }

    private sealed class RecordingTransfers : IBlockchainTransferClient
    {
        public List<TransferBroadcastRequest> Broadcasts { get; } = [];

        /// <summary>T172a — runs while a row is being sent (e.g. an operator flips a setting).</summary>
        public Func<Task>? OnBroadcast { get; set; }

        public async Task<TransferBroadcastResult> BroadcastAsync(
            TransferBroadcastRequest request, CancellationToken cancellationToken)
        {
            Broadcasts.Add(request);
            if (OnBroadcast is not null) await OnBroadcast();
            return new TransferBroadcastResult(
                TransferBroadcastStatus.Success, "tron-sent-" + Broadcasts.Count, null, null);
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
