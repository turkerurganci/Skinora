using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Skinora.API.Tests.Common;
using Skinora.API.Services.HotWallet;
using Skinora.API.Services.Reconciliation;
using Skinora.Payments.Domain.Entities;
using Skinora.Platform.Domain.Entities;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Application.PaymentAddresses;
using Skinora.Transactions.Application.Wallets;
using Skinora.Users.Domain.Entities;

namespace Skinora.API.Tests.Unit.HotWallet;

/// <summary>
/// Unit coverage for <see cref="HotWalletService"/> (T77 — 05 §3.3) — the
/// admin-driven hot→cold consolidation orchestrator. Asserts the
/// validation gate, sidecar contract, and the post-success persistence
/// invariant (ColdWalletTransfer ledger row + COLD_WALLET_TRANSFER_INITIATED
/// AuditLog row written inside a single SaveChanges scope).
/// </summary>
[Trait("Category", "Unit")]
public sealed class HotWalletServiceTests : IDisposable
{
    private const string HotWalletAddress = "THotWalletFixtureAddress";
    private const string ColdWalletAddress = "TColdWalletFixtureAddress";
    private static readonly Guid AdminId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly StubSidecarClient _sidecar = new();
    private readonly FakeTimeProvider _clock = new();
    private readonly StubPlatformWalletAddressProvider _walletAddresses = new();
    private readonly HotWalletService _sut;

    public HotWalletServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();

        _clock.SetUtcNow(new DateTimeOffset(2026, 5, 17, 14, 0, 0, TimeSpan.Zero));

        // Seed the admin user required by ColdWalletTransfer.InitiatedByAdminId
        // FK and the AuditLog.ActorId FK (NOT NULL). The SeedConstants.SystemUserId
        // row is already present via HasData; we only need to add the test admin.
        _db.Set<User>().Add(new User
        {
            Id = AdminId,
            SteamId = "76561198000000077",
            SteamDisplayName = "T77TestAdmin",
            PreferredLanguage = "en",
            CreatedAt = _clock.GetUtcNow().UtcDateTime,
        });
        _db.SaveChanges();

