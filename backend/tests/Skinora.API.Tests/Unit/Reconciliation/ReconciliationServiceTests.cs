using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Skinora.API.Services.Reconciliation;
using Skinora.Payments.Domain.Entities;
using Skinora.Platform.Domain.Entities;
using Skinora.Realtime.Application;
using Skinora.Realtime.Application.Contracts;
using Skinora.Shared.Domain.Seed;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Application.PaymentAddresses;
using Skinora.Transactions.Application.Reconciliation;
using Skinora.Transactions.Domain.Entities;

namespace Skinora.API.Tests.Unit.Reconciliation;

/// <summary>
/// Unit coverage for <see cref="ReconciliationService"/> (T76 — 05 §3.3).
/// Drives the three scopes (deposit / hot / cold) against a SQLite in-memory
/// AppDbContext + stub sidecar + stub realtime publisher.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ReconciliationServiceTests : IDisposable
{
    private const string HotWalletAddress = "THotWalletFixtureAddress";
    private const string ColdWalletAddress = "TColdWalletFixtureAddress";

    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly StubBalancesSidecarClient _sidecar = new();
    private readonly RecordingPublisher _publisher = new();
    private readonly FakeTimeProvider _clock = new();
    private readonly ReconciliationService _sut;
    private int _syntheticSweepSeq;

    public ReconciliationServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();

        _clock.SetUtcNow(new DateTimeOffset(2026, 5, 17, 3, 0, 0, TimeSpan.Zero));

        _sut = new ReconciliationService(
            _db,
            _sidecar,
            _publisher,
            _clock,
            NullLogger<ReconciliationService>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    // ─── Skip & no-op paths ─────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_NoActiveDepositsAndNoWalletsConfigured_NoOpAndReturnsZeroOutcome()
    {
        var outcome = await _sut.RunAsync(CancellationToken.None);

        Assert.Equal(0, outcome.DepositAddressesChecked);
        Assert.False(outcome.HotWalletChecked);
        Assert.False(outcome.ColdWalletChecked);
        Assert.Equal(0, outcome.MismatchCount);
        Assert.Empty(_sidecar.Calls);
        Assert.Empty(_publisher.Mismatches);
        Assert.Empty(await LoadAuditLogsAsync());
    }

    [Fact]
    public async Task RunAsync_HotWalletUnconfigured_SkipsHotScopeWithoutFailing()
    {
        await ConfigureColdWalletAsync();
        await SeedColdWalletInflowAsync(StablecoinType.USDT, 100m);
        _sidecar.QueueSnapshot(blockNumber: 1, addressTokens: new()
        {
            [ColdWalletAddress] = SnapshotForUsdt("100000000"),
        });

        var outcome = await _sut.RunAsync(CancellationToken.None);

        Assert.False(outcome.HotWalletChecked);
        Assert.True(outcome.ColdWalletChecked);
        Assert.Equal(0, outcome.MismatchCount);
    }

    [Fact]
    public async Task RunAsync_SidecarUnavailable_AbortsRunWithoutAudit()
    {
        await ConfigureHotWalletAsync();
        _sidecar.QueueUnavailable();

        var outcome = await _sut.RunAsync(CancellationToken.None);

        Assert.Equal(0, outcome.MismatchCount);
        Assert.False(outcome.HotWalletChecked);
        Assert.Empty(_publisher.Mismatches);
        Assert.Empty(await LoadAuditLogsAsync());
    }

    // ─── Deposit address scope ──────────────────────────────────────────

    [Fact]
    public async Task RunAsync_DepositAddress_BalanceMatchesLedger_NoMismatchRecorded()
    {
        var deposit = await SeedDepositAddressAsync("TDepositFixture-1");
        await SeedConfirmedDepositInflowAsync(deposit.Id, deposit.Address,
            BlockchainTransactionType.BUYER_PAYMENT, StablecoinType.USDT, 50m);

        _sidecar.QueueSnapshot(blockNumber: 100, addressTokens: new()
        {
            [deposit.Address] = SnapshotForUsdt("50000000"),
        });

        var outcome = await _sut.RunAsync(CancellationToken.None);

        Assert.Equal(1, outcome.DepositAddressesChecked);
        Assert.Equal(0, outcome.MismatchCount);
        Assert.Empty(_publisher.Mismatches);
        Assert.Empty(await LoadAuditLogsAsync());
    }

    [Fact]
    public async Task RunAsync_DepositAddress_ShortfallRaisesMismatchAndPushesAdmin()
    {
        var deposit = await SeedDepositAddressAsync("TDepositFixture-2");
        await SeedConfirmedDepositInflowAsync(deposit.Id, deposit.Address,
            BlockchainTransactionType.BUYER_PAYMENT, StablecoinType.USDT, 100m);

        // On-chain reports only 99.5 USDT — half-USDT short.
        _sidecar.QueueSnapshot(blockNumber: 200, addressTokens: new()
        {
            [deposit.Address] = SnapshotForUsdt("99500000"),
        });

        var outcome = await _sut.RunAsync(CancellationToken.None);

        Assert.Equal(1, outcome.MismatchCount);
        var push = Assert.Single(_publisher.Mismatches);
        Assert.Equal(ReconciliationScope.DepositAddress.ToString(), push.Scope);
        Assert.Equal(deposit.Address, push.Address);
        Assert.Equal(StablecoinType.USDT.ToString(), push.Token);
        Assert.Equal(100m, push.Expected);
        Assert.Equal(99.5m, push.Actual);
        Assert.Equal(-0.5m, push.Delta);
        Assert.Equal(200L, push.BlockNumber);

        var audit = Assert.Single(await LoadAuditLogsAsync());
        Assert.Equal(AuditAction.RECONCILIATION_MISMATCH, audit.Action);
        Assert.Equal("DepositAddress", audit.EntityType);
        Assert.Equal(deposit.Address, audit.EntityId);
        var payload = JsonSerializer.Deserialize<JsonElement>(audit.NewValue!);
        Assert.Equal("USDT", payload.GetProperty("token").GetString());
        Assert.Equal(100m, decimal.Parse(
            payload.GetProperty("expected").GetString()!, CultureInfo.InvariantCulture));
        Assert.Equal(99.5m, decimal.Parse(
            payload.GetProperty("actual").GetString()!, CultureInfo.InvariantCulture));
        Assert.Equal(SeedConstants.SystemUserId, audit.ActorId);
        Assert.Equal(ActorType.SYSTEM, audit.ActorType);
    }

    [Fact]
    public async Task RunAsync_DepositAddress_InFlightDetectedExcludedFromExpected()
    {
        var deposit = await SeedDepositAddressAsync("TDepositFixture-3");
        await SeedConfirmedDepositInflowAsync(deposit.Id, deposit.Address,
            BlockchainTransactionType.BUYER_PAYMENT, StablecoinType.USDT, 50m);
        // PENDING inflow row that has not finalized — must NOT count toward expected.
        await SeedBlockchainTransactionAsync(
            type: BlockchainTransactionType.BUYER_PAYMENT,
            token: StablecoinType.USDT,
            amount: 25m,
            status: BlockchainTransactionStatus.PENDING,
            toAddress: deposit.Address,
            paymentAddressId: deposit.Id);

        // On-chain reports 50 USDT (the CONFIRMED row), pending row is not yet
        // visible / not relevant for the reconciliation comparison.
        _sidecar.QueueSnapshot(blockNumber: 1, addressTokens: new()
        {
            [deposit.Address] = SnapshotForUsdt("50000000"),
        });

        var outcome = await _sut.RunAsync(CancellationToken.None);

        Assert.Equal(0, outcome.MismatchCount);
    }

    [Fact]
    public async Task RunAsync_DepositAddress_SweptToHotWallet_BalanceCollapsesToZero()
    {
        var deposit = await SeedDepositAddressAsync("TDepositFixture-4");
        await SeedConfirmedDepositInflowAsync(deposit.Id, deposit.Address,
            BlockchainTransactionType.BUYER_PAYMENT, StablecoinType.USDT, 100m);
        await SeedBlockchainTransactionAsync(
            fromAddress: deposit.Address, paymentAddressId: deposit.Id,
            type: BlockchainTransactionType.SWEEP, token: StablecoinType.USDT,
            amount: 100m, status: BlockchainTransactionStatus.CONFIRMED,
            toAddress: HotWalletAddress);

        _sidecar.QueueSnapshot(blockNumber: 1, addressTokens: new()
        {
            [deposit.Address] = SnapshotForUsdt("0"),
        });

        var outcome = await _sut.RunAsync(CancellationToken.None);

        Assert.Equal(0, outcome.MismatchCount);
    }

    // ─── Hot wallet scope ───────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_HotWallet_AccountsForSweepInflowMinusPayoutAndColdTransfer()
    {
        await ConfigureHotWalletAsync();

        // Inflow: 200 USDT swept in.
        await SeedBlockchainTransactionAsync(
            toAddress: HotWalletAddress, type: BlockchainTransactionType.SWEEP,
            token: StablecoinType.USDT, amount: 200m,
            status: BlockchainTransactionStatus.CONFIRMED);
        // Outflow on-chain: 50 USDT seller payout.
        await SeedBlockchainTransactionAsync(
            fromAddress: HotWalletAddress, toAddress: "TSellerExternal",
            type: BlockchainTransactionType.SELLER_PAYOUT,
            token: StablecoinType.USDT, amount: 50m,
            status: BlockchainTransactionStatus.CONFIRMED);
        // Outflow off-chain: 100 USDT hot→cold transfer (admin-initiated).
        await SeedColdWalletTransferAsync(StablecoinType.USDT, 100m,
            from: HotWalletAddress, to: ColdWalletAddress);

        // Expected = 200 - 50 - 100 = 50 USDT.
        _sidecar.QueueSnapshot(blockNumber: 1, addressTokens: new()
        {
            [HotWalletAddress] = SnapshotForUsdt("50000000"),
        });

        var outcome = await _sut.RunAsync(CancellationToken.None);

        Assert.True(outcome.HotWalletChecked);
        Assert.Equal(0, outcome.MismatchCount);
    }

    [Fact]
    public async Task RunAsync_HotWallet_OnChainSurplusRaisesMismatch()
    {
        await ConfigureHotWalletAsync();
        await SeedBlockchainTransactionAsync(
            toAddress: HotWalletAddress, type: BlockchainTransactionType.SWEEP,
            token: StablecoinType.USDC, amount: 100m,
            status: BlockchainTransactionStatus.CONFIRMED);

        // On-chain shows 150 USDC — 50 USDC unexplained inbound.
        _sidecar.QueueSnapshot(blockNumber: 1, addressTokens: new()
        {
            [HotWalletAddress] = SnapshotForUsdc("150000000"),
        });

        var outcome = await _sut.RunAsync(CancellationToken.None);

        Assert.Equal(1, outcome.MismatchCount);
        var push = Assert.Single(_publisher.Mismatches);
        Assert.Equal("HotWallet", push.Scope);
        Assert.Equal(HotWalletAddress, push.Address);
        Assert.Equal("USDC", push.Token);
        Assert.Equal(100m, push.Expected);
        Assert.Equal(150m, push.Actual);
        Assert.Equal(50m, push.Delta);
    }

    // ─── Cold wallet scope ──────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_ColdWallet_SumsColdWalletTransferLedger()
    {
        await ConfigureColdWalletAsync();
        await SeedColdWalletInflowAsync(StablecoinType.USDT, 80m);
        await SeedColdWalletInflowAsync(StablecoinType.USDC, 20m);

        _sidecar.QueueSnapshot(blockNumber: 1, addressTokens: new()
        {
            [ColdWalletAddress] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["TRX"] = "0",
                ["USDT"] = "80000000",
                ["USDC"] = "20000000",
            },
        });

        var outcome = await _sut.RunAsync(CancellationToken.None);

        Assert.True(outcome.ColdWalletChecked);
        Assert.Equal(0, outcome.MismatchCount);
    }

    // ─── Multi-token & combined scopes ──────────────────────────────────

    [Fact]
    public async Task RunAsync_MultiToken_OnlyMismatchingTokenRaised()
    {
        await ConfigureHotWalletAsync();
        await SeedBlockchainTransactionAsync(
            toAddress: HotWalletAddress, type: BlockchainTransactionType.SWEEP,
            token: StablecoinType.USDT, amount: 100m,
            status: BlockchainTransactionStatus.CONFIRMED);
        await SeedBlockchainTransactionAsync(
            toAddress: HotWalletAddress, type: BlockchainTransactionType.SWEEP,
            token: StablecoinType.USDC, amount: 50m,
            status: BlockchainTransactionStatus.CONFIRMED);

        // USDT matches (100), USDC short (40 on chain vs 50 expected).
        _sidecar.QueueSnapshot(blockNumber: 1, addressTokens: new()
        {
            [HotWalletAddress] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["USDT"] = "100000000",
                ["USDC"] = "40000000",
                ["TRX"] = "0",
            },
        });

        var outcome = await _sut.RunAsync(CancellationToken.None);

        Assert.Equal(1, outcome.MismatchCount);
        var push = Assert.Single(_publisher.Mismatches);
        Assert.Equal("USDC", push.Token);
        Assert.Equal(-10m, push.Delta);
    }

    // ─── Seeding helpers ────────────────────────────────────────────────

    private static IReadOnlyDictionary<string, string> SnapshotForUsdt(string raw) =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["USDT"] = raw,
            ["USDC"] = "0",
            ["TRX"] = "0",
        };

    private static IReadOnlyDictionary<string, string> SnapshotForUsdc(string raw) =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["USDT"] = "0",
            ["USDC"] = raw,
            ["TRX"] = "0",
        };

    private async Task<PaymentAddress> SeedDepositAddressAsync(string address)
    {
        // PaymentAddress requires a parent Transaction row (FK). The
        // reconciliation queries IgnoreQueryFilters and only project Id +
        // Address so we keep the parent minimally populated to satisfy
        // SQLite constraints. The wider transaction-lifecycle tests cover
        // the full graph; here we only need a unique key.
        var txId = Guid.NewGuid();
        await SeedParentTransactionAsync(txId);

        var pa = new PaymentAddress
        {
            Id = Guid.NewGuid(),
            TransactionId = txId,
            Address = address,
            HdWalletIndex = (int)(DateTime.UtcNow.Ticks & 0x7fffffff),
            ExpectedAmount = 0m,
            ExpectedToken = StablecoinType.USDT,
            MonitoringStatus = MonitoringStatus.ACTIVE,
            CreatedAt = _clock.GetUtcNow().UtcDateTime,
            UpdatedAt = _clock.GetUtcNow().UtcDateTime,
            RowVersion = new byte[8],
        };
        _db.Set<PaymentAddress>().Add(pa);
        await _db.SaveChangesAsync();
        return pa;
    }

    private async Task<Guid> SeedSyntheticStoppedDepositAsync()
    {
        // A STOPPED deposit + its own parent transaction (PaymentAddress has a
        // 1:1 unique TransactionId). STOPPED keeps it out of the deposit
        // reconciliation scan; it exists only to anchor a hot-scope SWEEP row
        // to a valid PaymentAddressId (WP3 CK_..._Type_Sweep).
        var seq = ++_syntheticSweepSeq;
        var txId = Guid.NewGuid();
        await SeedParentTransactionAsync(txId);

        var paId = Guid.NewGuid();
        _db.Set<PaymentAddress>().Add(new PaymentAddress
        {
            Id = paId,
            TransactionId = txId,
            Address = $"TSyntheticSweepDeposit-{seq}",
            HdWalletIndex = 900_000 + seq,
            ExpectedAmount = 0m,
            ExpectedToken = StablecoinType.USDT,
            MonitoringStatus = MonitoringStatus.STOPPED,
            CreatedAt = _clock.GetUtcNow().UtcDateTime,
            UpdatedAt = _clock.GetUtcNow().UtcDateTime,
            RowVersion = new byte[8],
        });
        await _db.SaveChangesAsync();
        return paId;
    }

    private async Task SeedParentTransactionAsync(Guid txId)
    {
        // Minimal Transaction row to satisfy PaymentAddress FK. The reconciliation
        // service never reads Transaction fields, only its own
        // BlockchainTransaction + PaymentAddress projections, so default values
        // are sufficient.
        var tx = new Transaction
        {
            Id = txId,
            SellerId = SeedConstants.SystemUserId,
            BuyerId = SeedConstants.SystemUserId,
            Status = TransactionStatus.CREATED,
            Price = 100m,
            CommissionRate = 0.02m,
            CommissionAmount = 2m,
            TotalAmount = 102m,
            StablecoinType = StablecoinType.USDT,
            BuyerIdentificationMethod = BuyerIdentificationMethod.OPEN_LINK,
            InviteToken = $"INV{txId.ToString("N")[..8].ToUpperInvariant()}",
            // Distinct per row: every fixture shares SeedConstants.SystemUserId
            // as the seller, and UQ_Transactions_SellerId_ItemAssetId_Active
            // allows only one open transaction per (seller, item).
            ItemAssetId = txId.ToString("N")[..12],
            SellerPayoutAddress = "TSellerPayoutFixture",
            PaymentTimeoutMinutes = 60,
            CreatedAt = _clock.GetUtcNow().UtcDateTime,
            UpdatedAt = _clock.GetUtcNow().UtcDateTime,
            RowVersion = new byte[8],
        };
        _db.Set<Transaction>().Add(tx);
        await _db.SaveChangesAsync();
    }

    private Task SeedConfirmedDepositInflowAsync(
        Guid paymentAddressId, string depositAddress,
        BlockchainTransactionType type, StablecoinType token, decimal amount) =>
        SeedBlockchainTransactionAsync(
            toAddress: depositAddress, paymentAddressId: paymentAddressId,
            type: type, token: token, amount: amount,
            status: BlockchainTransactionStatus.CONFIRMED);

    private async Task SeedBlockchainTransactionAsync(
        BlockchainTransactionType type,
        StablecoinType token,
        decimal amount,
        BlockchainTransactionStatus status,
        string toAddress = "",
        string fromAddress = "",
        Guid? paymentAddressId = null)
    {
        // WP3 — CK_BlockchainTransactions_Type_Sweep requires every SWEEP row to
        // carry a deposit PaymentAddressId. Hot-wallet-scope fixtures that don't
        // model a specific deposit get a synthetic STOPPED deposit: it satisfies
        // the constraint while staying out of the deposit reconciliation scan
        // (which excludes MonitoringStatus.STOPPED), so the hot-scope assertions
        // are unaffected.
        if (type == BlockchainTransactionType.SWEEP && paymentAddressId is null)
        {
            paymentAddressId = await SeedSyntheticStoppedDepositAsync();
        }

        var txId = paymentAddressId is null
            ? await EnsureFloatingParentTransactionAsync()
            : await _db.Set<PaymentAddress>().AsNoTracking()
                .Where(pa => pa.Id == paymentAddressId)
                .Select(pa => pa.TransactionId)
                .FirstAsync();

        // CK_BlockchainTransactions_Status_* require ConfirmationCount /
        // ConfirmedAt to line up with Status (06 §3.8).
        var (confirmationCount, confirmedAt) = status switch
        {
            BlockchainTransactionStatus.CONFIRMED => (20, _clock.GetUtcNow().UtcDateTime),
            BlockchainTransactionStatus.PENDING => (5, (DateTime?)null),
            BlockchainTransactionStatus.DETECTED => (0, (DateTime?)null),
            BlockchainTransactionStatus.FAILED => (0, (DateTime?)null),
            _ => (0, (DateTime?)null),
        };

        var row = new BlockchainTransaction
        {
            Id = Guid.NewGuid(),
            TransactionId = txId,
            PaymentAddressId = paymentAddressId,
            Type = type,
            Token = token,
            Amount = amount,
            Status = status,
            FromAddress = fromAddress,
            ToAddress = toAddress,
            TxHash = $"tx-{Guid.NewGuid():N}",
            ConfirmationCount = confirmationCount,
            CreatedAt = _clock.GetUtcNow().UtcDateTime,
            ConfirmedAt = confirmedAt,
        };
        _db.Set<BlockchainTransaction>().Add(row);
        await _db.SaveChangesAsync();
    }

    private async Task<Guid> EnsureFloatingParentTransactionAsync()
    {
        // BlockchainTransaction.TransactionId is non-nullable; for hot/cold
        // wallet rows that don't correspond to a deposit, reuse a single
        // synthetic transaction so foreign keys remain satisfied without
        // bloating the test fixture.
        var existing = await _db.Set<Transaction>().AsNoTracking()
            .Where(t => t.InviteToken == "RECON-FLT")
            .Select(t => (Guid?)t.Id)
            .FirstOrDefaultAsync();
        if (existing.HasValue) return existing.Value;

        var txId = Guid.NewGuid();
        var tx = new Transaction
        {
            Id = txId,
            SellerId = SeedConstants.SystemUserId,
            BuyerId = SeedConstants.SystemUserId,
            Status = TransactionStatus.CREATED,
            Price = 0m,
            CommissionRate = 0m,
            CommissionAmount = 0m,
            TotalAmount = 0m,
            StablecoinType = StablecoinType.USDT,
            BuyerIdentificationMethod = BuyerIdentificationMethod.OPEN_LINK,
            InviteToken = "RECON-FLT",
            ItemAssetId = "RECON-FLT-AST",
            SellerPayoutAddress = "TSellerPayoutFloating",
            PaymentTimeoutMinutes = 60,
            CreatedAt = _clock.GetUtcNow().UtcDateTime,
            UpdatedAt = _clock.GetUtcNow().UtcDateTime,
            RowVersion = new byte[8],
        };
        _db.Set<Transaction>().Add(tx);
        await _db.SaveChangesAsync();
        return txId;
    }

    private Task SeedColdWalletInflowAsync(StablecoinType token, decimal amount) =>
        SeedColdWalletTransferAsync(token, amount, from: HotWalletAddress, to: ColdWalletAddress);

    private async Task SeedColdWalletTransferAsync(
        StablecoinType token, decimal amount, string from, string to)
    {
        var row = new ColdWalletTransfer
        {
            Amount = amount,
            Token = token,
            FromAddress = from,
            ToAddress = to,
            TxHash = $"cw-{Guid.NewGuid():N}",
            InitiatedByAdminId = SeedConstants.SystemUserId,
            CreatedAt = _clock.GetUtcNow().UtcDateTime,
        };
        _db.Set<ColdWalletTransfer>().Add(row);
        await _db.SaveChangesAsync();
    }

    private async Task ConfigureHotWalletAsync()
    {
        await UpsertSystemSettingAsync(
            ReconciliationService.HotWalletAddressKey, HotWalletAddress);
    }

    private async Task ConfigureColdWalletAsync()
    {
        await UpsertSystemSettingAsync(
            ReconciliationService.ColdWalletAddressKey, ColdWalletAddress);
    }

    private async Task UpsertSystemSettingAsync(string key, string value)
    {
        // SystemSettingSeed.All ships every reconciliation key as Unconfigured
        // via HasData, so EnsureCreated leaves a row in place; the fixture
        // simply flips IsConfigured + Value rather than inserting a duplicate
        // (UQ_SystemSettings_Key would otherwise reject the second row).
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
                DataType = "string",
                Category = "Monitoring",
                Description = "Reconciliation fixture",
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
            .Where(a => a.Action == AuditAction.RECONCILIATION_MISMATCH)
            .ToListAsync();

    // ─── Stubs ──────────────────────────────────────────────────────────

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
        public List<NotificationRealtimePayloads.AdminReconciliationMismatch> Mismatches { get; } = new();

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

        public Task PublishAdminReconciliationMismatchAsync(
            NotificationRealtimePayloads.AdminReconciliationMismatch payload,
            CancellationToken cancellationToken)
        {
            Mismatches.Add(payload);
            return Task.CompletedTask;
        }

        public Task PublishAdminHotWalletThresholdBreachedAsync(
            NotificationRealtimePayloads.AdminHotWalletThresholdBreached payload,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
