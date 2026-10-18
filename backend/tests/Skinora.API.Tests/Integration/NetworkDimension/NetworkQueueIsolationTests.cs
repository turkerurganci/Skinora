using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Skinora.API.Outbox;
using Skinora.API.Tests.Common;
using Skinora.Shared.Domain;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Interfaces;
using Skinora.Shared.Persistence;
using Skinora.Shared.Persistence.Outbox;
using Skinora.Shared.Tests.Integration;
using Skinora.Transactions.Application.Networks;
using Skinora.Transactions.Application.PaymentAddresses;
using Skinora.Transactions.Application.PaymentMonitoring;
using Skinora.Transactions.Application.Transfers;
using Skinora.Transactions.Domain.Entities;
using Skinora.Users.Domain.Entities;
using static Skinora.API.Tests.Integration.NetworkDimension.NetworkDimensionTestSupport;

namespace Skinora.API.Tests.Integration.NetworkDimension;

/// <summary>
/// T150 — "Yapılandırılmış ama erişilemeyen ağ (servis kapalı) ortak kuyrukları
/// tıkamaz: outbox ve gönderim/onay işleri için backoff ya da ölü-mektup"
/// (11 §F8 T150; T148 validation N1–N2; backlog <c>OutboxNoBackoffOrDeadLetter</c>),
/// on the migrated SQL Server schema — the T150 migration (OutboxMessages.NextAttemptAt)
/// included. Ethereum is configured and its service is down; TRON keeps moving.
/// </summary>
public class NetworkQueueIsolationTests : IntegrationTestBase
{
    protected override bool UseMigrations => true;

    static NetworkQueueIsolationTests() => RegisterAllModules();

