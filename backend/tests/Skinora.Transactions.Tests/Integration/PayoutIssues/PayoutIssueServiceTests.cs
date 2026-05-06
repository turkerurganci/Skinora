using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Skinora.Shared.Domain;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Interfaces;
using Skinora.Shared.Persistence;
using Skinora.Shared.Tests.Integration;
using Skinora.Transactions.Application.PayoutIssues;
using Skinora.Transactions.Domain.Entities;
using Skinora.Transactions.Infrastructure.Persistence;
using Skinora.Users.Domain.Entities;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.Transactions.Tests.Integration.PayoutIssues;

/// <summary>
/// End-to-end coverage for <see cref="PayoutIssueService"/> (T60 — 07 §7.11,
/// 02 §10.3, 06 §3.8a, 03 §2.4a Senaryo A). Exercises every guard listed
/// under 07 §7.11 "Hatalar" plus every verifier outcome → terminal state
/// transition, against a real SQL Server instance so the
/// <c>CK_SellerPayoutIssues_Status_Invariants</c> and
/// <c>UQ_SellerPayoutIssues_TransactionId_Active</c> constraints are
/// genuinely under test.
/// </summary>
public class PayoutIssueServiceTests : IntegrationTestBase
{
    static PayoutIssueServiceTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        TransactionsModuleDbRegistration.RegisterTransactionsModule();
    }

    private const string SellerSteam = "76561198000000601";
    private const string BuyerSteam = "76561198000000602";
    private const string AdminSteam = "76561198000000603";
    private const string StrangerSteam = "76561198000000699";
    private const string SellerWallet = "TXqH2JBkDgGWyCFg4GZzg8eUjG5JMZ7hPL";

    private FakeTimeProvider _clock = null!;
    private RecordingOutboxService _outbox = null!;
    private FakeAdminResolver _adminResolver = null!;
    private User _seller = null!;
    private User _buyer = null!;
    private User _admin = null!;
    private User _stranger = null!;

    protected override async Task SeedAsync(AppDbContext context)
    {
        _clock = new FakeTimeProvider(new DateTimeOffset(2026, 5, 6, 12, 0, 0, TimeSpan.Zero));
        _outbox = new RecordingOutboxService();

        _seller = new User
        {
            Id = Guid.NewGuid(),
            SteamId = SellerSteam,
            SteamDisplayName = "Seller",
            DefaultPayoutAddress = SellerWallet,
            MobileAuthenticatorVerified = true,
        };
        _buyer = new User
        {
            Id = Guid.NewGuid(),
            SteamId = BuyerSteam,
            SteamDisplayName = "Buyer",
            MobileAuthenticatorVerified = true,
        };
        _admin = new User
        {
            Id = Guid.NewGuid(),
            SteamId = AdminSteam,
            SteamDisplayName = "Admin",
        };
        _stranger = new User
        {
            Id = Guid.NewGuid(),
            SteamId = StrangerSteam,
            SteamDisplayName = "Stranger",
        };
        context.Set<User>().AddRange(_seller, _buyer, _admin, _stranger);
        await context.SaveChangesAsync();

        _adminResolver = new FakeAdminResolver { AdminId = _admin.Id };
    }

    // ---------- Verifier-driven state transitions ----------

    [Fact]
    public async Task Report_VerifierConfirms_TransitionsToResolved_AndEmitsResolvedEvent()
    {
        var tx = await CreateCompletedTransactionWithPayoutAsync("0xverified-hash-001");
        var verifier = new FakePayoutVerifier
        {
            Result = new PayoutVerificationResult(
                PayoutVerificationOutcome.Confirmed,
                VerifiedTxHash: "0xverified-hash-001",
                Message: "On-chain confirmation present."),
        };

        var sut = BuildSut(verifier);
        var outcome = await sut.ReportAsync(_seller.Id, tx.Id,
            new ReportPayoutIssueRequest("Ödeme cüzdanıma ulaşmadı, kontrol edin lütfen."),
            CancellationToken.None);

        Assert.Equal(ReportPayoutIssueStatus.Reported, outcome.Status);
        Assert.NotNull(outcome.Body);
        Assert.Equal(PayoutIssueStatus.RESOLVED, outcome.Body.Status);
        Assert.Contains("0xverified-hash-001", outcome.Body.Message);

        await using var read = CreateContext();
        var persisted = await read.Set<SellerPayoutIssue>().AsNoTracking()
            .FirstAsync(i => i.Id == outcome.Body.IssueId);
        Assert.Equal(PayoutIssueStatus.RESOLVED, persisted.VerificationStatus);
        Assert.Equal("0xverified-hash-001", persisted.PayoutTxHash);
        Assert.NotNull(persisted.ResolvedAt);
        Assert.Null(persisted.EscalatedToAdminId);

        var evt = Assert.Single(_outbox.Published.OfType<SellerPayoutIssueResolvedEvent>());
        Assert.Equal(persisted.Id, evt.IssueId);
        Assert.Equal(_seller.Id, evt.SellerId);
        Assert.Equal("0xverified-hash-001", evt.PayoutTxHash);
        Assert.Null(evt.ResolvedByAdminId);
    }

    [Fact]
    public async Task Report_VerifierDetectsAnomaly_TransitionsToEscalated_AndEmitsEscalatedEvent()
    {
        var tx = await CreateCompletedTransactionWithPayoutAsync("0xanomaly-002");
        var verifier = new FakePayoutVerifier
        {
            Result = new PayoutVerificationResult(
                PayoutVerificationOutcome.AnomalyDetected,
                VerifiedTxHash: null,
                Message: "Chain anomaly detected — block reorg suspected."),
        };

        var sut = BuildSut(verifier);
        var outcome = await sut.ReportAsync(_seller.Id, tx.Id,
            new ReportPayoutIssueRequest("Ödeme alındığı raporlandı ama gelmedi."),
            CancellationToken.None);

        Assert.Equal(ReportPayoutIssueStatus.Reported, outcome.Status);
        Assert.Equal(PayoutIssueStatus.ESCALATED, outcome.Body!.Status);

        await using var read = CreateContext();
        var persisted = await read.Set<SellerPayoutIssue>().AsNoTracking()
            .FirstAsync(i => i.Id == outcome.Body.IssueId);
        Assert.Equal(PayoutIssueStatus.ESCALATED, persisted.VerificationStatus);
        Assert.Equal(_admin.Id, persisted.EscalatedToAdminId);
        Assert.Null(persisted.ResolvedAt);

        var evt = Assert.Single(_outbox.Published.OfType<SellerPayoutIssueEscalatedEvent>());
        Assert.Equal(persisted.Id, evt.IssueId);
        Assert.Equal(_admin.Id, evt.EscalatedToAdminId);
        Assert.Equal("Chain anomaly detected — block reorg suspected.", evt.VerificationMessage);
    }

    [Fact]
    public async Task Report_VerifierUnableToVerify_StubProductionDefault_EscalatesToAdmin()
    {
        // Mirrors what the StubPayoutVerifier emits in production until the
        // Tron sidecar lands (T64–T69 forward devir).
        var tx = await CreateCompletedTransactionWithPayoutAsync(payoutTxHash: null);
        var sut = BuildSut(new StubPayoutVerifier());

        var outcome = await sut.ReportAsync(_seller.Id, tx.Id,
            new ReportPayoutIssueRequest("Payout hesabıma yansımadı, kontrol bekliyorum."),
            CancellationToken.None);

        Assert.Equal(ReportPayoutIssueStatus.Reported, outcome.Status);
        Assert.Equal(PayoutIssueStatus.ESCALATED, outcome.Body!.Status);

        await using var read = CreateContext();
        var persisted = await read.Set<SellerPayoutIssue>().AsNoTracking()
            .FirstAsync(i => i.Id == outcome.Body.IssueId);
        Assert.Equal(_admin.Id, persisted.EscalatedToAdminId);

        Assert.Single(_outbox.Published.OfType<SellerPayoutIssueEscalatedEvent>());
    }

    [Fact]
    public async Task Report_VerifierStillPending_TransitionsToRetryScheduled_AndEmitsReportedEvent()
    {
        var tx = await CreateCompletedTransactionWithPayoutAsync("0xpending-003");
        var verifier = new FakePayoutVerifier
        {
            Result = new PayoutVerificationResult(
                PayoutVerificationOutcome.StillPending,
                VerifiedTxHash: null,
                Message: "Broadcast healthy but only 3/20 confirmations."),
        };

        var sut = BuildSut(verifier);
        var outcome = await sut.ReportAsync(_seller.Id, tx.Id,
            new ReportPayoutIssueRequest("Ödeme bekliyor görünüyor, kontrol gerek."),
            CancellationToken.None);

        Assert.Equal(ReportPayoutIssueStatus.Reported, outcome.Status);
        Assert.Equal(PayoutIssueStatus.RETRY_SCHEDULED, outcome.Body!.Status);

        await using var read = CreateContext();
        var persisted = await read.Set<SellerPayoutIssue>().AsNoTracking()
            .FirstAsync(i => i.Id == outcome.Body.IssueId);
        Assert.Equal(PayoutIssueStatus.RETRY_SCHEDULED, persisted.VerificationStatus);
        Assert.Equal(1, persisted.RetryCount);
        Assert.Null(persisted.EscalatedToAdminId);
        Assert.Null(persisted.ResolvedAt);

        var evt = Assert.Single(_outbox.Published.OfType<SellerPayoutIssueReportedEvent>());
        Assert.Equal(persisted.Id, evt.IssueId);
    }

    [Fact]
    public async Task Report_NoAdminAvailable_AndAnomalyDetected_Throws()
    {
        // 06 §3.8a CK requires EscalatedToAdminId NOT NULL when ESCALATED;
        // the service signals the unrecoverable config error rather than
        // committing an invalid row.
        var tx = await CreateCompletedTransactionWithPayoutAsync("0xanomaly-004");
        _adminResolver.AdminId = null;
        var verifier = new FakePayoutVerifier
        {
            Result = new PayoutVerificationResult(
                PayoutVerificationOutcome.AnomalyDetected,
                VerifiedTxHash: null,
                Message: "Anomaly."),
        };

        var sut = BuildSut(verifier);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.ReportAsync(_seller.Id, tx.Id,
                new ReportPayoutIssueRequest("Yeterince uzun bir açıklama metni."),
                CancellationToken.None));
    }

    // ---------- Guards (07 §7.11 Hatalar) ----------

    [Fact]
    public async Task Report_TransactionNotFound_Returns_NotFound()
    {
        var sut = BuildSut(new StubPayoutVerifier());
        var outcome = await sut.ReportAsync(_seller.Id, Guid.NewGuid(),
            new ReportPayoutIssueRequest("Yeterince uzun bir açıklama."),
            CancellationToken.None);

        Assert.Equal(ReportPayoutIssueStatus.NotFound, outcome.Status);
        Assert.Equal(PayoutIssueErrorCodes.TransactionNotFound, outcome.ErrorCode);
    }

    [Fact]
    public async Task Report_NonSeller_Returns_NotSeller()
    {
        var tx = await CreateCompletedTransactionWithPayoutAsync("0xhash");
        var sut = BuildSut(new StubPayoutVerifier());

        var outcome = await sut.ReportAsync(_stranger.Id, tx.Id,
            new ReportPayoutIssueRequest("Yeterince uzun bir açıklama."),
            CancellationToken.None);

        Assert.Equal(ReportPayoutIssueStatus.NotSeller, outcome.Status);
        Assert.Equal(PayoutIssueErrorCodes.NotSeller, outcome.ErrorCode);
    }

    [Fact]
    public async Task Report_BuyerCallsAsSeller_Returns_NotSeller()
    {
        // Even the buyer (the other party on the transaction) cannot report
        // a payout issue per 02 §10.3.
        var tx = await CreateCompletedTransactionWithPayoutAsync("0xhash");
        var sut = BuildSut(new StubPayoutVerifier());

        var outcome = await sut.ReportAsync(_buyer.Id, tx.Id,
            new ReportPayoutIssueRequest("Yeterince uzun bir açıklama."),
            CancellationToken.None);

        Assert.Equal(ReportPayoutIssueStatus.NotSeller, outcome.Status);
    }

    [Fact]
    public async Task Report_TransactionNotCompleted_Returns_TransactionNotCompleted()
    {
        // 07 §7.11: payout-issue is only valid on COMPLETED transactions.
        var tx = await CreateTransactionAsync(TransactionStatus.ITEM_DELIVERED);
        var sut = BuildSut(new StubPayoutVerifier());

        var outcome = await sut.ReportAsync(_seller.Id, tx.Id,
            new ReportPayoutIssueRequest("Yeterince uzun bir açıklama."),
            CancellationToken.None);

        Assert.Equal(ReportPayoutIssueStatus.TransactionNotCompleted, outcome.Status);
        Assert.Equal(PayoutIssueErrorCodes.TransactionNotCompleted, outcome.ErrorCode);
    }

    [Fact]
    public async Task Report_ActiveIssueAlreadyExists_Returns_IssueAlreadyReported()
    {
        // 06 §3.8a: filtered UQ blocks a second active row. The defensive
        // pre-check should surface ISSUE_ALREADY_REPORTED before the DB
        // raises a generic conflict.
        var tx = await CreateCompletedTransactionWithPayoutAsync("0xhash");

        // First report → stub leaves issue at ESCALATED (still "active" because
        // VerificationStatus != RESOLVED).
        var sut = BuildSut(new StubPayoutVerifier());
        var first = await sut.ReportAsync(_seller.Id, tx.Id,
            new ReportPayoutIssueRequest("İlk şikayet, doğrulama bekliyor."),
            CancellationToken.None);
        Assert.Equal(ReportPayoutIssueStatus.Reported, first.Status);
        Assert.Equal(PayoutIssueStatus.ESCALATED, first.Body!.Status);

        // Second report on the same transaction → blocked.
        var second = await sut.ReportAsync(_seller.Id, tx.Id,
            new ReportPayoutIssueRequest("İkinci şikayet aynı işlem için."),
            CancellationToken.None);

        Assert.Equal(ReportPayoutIssueStatus.IssueAlreadyReported, second.Status);
        Assert.Equal(PayoutIssueErrorCodes.IssueAlreadyReported, second.ErrorCode);
    }

    [Fact]
    public async Task Report_ReopenAfterResolved_Allowed()
    {
        // Once the previous issue is RESOLVED the active filter excludes it,
        // so a fresh report on the same transaction must pass.
        var tx = await CreateCompletedTransactionWithPayoutAsync("0xresolved-hash");
        var verifier = new FakePayoutVerifier
        {
            Result = new PayoutVerificationResult(
                PayoutVerificationOutcome.Confirmed,
                VerifiedTxHash: "0xresolved-hash",
                Message: "OK"),
        };

        var sut = BuildSut(verifier);
        var first = await sut.ReportAsync(_seller.Id, tx.Id,
            new ReportPayoutIssueRequest("İlk şikayet doğrulanacak."),
            CancellationToken.None);
        Assert.Equal(PayoutIssueStatus.RESOLVED, first.Body!.Status);

        var second = await sut.ReportAsync(_seller.Id, tx.Id,
            new ReportPayoutIssueRequest("Yeni şikayet — eski çözüldü."),
            CancellationToken.None);
        Assert.Equal(ReportPayoutIssueStatus.Reported, second.Status);
        Assert.Equal(PayoutIssueStatus.RESOLVED, second.Body!.Status);
        Assert.NotEqual(first.Body.IssueId, second.Body.IssueId);
    }

    [Fact]
    public async Task Report_DetailTooShort_Returns_ValidationFailed()
    {
        var tx = await CreateCompletedTransactionWithPayoutAsync("0xhash");
        var sut = BuildSut(new StubPayoutVerifier());

        var outcome = await sut.ReportAsync(_seller.Id, tx.Id,
            new ReportPayoutIssueRequest("kısa"),
            CancellationToken.None);

        Assert.Equal(ReportPayoutIssueStatus.ValidationFailed, outcome.Status);
        Assert.Equal(PayoutIssueErrorCodes.ValidationError, outcome.ErrorCode);
    }

    [Fact]
    public async Task Report_DetailWhitespaceOnly_Returns_ValidationFailed()
    {
        // After Trim, " "*15 collapses to "" — below the 10-char floor.
        var tx = await CreateCompletedTransactionWithPayoutAsync("0xhash");
        var sut = BuildSut(new StubPayoutVerifier());

        var outcome = await sut.ReportAsync(_seller.Id, tx.Id,
            new ReportPayoutIssueRequest(new string(' ', 15)),
            CancellationToken.None);

        Assert.Equal(ReportPayoutIssueStatus.ValidationFailed, outcome.Status);
    }

    // ---------- Test helpers ----------

    private PayoutIssueService BuildSut(IPayoutVerifier verifier)
    {
        return new PayoutIssueService(
            db: Context,
            outbox: _outbox,
            verifier: verifier,
            adminResolver: _adminResolver,
            clock: _clock);
    }

    private async Task<Transaction> CreateCompletedTransactionWithPayoutAsync(string? payoutTxHash)
    {
        var tx = await CreateTransactionAsync(TransactionStatus.COMPLETED);
        if (payoutTxHash is not null)
        {
            var now = _clock.GetUtcNow().UtcDateTime;
            Context.Set<BlockchainTransaction>().Add(new BlockchainTransaction
            {
                Id = Guid.NewGuid(),
                TransactionId = tx.Id,
                Type = BlockchainTransactionType.SELLER_PAYOUT,
                FromAddress = "TPlatformPayout000000000000000000",
                ToAddress = SellerWallet,
                Amount = tx.Price,
                Token = tx.StablecoinType,
                Status = BlockchainTransactionStatus.CONFIRMED,
                ConfirmationCount = 20,
                ConfirmedAt = now,
                TxHash = payoutTxHash,
                CreatedAt = now,
            });
            await Context.SaveChangesAsync();
        }
        return tx;
    }

    private async Task<Transaction> CreateTransactionAsync(TransactionStatus status)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var tx = new Transaction
        {
            Id = Guid.NewGuid(),
            Status = status,
            SellerId = _seller.Id,
            BuyerId = _buyer.Id,
            BuyerIdentificationMethod = BuyerIdentificationMethod.STEAM_ID,
            TargetBuyerSteamId = BuyerSteam,
            ItemAssetId = "asset-1",
            ItemClassId = "CLASS-AK47-REDLINE",
            ItemName = "AK-47 | Redline",
            StablecoinType = StablecoinType.USDT,
            Price = 50.00m,
            CommissionRate = 0.03m,
            CommissionAmount = 1.50m,
            TotalAmount = 51.50m,
            SellerPayoutAddress = SellerWallet,
            PaymentTimeoutMinutes = 60,
            AcceptedAt = now.AddMinutes(-30),
        };
        Context.Set<Transaction>().Add(tx);
        await Context.SaveChangesAsync();
        return tx;
    }

    private sealed class FakePayoutVerifier : IPayoutVerifier
    {
        public PayoutVerificationResult Result { get; set; } = new(
            PayoutVerificationOutcome.UnableToVerify, null, "default");

        public Task<PayoutVerificationResult> VerifyAsync(
            Guid transactionId,
            string? expectedPayoutTxHash,
            CancellationToken cancellationToken)
            => Task.FromResult(Result);
    }

    private sealed class FakeAdminResolver : IPayoutEscalationAdminResolver
    {
        public Guid? AdminId { get; set; }

        public Task<Guid?> ResolveAdminUserIdAsync(CancellationToken cancellationToken)
            => Task.FromResult(AdminId);
    }

    private sealed class RecordingOutboxService : IOutboxService
    {
        public List<IDomainEvent> Published { get; } = [];

        public Task PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default)
        {
            Published.Add(domainEvent);
            return Task.CompletedTask;
        }
    }
}
