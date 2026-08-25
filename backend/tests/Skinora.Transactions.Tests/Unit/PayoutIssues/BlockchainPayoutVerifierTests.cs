using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Application.PayoutIssues;
using Skinora.Transactions.Application.Transfers;
using Skinora.Transactions.Domain.Entities;
using Skinora.Transactions.Infrastructure.Persistence;
using Skinora.Users.Domain.Entities;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.Transactions.Tests.Unit.PayoutIssues;

/// <summary>
/// Backlog <c>StubPayoutVerifier</c> — the real verifier that replaces the
/// always-escalate stub.
/// </summary>
/// <remarks>
/// The load-bearing assertion in this file is not that a confirmed payout
/// resolves; it is that <b>nothing else does</b>. The stub's one virtue was
/// that every seller complaint reached a human, and automating the answer is
/// only safe if each non-confirmed shape is pinned to the outcome that still
/// gets there. So the cases below are written as a partition of the input
/// space: confirmed, failed, missing, un-broadcast, hash mismatch, and each
/// sidecar verdict.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class BlockchainPayoutVerifierTests : IDisposable
{
    static BlockchainPayoutVerifierTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        TransactionsModuleDbRegistration.RegisterTransactionsModule();
    }

    private const string PayoutHash = "0xpayout-hash";

    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly StubTransferClient _client = new();
    private readonly BlockchainPayoutVerifier _sut;

    private Guid _transactionId;

    public BlockchainPayoutVerifierTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options);
        _db.Database.EnsureCreated();

        _sut = new BlockchainPayoutVerifier(
            _db, _client, NullLogger<BlockchainPayoutVerifier>.Instance);
    }

    [Fact]
    public async Task ConfirmedRow_ResolvesWithoutAskingTheSidecar()
    {
        await SeedTransactionAsync();
        await SeedPayoutAsync(BlockchainTransactionStatus.CONFIRMED, PayoutHash);

        var result = await _sut.VerifyAsync(_transactionId, PayoutHash, default);

        Assert.Equal(PayoutVerificationOutcome.Confirmed, result.Outcome);
        Assert.Equal(PayoutHash, result.VerifiedTxHash);
        // A CONFIRMED row already means the sidecar reported ≥20 blocks (05
        // §3.3). Re-asking could only downgrade a settled answer to
        // UnableToVerify when the sidecar happens to be down.
        Assert.Empty(_client.Asked);
    }

    [Fact]
    public async Task NoPayoutRow_IsAnAnomaly_NotAResolution()
    {
        await SeedTransactionAsync();

        var result = await _sut.VerifyAsync(_transactionId, null, default);

        // A COMPLETED sale with no payout record means the seller's complaint
        // is CORRECT. Auto-resolving here would close the one case that most
        // needs a human.
        Assert.Equal(PayoutVerificationOutcome.AnomalyDetected, result.Outcome);
        Assert.Null(result.VerifiedTxHash);
        Assert.Empty(_client.Asked);
    }

    [Fact]
    public async Task FailedRow_IsAnAnomaly()
    {
        await SeedTransactionAsync();
        await SeedPayoutAsync(BlockchainTransactionStatus.FAILED, PayoutHash);

        var result = await _sut.VerifyAsync(_transactionId, PayoutHash, default);

        Assert.Equal(PayoutVerificationOutcome.AnomalyDetected, result.Outcome);
    }

    [Fact]
    public async Task ConfirmedRowWithNullHash_IsAnAnomaly()
    {
        await SeedTransactionAsync();
        await SeedPayoutAsync(BlockchainTransactionStatus.CONFIRMED, txHash: null);

        var result = await _sut.VerifyAsync(_transactionId, null, default);

        // Resolving would tell the seller "verified on chain" while pointing at
        // no transaction anyone could look up.
        Assert.Equal(PayoutVerificationOutcome.AnomalyDetected, result.Outcome);
    }

    [Fact]
    public async Task NeverBroadcast_IsStillPending()
    {
        await SeedTransactionAsync();
        await SeedPayoutAsync(BlockchainTransactionStatus.PENDING, txHash: null);

        var result = await _sut.VerifyAsync(_transactionId, null, default);

        Assert.Equal(PayoutVerificationOutcome.StillPending, result.Outcome);
        Assert.Empty(_client.Asked);
    }

    [Fact]
    public async Task HashMismatch_IsAnAnomaly()
    {
        await SeedTransactionAsync();
        await SeedPayoutAsync(BlockchainTransactionStatus.CONFIRMED, PayoutHash);

        var result = await _sut.VerifyAsync(_transactionId, "0xsomething-else", default);

        // The caller reads the hash off this same row today, so a disagreement
        // means the record moved underneath us — no automatic answer is safe.
        Assert.Equal(PayoutVerificationOutcome.AnomalyDetected, result.Outcome);
        Assert.Empty(_client.Asked);
    }

    [Theory]
    [InlineData(TransferStatusOutcome.Confirmed, PayoutVerificationOutcome.Confirmed)]
    [InlineData(TransferStatusOutcome.Failed, PayoutVerificationOutcome.AnomalyDetected)]
    [InlineData(TransferStatusOutcome.Pending, PayoutVerificationOutcome.StillPending)]
    [InlineData(TransferStatusOutcome.Unavailable, PayoutVerificationOutcome.UnableToVerify)]
    public async Task DetectedRow_MapsEachSidecarVerdict(
        TransferStatusOutcome sidecar, PayoutVerificationOutcome expected)
    {
        await SeedTransactionAsync();
        await SeedPayoutAsync(BlockchainTransactionStatus.DETECTED, PayoutHash);
        _client.Outcome = sidecar;

        var result = await _sut.VerifyAsync(_transactionId, PayoutHash, default);

        Assert.Equal(expected, result.Outcome);
        Assert.Equal(PayoutHash, Assert.Single(_client.Asked));
        // Only the confirmed branch may hand back a hash — the service stamps
        // SellerPayoutIssue.PayoutTxHash from it.
        if (expected == PayoutVerificationOutcome.Confirmed)
            Assert.Equal(PayoutHash, result.VerifiedTxHash);
        else
            Assert.Null(result.VerifiedTxHash);
    }

    [Fact]
    public async Task ASecondPayoutRow_IsRejectedByTheDatabase()
    {
        await SeedTransactionAsync();
        await SeedPayoutAsync(BlockchainTransactionStatus.FAILED, "0xold");

        // UQ_BlockchainTransactions_SellerPayout_TransactionId (WP2 money
        // safety) permits at most ONE SELLER_PAYOUT row per transaction. This
        // test exists to record WHY the verifier's OrderByDescending(CreatedAt)
        // can never actually choose: it is defence copied from
        // PayoutIssueService, not a live "pick the retry" rule. Anyone reading
        // that ordering later would otherwise reasonably assume retried payouts
        // stack up as extra rows — they do not; a retry reuses this one.
        await Assert.ThrowsAsync<DbUpdateException>(() =>
            SeedPayoutAsync(BlockchainTransactionStatus.CONFIRMED, "0xnew"));
    }

    [Fact]
    public async Task IgnoresRowsOfOtherTypes()
    {
        await SeedTransactionAsync();
        await SeedPayoutAsync(
            BlockchainTransactionStatus.CONFIRMED, "0xrefund",
            type: BlockchainTransactionType.BUYER_REFUND);

        var result = await _sut.VerifyAsync(_transactionId, null, default);

        // A confirmed refund is not a confirmed payout; conflating them would
        // tell a seller their money arrived because the buyer's did.
        Assert.Equal(PayoutVerificationOutcome.AnomalyDetected, result.Outcome);
    }

    private async Task SeedTransactionAsync()
    {
        var sellerId = Guid.NewGuid();
        var buyerId = Guid.NewGuid();
        _db.Set<User>().AddRange(
            new User { Id = sellerId, SteamId = "76561198000000390", SteamDisplayName = "Seller" },
            new User { Id = buyerId, SteamId = "76561198000000391", SteamDisplayName = "Buyer" });

        _transactionId = Guid.NewGuid();
        _db.Set<Transaction>().Add(new Transaction
        {
            Id = _transactionId,
            Status = TransactionStatus.COMPLETED,
            SellerId = sellerId,
            BuyerId = buyerId,
            BuyerIdentificationMethod = BuyerIdentificationMethod.STEAM_ID,
            TargetBuyerSteamId = "76561198000000391",
            BuyerRefundAddress = "TBuyerRefund000000000000000000000000",
            BuyerTradeUrl = "https://steamcommunity.com/tradeoffer/new/?partner=1&token=abc",
            ItemAssetId = "27348562891",
            ItemClassId = "310776959",
            ItemName = "AK-47 | Redline",
            StablecoinType = StablecoinType.USDT,
            Price = 100m,
            CommissionRate = 0.02m,
            CommissionAmount = 2m,
            TotalAmount = 102m,
            SellerPayoutAddress = "TSellerPayout00000000000000000000000",
            PaymentTimeoutMinutes = 1440,
        });
        await _db.SaveChangesAsync();
    }

    private async Task SeedPayoutAsync(
        BlockchainTransactionStatus status,
        string? txHash,
        BlockchainTransactionType type = BlockchainTransactionType.SELLER_PAYOUT)
    {
        var created = DateTime.UtcNow;

        // 06 §3.8 status-dependent CHECK constraints: CONFIRMED needs
        // ConfirmationCount >= 20 AND a ConfirmedAt; DETECTED needs exactly 0;
        // FAILED must leave ConfirmedAt null. Seeding a shape the database
        // would reject would make these tests assert behaviour production can
        // never reach.
        var confirmed = status == BlockchainTransactionStatus.CONFIRMED;

        _db.Set<BlockchainTransaction>().Add(new BlockchainTransaction
        {
            Id = Guid.NewGuid(),
            TransactionId = _transactionId,
            Type = type,
            TxHash = txHash,
            FromAddress = "THotWallet0000000000000000000000000",
            ToAddress = "TSellerPayout00000000000000000000000",
            Amount = 100m,
            Token = StablecoinType.USDT,
            Status = status,
            ConfirmationCount = confirmed ? 20 : 0,
            ConfirmedAt = confirmed ? created : null,
            CreatedAt = created,
        });
        await _db.SaveChangesAsync();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private sealed class StubTransferClient : IBlockchainTransferClient
    {
        public List<string> Asked { get; } = [];

        public TransferStatusOutcome Outcome { get; set; } = TransferStatusOutcome.Pending;

        public Task<TransferBroadcastResult> BroadcastAsync(
            TransferBroadcastRequest request, CancellationToken cancellationToken)
            => throw new InvalidOperationException(
                "The verifier must never broadcast — it only reads.");

        public Task<TransferStatusResult> GetStatusAsync(
            string txHash, CancellationToken cancellationToken)
        {
            Asked.Add(txHash);
            return Task.FromResult(new TransferStatusResult(Outcome, null, null, null, null));
        }
    }
}
