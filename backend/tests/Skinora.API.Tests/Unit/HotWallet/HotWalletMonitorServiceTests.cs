using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Skinora.API.Services.HotWallet;
using Skinora.API.Services.Reconciliation;
using Skinora.Platform.Domain.Entities;
using Skinora.Realtime.Application;
using Skinora.Realtime.Application.Contracts;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Application.PaymentAddresses;

namespace Skinora.API.Tests.Unit.HotWallet;

/// <summary>
/// Unit coverage for <see cref="HotWalletMonitorService"/> (T77 — 05 §3.3).
/// Uses an in-memory SQLite AppDbContext + stub sidecar + recording realtime
/// publisher to assert breach detection, idempotent skip on missing
/// settings, and exactly-one audit row per threshold crossing.
/// </summary>
[Trait("Category", "Unit")]
public sealed class HotWalletMonitorServiceTests : IDisposable
{
    private const string HotWalletAddress = "THotWalletFixtureAddress";

    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly StubBalancesSidecarClient _sidecar = new();
    private readonly RecordingPublisher _publisher = new();
    private readonly FakeTimeProvider _clock = new();
    private readonly HotWalletMonitorService _sut;

    public HotWalletMonitorServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();

        _clock.SetUtcNow(new DateTimeOffset(2026, 5, 17, 12, 0, 0, TimeSpan.Zero));