        // Addresses are deployment configuration now (05 §3.3, owner decision
        // 2026-09-16), so the fixture sets a provider instead of SystemSetting rows.
        _sut = new HotWalletService(
            _db,
            _sidecar,
            _walletAddresses,
            _clock,
            NullLogger<HotWalletService>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task InitiateColdTransferAsync_ZeroOrNegativeAmount_ReturnsInvalidAmount()
    {
        var outcome = await _sut.InitiateColdTransferAsync(
            0m, StablecoinType.USDT, AdminId, CancellationToken.None);
        Assert.IsType<HotWalletColdTransferOutcome.InvalidAmount>(outcome);

        outcome = await _sut.InitiateColdTransferAsync(
            -1m, StablecoinType.USDT, AdminId, CancellationToken.None);
        Assert.IsType<HotWalletColdTransferOutcome.InvalidAmount>(outcome);

        Assert.Empty(_sidecar.Calls);
        Assert.Empty(await LoadLedgerAsync());
        Assert.Empty(await LoadAuditAsync());
    }

    [Fact]
    public async Task InitiateColdTransferAsync_OutOfScaleAmount_ReturnsInvalidAmount()
    {
        // 7 fractional digits — exceeds scale 6
        var outcome = await _sut.InitiateColdTransferAsync(
            1.0000001m, StablecoinType.USDT, AdminId, CancellationToken.None);

        Assert.IsType<HotWalletColdTransferOutcome.InvalidAmount>(outcome);
        Assert.Empty(_sidecar.Calls);
        Assert.Empty(await LoadLedgerAsync());
    }

    [Fact]
    public async Task InitiateColdTransferAsync_HotWalletUnconfigured_ReturnsHotWalletNotConfigured()
    {
        _walletAddresses.ColdWalletAddress = ColdWalletAddress;

        var outcome = await _sut.InitiateColdTransferAsync(
            100m, StablecoinType.USDT, AdminId, CancellationToken.None);

        Assert.IsType<HotWalletColdTransferOutcome.HotWalletNotConfigured>(outcome);
        Assert.Empty(_sidecar.Calls);
        Assert.Empty(await LoadLedgerAsync());
    }

    [Fact]
    public async Task InitiateColdTransferAsync_ColdWalletUnconfigured_ReturnsColdWalletNotConfigured()
    {
        _walletAddresses.HotWalletAddress = HotWalletAddress;

        var outcome = await _sut.InitiateColdTransferAsync(
            100m, StablecoinType.USDT, AdminId, CancellationToken.None);

        Assert.IsType<HotWalletColdTransferOutcome.ColdWalletNotConfigured>(outcome);
        Assert.Empty(_sidecar.Calls);
        Assert.Empty(await LoadLedgerAsync());
    }


    [Fact]
    public async Task InitiateColdTransferAsync_SidecarUnavailable_NoLedgerOrAuditWritten()
    {
        await ConfigureBothWalletsAsync();
        _sidecar.QueueUnavailable();

        var outcome = await _sut.InitiateColdTransferAsync(
            100m, StablecoinType.USDT, AdminId, CancellationToken.None);

        Assert.IsType<HotWalletColdTransferOutcome.SidecarUnavailable>(outcome);
        Assert.Empty(await LoadLedgerAsync());
        Assert.Empty(await LoadAuditAsync());
    }

    [Fact]
    public async Task InitiateColdTransferAsync_SidecarSuccess_WritesLedgerAndAudit()
    {
        await ConfigureBothWalletsAsync();
        _sidecar.QueueSuccess("0xtxhash-cold-001");

        var outcome = await _sut.InitiateColdTransferAsync(
            250.5m, StablecoinType.USDC, AdminId, CancellationToken.None);

        var success = Assert.IsType<HotWalletColdTransferOutcome.Success>(outcome);
        Assert.Equal("0xtxhash-cold-001", success.TxHash);
        Assert.Equal(250.5m, success.Amount);
        Assert.Equal(StablecoinType.USDC, success.Token);
        Assert.Equal(HotWalletAddress, success.FromAddress);
        Assert.Equal(ColdWalletAddress, success.ToAddress);

        var ledger = Assert.Single(await LoadLedgerAsync());
        Assert.Equal(250.5m, ledger.Amount);
        Assert.Equal(StablecoinType.USDC, ledger.Token);
        Assert.Equal(HotWalletAddress, ledger.FromAddress);
        Assert.Equal(ColdWalletAddress, ledger.ToAddress);
        Assert.Equal("0xtxhash-cold-001", ledger.TxHash);
        Assert.Equal(AdminId, ledger.InitiatedByAdminId);
        Assert.Equal(success.ColdTransferId, ledger.Id);

        var audit = Assert.Single(await LoadAuditAsync());
        Assert.Equal(AuditAction.COLD_WALLET_TRANSFER_INITIATED, audit.Action);
        Assert.Equal(nameof(ColdWalletTransfer), audit.EntityType);
        Assert.Equal("0xtxhash-cold-001", audit.EntityId);
        Assert.Equal(AdminId, audit.ActorId);
        Assert.Equal(ActorType.ADMIN, audit.ActorType);
        Assert.NotNull(audit.NewValue);

        // Sidecar received the correct payload.
        var call = Assert.Single(_sidecar.Calls);
        Assert.Equal(ColdWalletAddress, call.ToColdAddress);
        Assert.Equal("250.5", call.Amount);
        Assert.Equal("USDC", call.Token);
    }

    [Fact]
    public async Task InitiateColdTransferAsync_SidecarSuccessTwice_TwoLedgerRowsTwoAudits()
    {
        await ConfigureBothWalletsAsync();
        _sidecar.QueueSuccess("0xfirst");
        _sidecar.QueueSuccess("0xsecond");

        await _sut.InitiateColdTransferAsync(100m, StablecoinType.USDT, AdminId, CancellationToken.None);
        await _sut.InitiateColdTransferAsync(200m, StablecoinType.USDT, AdminId, CancellationToken.None);

        var ledger = await LoadLedgerAsync();
        Assert.Equal(2, ledger.Count);
        Assert.Contains(ledger, r => r.TxHash == "0xfirst");
        Assert.Contains(ledger, r => r.TxHash == "0xsecond");
        Assert.Equal(2, (await LoadAuditAsync()).Count);
    }

    // ─── Helpers ────────────────────────────────────────────────────────

    private async Task ConfigureBothWalletsAsync()
    {
        _walletAddresses.HotWalletAddress = HotWalletAddress;
        _walletAddresses.ColdWalletAddress = ColdWalletAddress;
    }


    private async Task<List<ColdWalletTransfer>> LoadLedgerAsync() =>
        await _db.Set<ColdWalletTransfer>().AsNoTracking().ToListAsync();

    private async Task<List<AuditLog>> LoadAuditAsync() =>
        await _db.Set<AuditLog>().AsNoTracking()
            .Where(a => a.Action == AuditAction.COLD_WALLET_TRANSFER_INITIATED)
            .ToListAsync();

    private sealed class StubSidecarClient : IBlockchainSidecarClient
    {
        public Queue<BlockchainSidecarTransferResult> Responses { get; } = new();
        public List<HotToColdTransferRequest> Calls { get; } = new();

        public Task<BlockchainSidecarDeriveResult> DeriveAddressAsync(
            int index, Guid transactionId, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<BlockchainSidecarStatus> StartMonitoringAsync(
            PaymentMonitorStartRequest request, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<BlockchainSidecarStatus> StopMonitoringAsync(
            string address, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<BlockchainSidecarStatus> StartPostCancelMonitoringAsync(
            PostCancelMonitorStartRequest request, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<BlockchainSidecarStatus> StopPostCancelMonitoringAsync(
            string address, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<BlockchainSidecarBalancesResult> GetWalletBalancesAsync(
            IReadOnlyList<string> addresses, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<BlockchainSidecarTransferResult> SendHotToColdTransferAsync(
            HotToColdTransferRequest request, CancellationToken cancellationToken)
        {
            Calls.Add(request);
            var response = Responses.Count > 0
                ? Responses.Dequeue()
                : new BlockchainSidecarTransferResult(
                    BlockchainSidecarStatus.Success, "0xstub-tx-hash");
            return Task.FromResult(response);
        }

        public void QueueSuccess(string txHash) =>
            Responses.Enqueue(new BlockchainSidecarTransferResult(
                BlockchainSidecarStatus.Success, txHash));

        public void QueueUnavailable() =>
            Responses.Enqueue(new BlockchainSidecarTransferResult(
                BlockchainSidecarStatus.Unavailable, null));
    }
}
