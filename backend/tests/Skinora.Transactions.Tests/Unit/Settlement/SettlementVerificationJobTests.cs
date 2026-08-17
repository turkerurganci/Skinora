using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Skinora.Shared.Domain;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Interfaces;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Application.Lifecycle;
using Skinora.Transactions.Application.Reputation;
using Skinora.Transactions.Application.Settlement;
using Skinora.Transactions.Application.Steam;
using Skinora.Transactions.Domain.Entities;
using Skinora.Transactions.Infrastructure.Persistence;
using Skinora.Users.Domain.Entities;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.Transactions.Tests.Unit.Settlement;

/// <summary>
/// T129 — what the job DOES with each verdict (02 §4.5.1, 03 §2.4 step 2).
/// The verdicts themselves are covered by
/// <c>SettlementVerificationServiceTests</c>; here the engine is stubbed so each
/// action can be asserted in isolation.
/// </summary>
/// <remarks>
/// The recurring theme in these assertions is that only two of the five verdicts
/// may move money, and none of the other three may be allowed to drift into
/// doing so by accident — nor, since the fix round, may a parked transaction be
/// allowed to sit with no way out at all.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class SettlementVerificationJobTests : IDisposable
{
    static SettlementVerificationJobTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        TransactionsModuleDbRegistration.RegisterTransactionsModule();
    }

    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly StubVerificationService _verification;
    private readonly StubSettlementSettings _settings;
    private readonly RecordingFlagWriter _flags;
    private readonly RecordingReputationRefresher _reputation;
    private readonly RecordingOutbox _outbox;
    private readonly FakeTimeProvider _clock;
    private readonly SettlementVerificationJob _sut;

    private Guid _sellerId;
    private Guid _buyerId;
    private Guid _adminId;

    public SettlementVerificationJobTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options);
        _db.Database.EnsureCreated();

        _verification = new StubVerificationService();
        _settings = new StubSettlementSettings();
        _flags = new RecordingFlagWriter();
        _reputation = new RecordingReputationRefresher(_db);
        _outbox = new RecordingOutbox();
        _clock = new FakeTimeProvider();
        _clock.SetUtcNow(new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero));

        _sut = new SettlementVerificationJob(
            _db, _verification, _settings, _flags, _reputation, _outbox, _clock,
            NullLogger<SettlementVerificationJob>.Instance);
    }

    // ================= Verified =================

    [Fact]
    public async Task Verified_StampsSettlementClearance()
    {
        var tx = await SeedAsync();
        _verification.Result = Verdict(SettlementVerdict.Verified, buyerHoldsItem: true);

        await _sut.ExecuteAsync();

        var persisted = await ReloadAsync(tx.Id);
        Assert.Equal(_clock.GetUtcNow().UtcDateTime, persisted.SettlementVerifiedAt);
        Assert.Null(persisted.DeliveryReversedAt);
        Assert.Equal(TransactionStatus.ITEM_DELIVERED, persisted.Status);
        Assert.Equal(_clock.GetUtcNow().UtcDateTime, persisted.SettlementCheckedAt);
        Assert.Empty(_outbox.Published);
        Assert.Empty(_flags.Calls);

        // Nothing terminal happened, so the reputation formula has no new input.
        Assert.Empty(_reputation.Calls);
    }

    // ================= Reversal, gate open =================

    [Fact]
    public async Task ReversalSignature_WithGateOpen_RefundsAndFlags()
    {
        var tx = await SeedAsync();
        _settings.ReversalAutoRefundEnabled = true;
        _verification.Result = Verdict(SettlementVerdict.ReversalSignature,
            buyerHoldsItem: false, sellerAssetReturned: true);

        await _sut.ExecuteAsync();

        var persisted = await ReloadAsync(tx.Id);
        Assert.Equal(TransactionStatus.REFUNDED, persisted.Status);
        Assert.Equal(_clock.GetUtcNow().UtcDateTime, persisted.DeliveryReversedAt);
        Assert.Null(persisted.SettlementVerifiedAt);

        // CK_Transactions_Cancel — REFUNDED carries the full attribution trail.
        Assert.Equal(CancelledByType.SELLER, persisted.CancelledBy);
        Assert.False(string.IsNullOrWhiteSpace(persisted.CancelReason));
        Assert.NotNull(persisted.CancelledAt);

        // The buyer's money travels the one audited refund pipeline (WP2).
        var refund = Assert.Single(_outbox.Published.OfType<PaymentRefundToBuyerRequestedEvent>());
        Assert.Equal(_buyerId, refund.BuyerId);

        Assert.Single(_outbox.Published.OfType<SettlementReversalDetectedEvent>());
        Assert.Single(_outbox.Published.OfType<TransactionStatusChangedEvent>());

        // 02 §4.5.1 — account-level flag on the SELLER.
        var flag = Assert.Single(_flags.Calls);
        Assert.Equal(_sellerId, flag.UserId);
        Assert.Equal(FraudFlagType.DELIVERY_REVERSED, flag.Type);
        Assert.Contains(tx.Id.ToString(), flag.Details);

        // WP15 — the transition is on the audit trail.
        Assert.True(await _db.Set<TransactionHistory>()
            .AnyAsync(h => h.TransactionId == tx.Id
                && h.NewStatus == TransactionStatus.REFUNDED));
    }

    /// <summary>
    /// 06 §3.1 counts this REFUNDED against the seller, but the rate it feeds is
    /// a denormalised column: writing the formula changed nothing until somebody
    /// asked for the recompute (validator finding B3).
    /// </summary>
    [Fact]
    public async Task ReversalSignature_WithGateOpen_RefreshesTheSellersReputation_AfterTheFlush()
    {
        await SeedAsync();
        _settings.ReversalAutoRefundEnabled = true;
        _verification.Result = Verdict(SettlementVerdict.ReversalSignature,
            buyerHoldsItem: false, sellerAssetReturned: true);

        await _sut.ExecuteAsync();

        var call = Assert.Single(_reputation.Calls);
        Assert.Equal(_sellerId, call.SellerId);
        Assert.Equal(_buyerId, call.BuyerId);

        // The cooldown query counts only the CANCELLED_* statuses; asking it to
        // run over a REFUNDED row would be two guaranteed no-op queries.
        Assert.False(call.EvaluateCooldown);

        // Ordering is the whole finding: the aggregator reads AsNoTracking, so a
        // refresh requested before the save would still see ITEM_DELIVERED and a
        // null DeliveryReversedAt, and the row would miss the denominator.
        Assert.Equal(TransactionStatus.REFUNDED, Assert.Single(_reputation.StatusInDbAtCallTime));
    }


    // ================= Reversal, gate closed =================

    [Fact]
    public async Task ReversalSignature_WithGateClosed_EscalatesWithoutMovingMoney()
    {
        var tx = await SeedAsync();
        _verification.Result = Verdict(SettlementVerdict.ReversalSignature,
            buyerHoldsItem: false, sellerAssetReturned: true);

        await _sut.ExecuteAsync();

        var persisted = await ReloadAsync(tx.Id);
        Assert.Equal(TransactionStatus.ITEM_DELIVERED, persisted.Status);
        Assert.Null(persisted.DeliveryReversedAt);
        Assert.Null(persisted.SettlementVerifiedAt);
        Assert.Equal(_clock.GetUtcNow().UtcDateTime, persisted.SettlementEscalatedAt);

        var review = Assert.Single(_outbox.Published.OfType<SettlementReviewRequiredEvent>());
        Assert.Equal(SettlementReviewReasons.ReversalGated, review.Reason);

        // The reason also lands on the row: the triage query has to be able to
        // separate the classes, and ClearForPayout has to know a departure was
        // observed here.
        Assert.Equal(SettlementReviewReasons.ReversalGated, persisted.SettlementEscalationReason);

        // Nothing that moves money or blames anybody happened.
        Assert.Empty(_outbox.Published.OfType<PaymentRefundToBuyerRequestedEvent>());
        Assert.Empty(_flags.Calls);
        Assert.Empty(_reputation.Calls);
    }

    // ================= Ambiguous =================

    [Fact]
    public async Task AmbiguousDeparture_Escalates_AndNeverPaysOrRefunds()
    {
        var tx = await SeedAsync();
        _verification.Result = Verdict(SettlementVerdict.AmbiguousDeparture,
            buyerHoldsItem: false, sellerAssetReturned: false);

        await _sut.ExecuteAsync();

        var persisted = await ReloadAsync(tx.Id);
        Assert.Null(persisted.SettlementVerifiedAt);
        Assert.Null(persisted.DeliveryReversedAt);
        Assert.NotNull(persisted.SettlementEscalatedAt);

        var review = Assert.Single(_outbox.Published.OfType<SettlementReviewRequiredEvent>());
        Assert.Equal(SettlementReviewReasons.AmbiguousDeparture, review.Reason);
    }

    // ================= Inconclusive =================

    [Fact]
    public async Task Inconclusive_BeforeThreshold_RetriesSilently()
    {
        var tx = await SeedAsync(payoutEligibleAt: _clock.GetUtcNow().UtcDateTime.AddHours(-1));
        _verification.Result = Verdict(SettlementVerdict.Inconclusive);

        await _sut.ExecuteAsync();

        var persisted = await ReloadAsync(tx.Id);
        Assert.Null(persisted.SettlementVerifiedAt);
        Assert.Null(persisted.SettlementEscalatedAt);
        // The round is recorded even when it concludes nothing — that column is
        // what keeps the batch from re-reading the same rows forever.
        Assert.Equal(_clock.GetUtcNow().UtcDateTime, persisted.SettlementCheckedAt);
        Assert.Empty(_outbox.Published);
    }

    [Fact]
    public async Task Inconclusive_PastThreshold_Escalates()
    {
        var tx = await SeedAsync(payoutEligibleAt: _clock.GetUtcNow().UtcDateTime.AddHours(-49));
        _verification.Result = Verdict(SettlementVerdict.Inconclusive);

        await _sut.ExecuteAsync();

        var persisted = await ReloadAsync(tx.Id);
        Assert.NotNull(persisted.SettlementEscalatedAt);
        var review = Assert.Single(_outbox.Published.OfType<SettlementReviewRequiredEvent>());
        Assert.Equal(SettlementReviewReasons.Unreadable, review.Reason);
    }

    [Fact]
    public async Task Escalation_IsAnnouncedOnce_ButCheckingContinues_Hourly()
    {
        var tx = await SeedAsync(payoutEligibleAt: _clock.GetUtcNow().UtcDateTime.AddHours(-49));
        _verification.Result = Verdict(SettlementVerdict.Inconclusive);

        await _sut.ExecuteAsync();
        var firstEscalation = (await ReloadAsync(tx.Id)).SettlementEscalatedAt;
        var firstCheck = (await ReloadAsync(tx.Id)).SettlementCheckedAt;
        Assert.Equal(1, _verification.CallCount);

        // Inside the throttle window: a human is already on this row, so the
        // tick must not spend two more rate-limited Steam reads on it.
        _clock.Advance(TimeSpan.FromMinutes(30));
        await _sut.ExecuteAsync();

        var throttled = await ReloadAsync(tx.Id);
        Assert.Equal(1, _verification.CallCount);
        Assert.Equal(firstCheck, throttled.SettlementCheckedAt);

        // Past it: still re-checked, because an unreadable inventory may open
        // and resolve what a human would otherwise have to.
        _clock.Advance(TimeSpan.FromMinutes(31));
        await _sut.ExecuteAsync();

        var persisted = await ReloadAsync(tx.Id);
        Assert.Equal(2, _verification.CallCount);
        Assert.Equal(_clock.GetUtcNow().UtcDateTime, persisted.SettlementCheckedAt);

        // And through all of it, the admin was told exactly once.
        Assert.Equal(firstEscalation, persisted.SettlementEscalatedAt);
        Assert.Single(_outbox.Published.OfType<SettlementReviewRequiredEvent>());
    }

    // ================= No decision input (B1) =================

    /// <summary>
    /// The buyer's inventory was private at SELLER_CONFIRMED and the delivery
    /// closed on their own confirmation, so neither the asset id nor the
    /// baseline exists — and neither is writable any more. Retrying cannot win
    /// anything, so the threshold is skipped: before the fix round this class
    /// waited out 48 hours and then sat escalated with no way to end
    /// (validator finding B1).
    /// </summary>
    [Fact]
    public async Task NoDeliveryReference_EscalatesOnTheFirstRound_WithoutWaitingTheThreshold()
    {
        // Five minutes past the window — nowhere near the 48h unreadable floor.
        var tx = await SeedAsync(payoutEligibleAt: _clock.GetUtcNow().UtcDateTime.AddMinutes(-5));
        _verification.Result = Verdict(SettlementVerdict.NoDeliveryReference);

        await _sut.ExecuteAsync();

        var persisted = await ReloadAsync(tx.Id);
        Assert.Equal(_clock.GetUtcNow().UtcDateTime, persisted.SettlementEscalatedAt);
        Assert.Equal(SettlementReviewReasons.NoDeliveryReference, persisted.SettlementEscalationReason);

        // Still no money moved in either direction — escalation is not a verdict.
        Assert.Null(persisted.SettlementVerifiedAt);
        Assert.Null(persisted.DeliveryReversedAt);
        Assert.Empty(_flags.Calls);

        var review = Assert.Single(_outbox.Published.OfType<SettlementReviewRequiredEvent>());
        Assert.Equal(SettlementReviewReasons.NoDeliveryReference, review.Reason);
    }

    // ================= Escalation stickiness (N2) =================

    /// <summary>
    /// A round that watched the item LEAVE may not be undone by a later round
    /// that reads it back. The class-count route says "item duruyor" as soon as
    /// the buyer acquires any other copy of the same skin, and before the fix
    /// round that reading stamped the clearance and released the money while an
    /// ADMIN_ESCALATION was still open, with nobody told (validator finding N2).
    /// </summary>
    [Fact]
    public async Task Escalation_ThatObservedADeparture_IsNotClearedByALaterVerifiedRound()
    {
        var tx = await SeedAsync();
        _verification.Result = Verdict(SettlementVerdict.AmbiguousDeparture,
            buyerHoldsItem: false, sellerAssetReturned: false);

        await _sut.ExecuteAsync();
        Assert.Equal(
            SettlementReviewReasons.AmbiguousDeparture,
            (await ReloadAsync(tx.Id)).SettlementEscalationReason);

        _clock.Advance(TimeSpan.FromHours(2));
        _verification.Result = Verdict(SettlementVerdict.Verified, buyerHoldsItem: true);

        await _sut.ExecuteAsync();

        var persisted = await ReloadAsync(tx.Id);
        Assert.Null(persisted.SettlementVerifiedAt);
        Assert.NotNull(persisted.SettlementEscalatedAt);
        Assert.Equal(SettlementReviewReasons.AmbiguousDeparture, persisted.SettlementEscalationReason);

        // The round still happened (queue fairness) and the admin was still told
        // exactly once.
        Assert.Equal(_clock.GetUtcNow().UtcDateTime, persisted.SettlementCheckedAt);
        Assert.Single(_outbox.Published.OfType<SettlementReviewRequiredEvent>());
    }

    /// <summary>
    /// The mirror case, and the reason stickiness is keyed on the REASON rather
    /// than on "was escalated": an escalation that observed nothing carries no
    /// finding to preserve, so an inventory that finally opens and shows the
    /// item is new information in the safe direction.
    /// </summary>
    [Fact]
    public async Task Escalation_ThatObservedNothing_IsClearedByALaterVerifiedRound()
    {
        var tx = await SeedAsync(payoutEligibleAt: _clock.GetUtcNow().UtcDateTime.AddHours(-49));
        _verification.Result = Verdict(SettlementVerdict.Inconclusive);

        await _sut.ExecuteAsync();
        Assert.Equal(
            SettlementReviewReasons.Unreadable,
            (await ReloadAsync(tx.Id)).SettlementEscalationReason);

        _clock.Advance(TimeSpan.FromHours(2));
        _verification.Result = Verdict(SettlementVerdict.Verified, buyerHoldsItem: true);

        await _sut.ExecuteAsync();

        var persisted = await ReloadAsync(tx.Id);
        Assert.Equal(_clock.GetUtcNow().UtcDateTime, persisted.SettlementVerifiedAt);
    }

    /// <summary>
    /// An inventory that opens and shows the item gone is a strictly stronger
    /// finding than "could not read", so the recorded reason is upgraded — but
    /// the admin is not told twice, because one is already looking.
    /// </summary>
    [Fact]
    public async Task Escalation_Reason_IsUpgraded_WhenALaterRoundLearnsMore()
    {
        var tx = await SeedAsync(payoutEligibleAt: _clock.GetUtcNow().UtcDateTime.AddHours(-49));
        _verification.Result = Verdict(SettlementVerdict.Inconclusive);
        await _sut.ExecuteAsync();

        var firstEscalation = (await ReloadAsync(tx.Id)).SettlementEscalatedAt;

        _clock.Advance(TimeSpan.FromHours(2));
        _verification.Result = Verdict(SettlementVerdict.AmbiguousDeparture,
            buyerHoldsItem: false, sellerAssetReturned: false);
        await _sut.ExecuteAsync();

        var persisted = await ReloadAsync(tx.Id);
        Assert.Equal(SettlementReviewReasons.AmbiguousDeparture, persisted.SettlementEscalationReason);
        Assert.Equal(firstEscalation, persisted.SettlementEscalatedAt);
        Assert.Single(_outbox.Published.OfType<SettlementReviewRequiredEvent>());
    }

    // ================= Candidate selection =================

    [Theory]
    [InlineData("hold")]
    [InlineData("dispute")]
    [InlineData("window-open")]
    [InlineData("already-verified")]
    [InlineData("already-reversed")]
    [InlineData("not-delivered")]
    public async Task IneligibleTransactions_AreNotEvenRead(string kind)
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        await SeedAsync(configure: t =>
        {
            switch (kind)
            {
                case "hold":
                    // CK_Transactions_Hold + the freeze-hold correlation
                    // (06 §3.5) — a hold is never half-recorded.
                    t.IsOnHold = true;
                    t.EmergencyHoldAt = nowUtc;
                    t.EmergencyHoldReason = "test";
                    t.EmergencyHoldByAdminId = _adminId;
                    t.TimeoutFrozenAt = nowUtc;
                    t.TimeoutFreezeReason = TimeoutFreezeReason.EMERGENCY_HOLD;
                    t.TimeoutRemainingSeconds = 60;
                    break;
                case "dispute": t.HasActiveDispute = true; break;
                case "window-open": t.PayoutEligibleAt = nowUtc.AddDays(1); break;
                case "already-verified": t.SettlementVerifiedAt = nowUtc; break;
                case "already-reversed": t.DeliveryReversedAt = nowUtc; break;
                case "not-delivered": t.Status = TransactionStatus.PAYMENT_RECEIVED; break;
            }
        });

        await _sut.ExecuteAsync();

        Assert.Equal(0, _verification.CallCount);
        Assert.Empty(_outbox.Published);
    }

    [Fact]
    public async Task VerificationThrowing_IsTreatedAsInconclusive_NotAsAFinding()
    {
        var tx = await SeedAsync();
        _verification.Throw = new HttpRequestException("sidecar down");

        await _sut.ExecuteAsync();

        var persisted = await ReloadAsync(tx.Id);
        Assert.Null(persisted.SettlementVerifiedAt);
        Assert.Null(persisted.DeliveryReversedAt);
        Assert.Equal(_clock.GetUtcNow().UtcDateTime, persisted.SettlementCheckedAt);
        Assert.Empty(_outbox.Published);
    }

    // ================= Helpers =================

    private async Task<Transaction> ReloadAsync(Guid id)
    {
        _db.ChangeTracker.Clear();
        return await _db.Set<Transaction>().FirstAsync(t => t.Id == id);
    }

    private static SettlementVerificationResult Verdict(
        SettlementVerdict verdict,
        bool? buyerHoldsItem = null,
        bool? sellerAssetReturned = null) =>
        new(verdict, buyerHoldsItem, sellerAssetReturned,
            BuyerVisibility: null, SellerVisibility: null,
            ObservedClassCount: null, ExpectedClassCount: null,
            Detail: "test");

    private async Task<Transaction> SeedAsync(
        DateTime? payoutEligibleAt = null,
        Action<Transaction>? configure = null)
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        _sellerId = Guid.NewGuid();
        _buyerId = Guid.NewGuid();
        _adminId = Guid.NewGuid();

        _db.Set<User>().AddRange(
            new User { Id = _sellerId, SteamId = "76561198000000290", SteamDisplayName = "Seller" },
            new User { Id = _buyerId, SteamId = "76561198000000291", SteamDisplayName = "Buyer" },
            new User { Id = _adminId, SteamId = "76561198000000292", SteamDisplayName = "Admin" });

        var tx = new Transaction
        {
            Id = Guid.NewGuid(),
            Status = TransactionStatus.ITEM_DELIVERED,
            SellerId = _sellerId,
            BuyerId = _buyerId,
            BuyerIdentificationMethod = BuyerIdentificationMethod.STEAM_ID,
            TargetBuyerSteamId = "76561198000000291",
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
            AcceptedAt = nowUtc.AddDays(-9),
            SellerReadyConfirmedAt = nowUtc.AddDays(-9),
            PaymentReceivedAt = nowUtc.AddDays(-9),
            ItemDeliveredAt = nowUtc.AddDays(-8),
            DeliveryVerifiedAt = nowUtc.AddDays(-8),
            DeliveryEvidence = DeliveryEvidence.BUYER_CONFIRMED,
            PayoutEligibleAt = payoutEligibleAt ?? nowUtc.AddMinutes(-5),
        };
        configure?.Invoke(tx);

        _db.Set<Transaction>().Add(tx);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return tx;
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private sealed class StubVerificationService : ISettlementVerificationService
    {
        public SettlementVerificationResult Result { get; set; } =
            new(SettlementVerdict.Inconclusive, null, null, null, null, null, null, "stub");

        public Exception? Throw { get; set; }

        public int CallCount { get; private set; }

        public Task<SettlementVerificationResult> VerifyAsync(
            Transaction transaction, CancellationToken cancellationToken)
        {
            CallCount++;
            if (Throw is not null) throw Throw;
            return Task.FromResult(Result);
        }
    }

    private sealed class StubSettlementSettings : ISettlementSettingsProvider
    {
        public int SettlementDays { get; set; } = 8;
        public int UnreadableEscalationHours { get; set; } = 48;
        public bool ReversalAutoRefundEnabled { get; set; }

        public Task<SettlementSettings> GetAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new SettlementSettings(
                SettlementDays, UnreadableEscalationHours, ReversalAutoRefundEnabled));
    }

    private sealed class RecordingFlagWriter : ITransactionFraudFlagWriter
    {
        public List<(Guid UserId, FraudFlagType Type, string Details)> Calls { get; } = [];

        public Task StagePreCreateFlagAsync(
            Guid userId, Guid transactionId, FraudFlagType type, string details,
            CancellationToken cancellationToken)
            => throw new InvalidOperationException(
                "Settlement never raises a pre-create flag — the transaction already exists.");

        public Task StageAccountFlagAsync(
            Guid userId, FraudFlagType type, string details, CancellationToken cancellationToken)
        {
            Calls.Add((userId, type, details));
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingReputationRefresher : ITransactionReputationRefresher
    {
        private readonly AppDbContext _db;

        public RecordingReputationRefresher(AppDbContext db) => _db = db;

        public List<(Guid SellerId, Guid? BuyerId, bool EvaluateCooldown)> Calls { get; } = [];

        /// <summary>
        /// The status the DATABASE held at the moment the refresh was asked for.
        /// The real aggregator reads AsNoTracking, so this is what it would have
        /// seen — the only way to prove the call sits after the flush and not
        /// before it.
        /// </summary>
        public List<TransactionStatus> StatusInDbAtCallTime { get; } = [];

        public async Task RefreshAsync(
            Guid sellerId, Guid? buyerId, bool evaluateCooldown, CancellationToken cancellationToken)
        {
            Calls.Add((sellerId, buyerId, evaluateCooldown));
            StatusInDbAtCallTime.AddRange(await _db.Set<Transaction>()
                .AsNoTracking()
                .Where(t => t.SellerId == sellerId)
                .Select(t => t.Status)
                .ToListAsync(cancellationToken));
        }
    }

    private sealed class RecordingOutbox : IOutboxService
    {
        public List<IDomainEvent> Published { get; } = [];

        public Task PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default)
        {
            Published.Add(domainEvent);
            return Task.CompletedTask;
        }
    }
}