        _sut = new HotWalletMonitorService(
            _db,
            _sidecar,
            _publisher,
            _clock,
            NullLogger<HotWalletMonitorService>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task RunAsync_HotWalletUnconfigured_SkipsCleanly()
    {
        var outcome = await _sut.RunAsync(CancellationToken.None);

        Assert.False(outcome.HotWalletChecked);
        Assert.Equal(0, outcome.BreachCount);
        Assert.Empty(_sidecar.Calls);
        Assert.Empty(_publisher.Breaches);
        Assert.Empty(await LoadAuditLogsAsync());
    }

    [Fact]
    public async Task RunAsync_SidecarUnavailable_AbortsWithoutAudit()
    {
        await ConfigureHotWalletAsync();
        await UpsertSystemSettingAsync(HotWalletMonitorService.HotWalletLimitKey, "1000", "decimal");
        _sidecar.QueueUnavailable();

        var outcome = await _sut.RunAsync(CancellationToken.None);

        Assert.False(outcome.HotWalletChecked);
        Assert.Equal(0, outcome.BreachCount);
        Assert.Empty(await LoadAuditLogsAsync());
        Assert.Empty(_publisher.Breaches);
    }

    [Fact]
    public async Task RunAsync_BalancesAllWithinThresholds_NoBreach()
    {
        await ConfigureHotWalletAsync();
        await UpsertSystemSettingAsync(HotWalletMonitorService.HotWalletLimitKey, "1000", "decimal");
        await UpsertSystemSettingAsync(HotWalletMonitorService.TrxBalanceMinimumKey, "100", "decimal");

        _sidecar.QueueSnapshot(blockNumber: 200, addressTokens: new()
        {
            [HotWalletAddress] = new Dictionary<string, string>
            {
                ["USDT"] = "500000000",   // 500 USDT (under 1000)
                ["USDC"] = "750000000",   // 750 USDC (under 1000)
                ["TRX"] = "200000000",    // 200 TRX (above 100)
            },
        });

        var outcome = await _sut.RunAsync(CancellationToken.None);

        Assert.True(outcome.HotWalletChecked);
        Assert.Equal(0, outcome.BreachCount);
        Assert.Empty(_publisher.Breaches);
        Assert.Empty(await LoadAuditLogsAsync());
    }

    [Fact]
    public async Task RunAsync_UsdtAboveLimit_EmitsUpperBreach()
    {
        await ConfigureHotWalletAsync();
        await UpsertSystemSettingAsync(HotWalletMonitorService.HotWalletLimitKey, "1000", "decimal");

        _sidecar.QueueSnapshot(blockNumber: 201, addressTokens: new()
        {
            [HotWalletAddress] = new Dictionary<string, string>
            {
                ["USDT"] = "1500000000",  // 1500 > 1000
                ["USDC"] = "0",
                ["TRX"] = "200000000",
            },
        });

        var outcome = await _sut.RunAsync(CancellationToken.None);

        Assert.True(outcome.HotWalletChecked);
        Assert.Equal(1, outcome.BreachCount);

        var breach = Assert.Single(_publisher.Breaches);
        Assert.Equal("USDT", breach.Token);
        Assert.Equal(HotWalletMonitorService.DirectionUpper, breach.Direction);
        Assert.Equal(1000m, breach.Threshold);
        Assert.Equal(1500m, breach.Actual);
        Assert.Equal(201L, breach.BlockNumber);

        var audit = Assert.Single(await LoadAuditLogsAsync());
        Assert.Equal(AuditAction.HOT_WALLET_THRESHOLD_BREACHED, audit.Action);
        Assert.Equal("HotWallet", audit.EntityType);
        Assert.Equal("USDT", audit.EntityId);
        Assert.Equal(ActorType.SYSTEM, audit.ActorType);
        Assert.NotNull(audit.NewValue);
        using var json = JsonDocument.Parse(audit.NewValue!);
        Assert.Equal("USDT", json.RootElement.GetProperty("token").GetString());
        Assert.Equal(HotWalletMonitorService.DirectionUpper, json.RootElement.GetProperty("direction").GetString());
    }

    [Fact]
    public async Task RunAsync_BothStablecoinsExceedLimit_EmitsTwoUpperBreaches()
    {
        await ConfigureHotWalletAsync();
        await UpsertSystemSettingAsync(HotWalletMonitorService.HotWalletLimitKey, "100", "decimal");

        _sidecar.QueueSnapshot(blockNumber: 202, addressTokens: new()
        {
            [HotWalletAddress] = new Dictionary<string, string>
            {
                ["USDT"] = "200000000",  // 200 > 100
                ["USDC"] = "300000000",  // 300 > 100
                ["TRX"] = "200000000",
            },
        });

        var outcome = await _sut.RunAsync(CancellationToken.None);

        Assert.Equal(2, outcome.BreachCount);
        Assert.Equal(2, _publisher.Breaches.Count);
        Assert.Contains(_publisher.Breaches, b => b.Token == "USDT");
        Assert.Contains(_publisher.Breaches, b => b.Token == "USDC");
        Assert.All(_publisher.Breaches, b =>
            Assert.Equal(HotWalletMonitorService.DirectionUpper, b.Direction));
        Assert.Equal(2, (await LoadAuditLogsAsync()).Count);
    }

    [Fact]
    public async Task RunAsync_TrxBelowMinimum_EmitsLowerBreach()
    {
        await ConfigureHotWalletAsync();
        await UpsertSystemSettingAsync(HotWalletMonitorService.TrxBalanceMinimumKey, "100", "decimal");

        _sidecar.QueueSnapshot(blockNumber: 203, addressTokens: new()
        {
            [HotWalletAddress] = new Dictionary<string, string>
            {
                ["USDT"] = "0",
                ["USDC"] = "0",
                ["TRX"] = "50000000",  // 50 TRX < 100 minimum
            },
        });

        var outcome = await _sut.RunAsync(CancellationToken.None);

        Assert.Equal(1, outcome.BreachCount);
        var breach = Assert.Single(_publisher.Breaches);
        Assert.Equal(HotWalletMonitorService.TokenTrx, breach.Token);
        Assert.Equal(HotWalletMonitorService.DirectionLower, breach.Direction);
        Assert.Equal(100m, breach.Threshold);
        Assert.Equal(50m, breach.Actual);

        var audit = Assert.Single(await LoadAuditLogsAsync());
        Assert.Equal("TRX", audit.EntityId);
    }

    [Fact]
    public async Task RunAsync_NoLimitConfigured_DoesNotEmitStablecoinBreach()
    {
        await ConfigureHotWalletAsync();
        // hot_wallet_limit ships Unconfigured by SystemSettingSeed; we leave it so.
        _sidecar.QueueSnapshot(blockNumber: 204, addressTokens: new()
        {
            [HotWalletAddress] = new Dictionary<string, string>
            {
                ["USDT"] = "99999000000",  // huge; should NOT alert since no limit configured
                ["USDC"] = "0",
                ["TRX"] = "200000000",
            },
        });

        var outcome = await _sut.RunAsync(CancellationToken.None);

        Assert.True(outcome.HotWalletChecked);
        Assert.Equal(0, outcome.BreachCount);
        Assert.Empty(_publisher.Breaches);
    }

    [Fact]
    public async Task RunAsync_HotWalletAddressIsNoneSentinel_TreatsAsUnconfigured()
    {
        await UpsertSystemSettingAsync(
            ReconciliationService.HotWalletAddressKey, "NONE", "string");

        var outcome = await _sut.RunAsync(CancellationToken.None);

        Assert.False(outcome.HotWalletChecked);
        Assert.Empty(_sidecar.Calls);
    }

    // ─── Helpers ────────────────────────────────────────────────────────

    private Task ConfigureHotWalletAsync() =>
        UpsertSystemSettingAsync(
            ReconciliationService.HotWalletAddressKey, HotWalletAddress, "string");

    private async Task UpsertSystemSettingAsync(string key, string value, string dataType)
    {
        var existing = await _db.Set<SystemSetting>()
            .FirstOrDefaultAsync(s => s.Key == key);
        if (existing is null)
        {
            _db.Set<SystemSetting>().Add(new SystemSetting
            {
                Id = Guid.NewGuid(),
                Key = key,
                Value = value,
                IsConfigured = true,
                DataType = dataType,
                Category = "Monitoring",
                Description = "HotWalletMonitor fixture",
                CreatedAt = _clock.GetUtcNow().UtcDateTime,
                UpdatedAt = _clock.GetUtcNow().UtcDateTime,
                RowVersion = new byte[8],
            });
        }
        else
        {
            existing.Value = value;
            existing.IsConfigured = true;
            existing.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
        }
        await _db.SaveChangesAsync();
    }

    private async Task<List<AuditLog>> LoadAuditLogsAsync() =>
        await _db.Set<AuditLog>().AsNoTracking()
            .Where(a => a.Action == AuditAction.HOT_WALLET_THRESHOLD_BREACHED)
            .ToListAsync();

    private sealed class StubBalancesSidecarClient : IBlockchainSidecarClient
    {
        public Queue<BlockchainSidecarBalancesResult> Responses { get; } = new();
        public List<IReadOnlyList<string>> Calls { get; } = new();

        public Task<BlockchainSidecarDeriveResult> DeriveAddressAsync(
            int index, Guid transactionId, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<BlockchainSidecarStatus> StartPostCancelMonitoringAsync(
            PostCancelMonitorStartRequest request, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<BlockchainSidecarStatus> StopPostCancelMonitoringAsync(
            string address, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<BlockchainSidecarBalancesResult> GetWalletBalancesAsync(
            IReadOnlyList<string> addresses, CancellationToken cancellationToken)
        {
            Calls.Add(addresses);
            return Task.FromResult(Responses.Count > 0
                ? Responses.Dequeue()
                : BlockchainSidecarBalancesResult.Unavailable);
        }

        public Task<BlockchainSidecarTransferResult> SendHotToColdTransferAsync(
            HotToColdTransferRequest request, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public void QueueSnapshot(
            long blockNumber,
            Dictionary<string, IReadOnlyDictionary<string, string>> addressTokens)
        {
            var rows = addressTokens
                .Select(kv => new BlockchainSidecarAddressBalances(kv.Key, kv.Value))
                .ToList();
            Responses.Enqueue(new BlockchainSidecarBalancesResult(
                BlockchainSidecarStatus.Success, blockNumber, rows));
        }

        public void QueueUnavailable() =>
            Responses.Enqueue(BlockchainSidecarBalancesResult.Unavailable);
    }

    private sealed class RecordingPublisher : INotificationRealtimePublisher
    {
        public List<NotificationRealtimePayloads.AdminHotWalletThresholdBreached> Breaches { get; }
            = new();

        public Task PublishNewNotificationAsync(
            Guid userId,
            NotificationRealtimePayloads.NewNotification payload,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task PublishUnreadCountChangedAsync(
            Guid userId,
            NotificationRealtimePayloads.UnreadCountChanged payload,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task PublishTelegramConnectedAsync(
            Guid userId,
            NotificationRealtimePayloads.TelegramConnected payload,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task PublishDiscordConnectedAsync(
            Guid userId,
            NotificationRealtimePayloads.DiscordConnected payload,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task PublishMaintenanceStatusChangedAsync(
            NotificationRealtimePayloads.MaintenanceStatusChanged payload,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task PublishAdminBotStatusChangedAsync(
            NotificationRealtimePayloads.AdminBotStatusChanged payload,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task PublishAdminReconciliationMismatchAsync(
            NotificationRealtimePayloads.AdminReconciliationMismatch payload,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task PublishAdminHotWalletThresholdBreachedAsync(
            NotificationRealtimePayloads.AdminHotWalletThresholdBreached payload,
            CancellationToken cancellationToken)
        {
            Breaches.Add(payload);
            return Task.CompletedTask;
        }
    }
}