    private static readonly DateTimeOffset Start = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);
    private readonly FakeTimeProvider _clock = new(Start);
    private User _seller = null!;

    protected override async Task SeedAsync(AppDbContext context)
    {
        _seller = new User
        {
            Id = Guid.NewGuid(),
            SteamId = "76561198000000977",
            SteamDisplayName = "T150 Seller",
        };
        context.Set<User>().Add(_seller);
        await context.SaveChangesAsync();
    }

    // ============================ Outbox ============================

    private static readonly OutboxOptions OutboxOptionsForTest = new()
    {
        PollingIntervalSeconds = 5,
        BatchSize = 10,
        MaxRetryCount = 3,
        RetryBackoffBaseSeconds = 5,
        RetryBackoffMaxSeconds = 900,
        RetrySlotsPerBatch = 10,
        LockAcquireTimeoutSeconds = 0,
    };

    private async Task<Guid> AddMonitorStartAsync(BlockchainNetwork network, int n)
    {
        var @event = new PaymentMonitorStartRequestedEvent(
            EventId: Guid.NewGuid(),
            TransactionId: Guid.NewGuid(),
            PaymentAddressId: Guid.NewGuid(),
            Address: network == BlockchainNetwork.TRON ? TronAddress : EvmLower,
            ExpectedToken: StablecoinType.USDT,
            ExpectedContractAddress: network == BlockchainNetwork.TRON ? TronAddress : EvmLower2,
            OccurredAt: Start.UtcDateTime,
            Network: network);
        await using var ctx = CreateContext();
        ctx.OutboxMessages.Add(new OutboxMessage
        {
            Id = @event.EventId,
            EventType = OutboxEventTypeName.For(typeof(PaymentMonitorStartRequestedEvent)),
            Payload = JsonSerializer.Serialize(@event, OutboxService.PayloadSerializerOptions),
            Status = OutboxMessageStatus.PENDING,
            // Strictly increasing, so "older than" is unambiguous.
            CreatedAt = Start.UtcDateTime.AddMilliseconds(n),
            Sequence = 1,
        });
        await ctx.SaveChangesAsync();
        return @event.EventId;
    }

    private async Task RunOutboxAsync(MonitorSidecar tron, MonitorSidecar ethereum, SpyOutboxAdminAlertSink alerts)
    {
        var registry = new FakeBlockchainServiceRegistry()
            .With(BlockchainNetwork.TRON, sidecar: tron)
            .With(BlockchainNetwork.ETHEREUM, sidecar: ethereum);
        var handler = new PaymentMonitorStartDispatcher(registry, NullLogger<PaymentMonitorStartDispatcher>.Instance);
        await using var ctx = CreateContext();
        var dispatcher = new OutboxDispatcher(
            ctx,
            new MonitorStartPublisher(handler),
            alerts,
            new InMemoryDistributedLockProvider(),
            new SpyJobScheduler(),
            Options.Create(OutboxOptionsForTest),
            _clock,
            NullLogger<OutboxDispatcher>.Instance);
        await dispatcher.ProcessAndRescheduleAsync();
        _clock.Advance(TimeSpan.FromSeconds(OutboxOptionsForTest.PollingIntervalSeconds));
    }

    [Fact]
    public async Task Monitor_Starts_Of_A_Down_Network_Do_Not_Stall_Trons_And_Are_Delivered_When_It_Returns()
    {
        // Three batches' worth of Ethereum monitor starts, all older than TRON's.
        var ethereumEvents = new List<Guid>();
        for (var i = 0; i < 3 * OutboxOptionsForTest.BatchSize; i++)
            ethereumEvents.Add(await AddMonitorStartAsync(BlockchainNetwork.ETHEREUM, i));
        var tronEvent = await AddMonitorStartAsync(BlockchainNetwork.TRON, 1_000);

        var tron = new MonitorSidecar(BlockchainSidecarStatus.Success);
        var ethereum = new MonitorSidecar(BlockchainSidecarStatus.Unavailable);
        var alerts = new SpyOutboxAdminAlertSink();

        // Polls 5 s apart. Before T150 the 10 oldest failures were re-read on
        // every poll ahead of everything else and the TRON event never got a
        // slot; now fresh events are read first and failures only once their
        // backoff (5, 10, 20 s …) has passed, into the slots fresh rows leave —
        // every Ethereum event is tried and TRON's goes out on the fourth poll.
        for (var poll = 0; poll < 4; poll++)
            await RunOutboxAsync(tron, ethereum, alerts);

        Assert.Single(tron.Started);
        // Every Ethereum event was attempted (none starved either).
        Assert.All(await FailedAttemptsAsync(ethereumEvents), attempts => Assert.True(attempts >= 1));
        await using (var ctx = CreateContext())
        {
            var tronRow = await ctx.OutboxMessages.AsNoTracking().SingleAsync(m => m.Id == tronEvent);
            Assert.Equal(OutboxMessageStatus.PROCESSED, tronRow.Status);
            var failed = await ctx.OutboxMessages.AsNoTracking()
                .Where(m => ethereumEvents.Contains(m.Id)).ToListAsync();
            Assert.All(failed, m =>
            {
                Assert.Equal(OutboxMessageStatus.FAILED, m.Status);
                // Backoff (5, 10 s …): 4 polls in 15 s give at most 3 attempts — fewer than one per poll.
                Assert.InRange(m.RetryCount, 1, 3);
                Assert.NotNull(m.NextAttemptAt);
            });
        }

        // Ethereum stays down long past the alert threshold: the rows keep
        // retrying at the capped backoff, the alert fires once per row.
        _clock.Advance(TimeSpan.FromHours(2));
        for (var round = 0; round < 6; round++)
        {
            for (var poll = 0; poll < 3; poll++)
                await RunOutboxAsync(tron, ethereum, alerts);
            _clock.Advance(TimeSpan.FromMinutes(16));
        }
        Assert.Equal(ethereumEvents.Count, alerts.Raised.Select(m => m.Id).Distinct().Count());
        Assert.Equal(alerts.Raised.Count, alerts.Raised.Select(m => m.Id).Distinct().Count());

        // The service comes back: every monitor start is delivered — nothing
        // was dead-lettered away.
        ethereum.Status = BlockchainSidecarStatus.Success;
        ethereum.Started.Clear();
        for (var poll = 0; poll < 3; poll++)
            await RunOutboxAsync(tron, ethereum, alerts);
        Assert.Equal(ethereumEvents.Count, ethereum.Started.Count);
        await using (var ctx = CreateContext())
        {
            Assert.Equal(
                ethereumEvents.Count + 1,
                await ctx.OutboxMessages.CountAsync(m => m.Status == OutboxMessageStatus.PROCESSED));
        }
    }

    private async Task<List<int>> FailedAttemptsAsync(List<Guid> ids)
    {
        await using var ctx = CreateContext();
        return await ctx.OutboxMessages.AsNoTracking()
            .Where(m => ids.Contains(m.Id))
            .Select(m => m.RetryCount)
            .ToListAsync();
    }

    // ===================== Outbound dispatch / confirmation =====================

    private async Task<BlockchainTransaction> SeedOutboundAsync(
        BlockchainNetwork network, BlockchainTransactionStatus status, int n)
    {
        await using var ctx = CreateContext();
        var tx = NewTransaction(_seller.Id, network: network);
        ctx.Set<Transaction>().Add(tx);
        await ctx.SaveChangesAsync();
        var row = new BlockchainTransaction
        {
            Id = Guid.NewGuid(),
            TransactionId = tx.Id,
            Network = network,
            Type = BlockchainTransactionType.SELLER_PAYOUT,
            TxHash = status == BlockchainTransactionStatus.DETECTED
                ? (network == BlockchainNetwork.TRON ? "" : "0x") + n.ToString("x64")
                : null,
            FromAddress = string.Empty,
            ToAddress = network == BlockchainNetwork.TRON ? SellerWallet : EvmLower,
            Amount = 50m,
            Token = StablecoinType.USDT,
            Status = status,
            CreatedAt = Start.UtcDateTime.AddSeconds(n),
        };
        ctx.Set<BlockchainTransaction>().Add(row);
        await ctx.SaveChangesAsync();
        return row;
    }

    [Fact]
    public async Task Dispatch_Sends_Tron_Behind_A_Full_Batch_Of_A_Down_Networks_Rows_And_Asks_It_Once()
    {
        var ethereumRows = new List<Guid>();
        for (var i = 0; i < OutgoingTransferDispatchJob.BatchSize + 5; i++)
            ethereumRows.Add((await SeedOutboundAsync(BlockchainNetwork.ETHEREUM, BlockchainTransactionStatus.PENDING, i)).Id);
        var tronRow = await SeedOutboundAsync(BlockchainNetwork.TRON, BlockchainTransactionStatus.PENDING, 1_000);

        var tron = new TransferStub { Broadcast = new(TransferBroadcastStatus.Success, "tron-tx-1", null, null) };
        var ethereum = new TransferStub
        {
            Broadcast = new(TransferBroadcastStatus.TransientFailure, null,
                HttpBlockchainTransferClient.TransportFailureCode, "connection refused"),
        };
        var registry = new FakeBlockchainServiceRegistry()
            .With(BlockchainNetwork.TRON, transfers: tron)
            .With(BlockchainNetwork.ETHEREUM, transfers: ethereum);

        await using (var ctx = CreateContext())
        {
            var job = new OutgoingTransferDispatchJob(
                ctx, registry, AlwaysHealthyBlockchainNetworks.Instance, new FixedRetryPolicy(),
                new OutboxService(ctx), StablecoinContractRegistry.FromOptions(new StablecoinContractOptions()),
                new BlockchainNetworkSettingsProvider(ctx), NullOutboundQueueObserver.Instance,
                new StubPlatformWalletAddressProvider(),
                _clock, NullLogger<OutgoingTransferDispatchJob>.Instance);
            await job.ExecuteAsync();
        }

        Assert.Equal(tronRow.Id, Assert.Single(tron.Broadcasts));
        Assert.Equal(ethereumRows[0], Assert.Single(ethereum.Broadcasts));
        await using var verify = CreateContext();
        var sent = await verify.Set<BlockchainTransaction>().AsNoTracking().SingleAsync(b => b.Id == tronRow.Id);
        Assert.Equal(BlockchainTransactionStatus.DETECTED, sent.Status);
        var untouched = await verify.Set<BlockchainTransaction>().AsNoTracking()
            .Where(b => ethereumRows.Skip(1).Contains(b.Id)).ToListAsync();
        Assert.All(untouched, b =>
        {
            Assert.Equal(BlockchainTransactionStatus.PENDING, b.Status);
            Assert.Equal(0, b.RetryCount);
        });
    }

    [Fact]
    public async Task Confirmation_Reads_Tron_Behind_A_Full_Batch_Of_A_Down_Networks_Rows()
    {
        for (var i = 0; i < OutgoingTransferConfirmationJob.BatchSize + 5; i++)
            await SeedOutboundAsync(BlockchainNetwork.ETHEREUM, BlockchainTransactionStatus.DETECTED, i);
        var tronRow = await SeedOutboundAsync(BlockchainNetwork.TRON, BlockchainTransactionStatus.DETECTED, 1_000);

        var tron = new TransferStub { Status = new(TransferStatusOutcome.Confirmed, 70_000_000L, 20, "SUCCESS", null) };
        var ethereum = new TransferStub { Status = new(TransferStatusOutcome.Unavailable, null, null, null, "down") };
        var registry = new FakeBlockchainServiceRegistry()
            .With(BlockchainNetwork.TRON, transfers: tron)
            .With(BlockchainNetwork.ETHEREUM, transfers: ethereum);

        await using (var ctx = CreateContext())
        {
            var job = new OutgoingTransferConfirmationJob(
                ctx, registry, AlwaysHealthyBlockchainNetworks.Instance, new OutboxService(ctx),
                StablecoinContractRegistry.FromOptions(new StablecoinContractOptions()),
                new StubPlatformWalletAddressProvider(),
                _clock, NullLogger<OutgoingTransferConfirmationJob>.Instance);
            await job.ExecuteAsync();
        }

        Assert.Equal(tronRow.TxHash, Assert.Single(tron.StatusReads));
        Assert.Single(ethereum.StatusReads);
        await using var verify = CreateContext();
        var confirmed = await verify.Set<BlockchainTransaction>().AsNoTracking().SingleAsync(b => b.Id == tronRow.Id);
        Assert.Equal(BlockchainTransactionStatus.CONFIRMED, confirmed.Status);
    }

    // ============================ fakes ============================

    private sealed class MonitorSidecar(BlockchainSidecarStatus status) : IBlockchainSidecarClient
    {
        public BlockchainSidecarStatus Status { get; set; } = status;
        public List<string> Started { get; } = new();

        public Task<BlockchainSidecarStatus> StartMonitoringAsync(
            PaymentMonitorStartRequest request, CancellationToken cancellationToken)
        {
            Started.Add(request.Address);
            return Task.FromResult(Status);
        }

        public Task<BlockchainSidecarDeriveResult> DeriveAddressAsync(int index, Guid transactionId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<BlockchainSidecarStatus> StopMonitoringAsync(string address, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<BlockchainSidecarStatus> StartPostCancelMonitoringAsync(PostCancelMonitorStartRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<BlockchainSidecarStatus> StopPostCancelMonitoringAsync(string address, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<BlockchainSidecarBalancesResult> GetWalletBalancesAsync(IReadOnlyList<string> addresses, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<BlockchainSidecarTransferResult> SendHotToColdTransferAsync(HotToColdTransferRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    /// <summary>Routes the one event type these tests publish to its production handler.</summary>
    private sealed class MonitorStartPublisher(PaymentMonitorStartDispatcher handler) : IPublisher
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) =>
            notification is PaymentMonitorStartRequestedEvent e
                ? handler.Handle(e, cancellationToken)
                : throw new InvalidOperationException($"Unexpected event {notification.GetType().Name}");

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification =>
            Publish((object)notification!, cancellationToken);
    }

    private sealed class TransferStub : IBlockchainTransferClient
    {
        public TransferBroadcastResult Broadcast { get; init; } = new(TransferBroadcastStatus.Success, "x", null, null);
        public TransferStatusResult Status { get; init; } = new(TransferStatusOutcome.Pending, null, null, null, null);
        public List<Guid> Broadcasts { get; } = new();
        public List<string> StatusReads { get; } = new();

        public Task<TransferBroadcastResult> BroadcastAsync(TransferBroadcastRequest request, CancellationToken cancellationToken)
        {
            Broadcasts.Add(request.BlockchainTransactionId);
            return Task.FromResult(Broadcast);
        }

        public Task<TransferStatusResult> GetStatusAsync(string txHash, CancellationToken cancellationToken)
        {
            StatusReads.Add(txHash);
            return Task.FromResult(Status);
        }
    }

    private sealed class FixedRetryPolicy : ITransferRetryPolicy
    {
        public Task<int> GetMaxAttemptsAsync(CancellationToken cancellationToken) => Task.FromResult(4);

        public Task<TimeSpan?> GetRetryDelayAsync(int retryCount, CancellationToken cancellationToken) =>
            Task.FromResult<TimeSpan?>(retryCount < 3 ? TimeSpan.FromMinutes(1) : null);
    }
}
