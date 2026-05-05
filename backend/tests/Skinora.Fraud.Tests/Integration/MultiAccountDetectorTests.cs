using System.Linq.Expressions;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Skinora.Fraud.Application.Flags;
using Skinora.Fraud.Application.MultiAccount;
using Skinora.Fraud.Domain.Entities;
using Skinora.Fraud.Infrastructure.Persistence;
using Skinora.Platform.Application.Audit;
using Skinora.Platform.Domain.Entities;
using Skinora.Platform.Infrastructure.Persistence;
using Skinora.Shared.BackgroundJobs;
using Skinora.Shared.Domain;
using Skinora.Shared.Domain.Seed;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Interfaces;
using Skinora.Shared.Persistence;
using Skinora.Shared.Tests.Integration;
using Skinora.Transactions.Application.Lifecycle;
using Skinora.Transactions.Application.Timeouts;
using Skinora.Transactions.Domain.Entities;
using Skinora.Transactions.Infrastructure.Persistence;
using Skinora.Users.Application.MultiAccount;
using Skinora.Users.Domain.Entities;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.Fraud.Tests.Integration;

/// <summary>
/// Integration coverage for <see cref="MultiAccountDetector"/> (T56 — 02 §14.3,
/// 03 §7.4). Exercises the full signal set against a real SQL Server through
/// the T54 <see cref="FraudFlagService"/> staging pipeline, so each invariant
/// (strong-vs-supporting separation, idempotency, exchange exclusion) is
/// observable end-to-end via the resulting <c>FraudFlag</c> row + audit log
/// + outbox event.
/// </summary>
public class MultiAccountDetectorTests : IntegrationTestBase
{
    static MultiAccountDetectorTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        TransactionsModuleDbRegistration.RegisterTransactionsModule();
        FraudModuleDbRegistration.RegisterFraudModule();
        PlatformModuleDbRegistration.RegisterPlatformModule();
    }

    private const string PayoutAddressA = "TXqH2JBkDgGWyCFg4GZzg8eUjG5JMZ7hPL";
    private const string PayoutAddressB = "TYabcd1234567890abcdef1234567890ab";
    private const string RefundAddressA = "TRefundAddrAAAAAAAAAAAAAAAAAAAAAAA";
    private const string RefundAddressB = "TRefundAddrBBBBBBBBBBBBBBBBBBBBBBB";
    private const string ExchangeAddress = "TExchangeKnownCustodialAddress01234";

    private FakeTimeProvider _clock = null!;
    private RecordingOutboxService _outbox = null!;

    protected override async Task SeedAsync(AppDbContext context)
    {
        _clock = new FakeTimeProvider(new DateTimeOffset(2026, 5, 5, 12, 0, 0, TimeSpan.Zero));
        _outbox = new RecordingOutboxService();
        await context.SaveChangesAsync();
    }

    // ---------- No-signal paths ----------

    [Fact]
    public async Task Returns_NoSignal_When_User_Is_Alone()
    {
        var subject = await InsertUserAsync("subject", PayoutAddressA, RefundAddressA);

        var result = await BuildDetector().EvaluateAsync(subject.Id, CancellationToken.None);

        Assert.Equal(MultiAccountEvaluationStatus.NoSignal, result.Status);
        Assert.False(await Context.Set<FraudFlag>().AnyAsync());
        Assert.Empty(_outbox.Published);
    }

    [Fact]
    public async Task Returns_NoSignal_When_Other_Account_Has_Different_Wallet()
    {
        var subject = await InsertUserAsync("subject", PayoutAddressA, RefundAddressA);
        await InsertUserAsync("other", PayoutAddressB, RefundAddressB);

        var result = await BuildDetector().EvaluateAsync(subject.Id, CancellationToken.None);

        Assert.Equal(MultiAccountEvaluationStatus.NoSignal, result.Status);
    }

    [Fact]
    public async Task Returns_NoSignal_When_User_Has_No_Wallet_Configured()
    {
        var subject = await InsertUserAsync("subject", payout: null, refund: null);
        await InsertUserAsync("other", payout: null, refund: null);

        var result = await BuildDetector().EvaluateAsync(subject.Id, CancellationToken.None);

        Assert.Equal(MultiAccountEvaluationStatus.NoSignal, result.Status);
    }

    // ---------- Strong signal paths ----------

    [Fact]
    public async Task Flags_When_Other_Account_Shares_Payout_Address()
    {
        var subject = await InsertUserAsync("subject", PayoutAddressA, RefundAddressA);
        var twin = await InsertUserAsync("twin", PayoutAddressA, RefundAddressB);

        var result = await BuildDetector().EvaluateAsync(subject.Id, CancellationToken.None);

        Assert.Equal(MultiAccountEvaluationStatus.Flagged, result.Status);
        Assert.Equal(MultiAccountMatchType.WALLET_PAYOUT, result.PrimaryMatchType);
        Assert.Equal(PayoutAddressA, result.PrimaryMatchValue);
        Assert.Equal(1, result.LinkedAccountCount);

        var flag = await Context.Set<FraudFlag>().SingleAsync();
        Assert.Equal(FraudFlagScope.ACCOUNT_LEVEL, flag.Scope);
        Assert.Equal(FraudFlagType.MULTI_ACCOUNT, flag.Type);
        Assert.Equal(ReviewStatus.PENDING, flag.Status);
        Assert.Equal(subject.Id, flag.UserId);

        var detail = JsonSerializer.Deserialize<JsonElement>(flag.Details);
        Assert.Equal("WALLET_PAYOUT", detail.GetProperty("matchType").GetString());
        Assert.Equal(PayoutAddressA, detail.GetProperty("matchValue").GetString());
        var linked = detail.GetProperty("linkedAccounts").EnumerateArray().ToArray();
        Assert.Single(linked);
        Assert.Equal(twin.SteamId, linked[0].GetProperty("steamId").GetString());

        var evt = Assert.IsType<FraudFlagCreatedEvent>(_outbox.Published.Single());
        Assert.Equal(FraudFlagType.MULTI_ACCOUNT, evt.Type);
        Assert.Equal(FraudFlagScope.ACCOUNT_LEVEL, evt.Scope);
    }

    [Fact]
    public async Task Flags_When_Other_Account_Shares_Refund_Address_Only()
    {
        var subject = await InsertUserAsync("subject", PayoutAddressA, RefundAddressA);
        var twin = await InsertUserAsync("twin", PayoutAddressB, RefundAddressA);

        var result = await BuildDetector().EvaluateAsync(subject.Id, CancellationToken.None);

        Assert.Equal(MultiAccountEvaluationStatus.Flagged, result.Status);
        Assert.Equal(MultiAccountMatchType.WALLET_REFUND, result.PrimaryMatchType);
        Assert.Equal(RefundAddressA, result.PrimaryMatchValue);

        var flag = await Context.Set<FraudFlag>().SingleAsync(f => f.UserId == subject.Id);
        var detail = JsonSerializer.Deserialize<JsonElement>(flag.Details);
        Assert.Equal("WALLET_REFUND", detail.GetProperty("matchType").GetString());
        Assert.Equal(twin.SteamId, detail.GetProperty("linkedAccounts")[0].GetProperty("steamId").GetString());
    }

    [Fact]
    public async Task Payout_Match_Wins_Over_Refund_Match()
    {
        var subject = await InsertUserAsync("subject", PayoutAddressA, RefundAddressA);
        await InsertUserAsync("twinPayout", PayoutAddressA, RefundAddressB);
        await InsertUserAsync("twinRefund", PayoutAddressB, RefundAddressA);

        var result = await BuildDetector().EvaluateAsync(subject.Id, CancellationToken.None);

        Assert.Equal(MultiAccountMatchType.WALLET_PAYOUT, result.PrimaryMatchType);
    }

    [Fact]
    public async Task Ignores_Soft_Deleted_Or_Deactivated_Other_Accounts()
    {
        var subject = await InsertUserAsync("subject", PayoutAddressA, RefundAddressA);
        await InsertUserAsync("deletedTwin", PayoutAddressA, RefundAddressB, isDeleted: true);
        await InsertUserAsync("deactivatedTwin", PayoutAddressA, RefundAddressB, isDeactivated: true);

        var result = await BuildDetector().EvaluateAsync(subject.Id, CancellationToken.None);

        Assert.Equal(MultiAccountEvaluationStatus.NoSignal, result.Status);
    }

    // ---------- Supporting signals ----------

    [Fact]
    public async Task Supporting_Only_Ip_Match_Does_Not_Flag()
    {
        var subject = await InsertUserAsync("subject", PayoutAddressA, RefundAddressA);
        var other = await InsertUserAsync("other", PayoutAddressB, RefundAddressB);

        await InsertLoginAsync(subject.Id, "203.0.113.5", deviceFingerprint: null);
        await InsertLoginAsync(other.Id, "203.0.113.5", deviceFingerprint: null);

        var result = await BuildDetector().EvaluateAsync(subject.Id, CancellationToken.None);

        Assert.Equal(MultiAccountEvaluationStatus.NoSignal, result.Status);
        Assert.False(await Context.Set<FraudFlag>().AnyAsync());
    }

    [Fact]
    public async Task Supporting_Only_Device_Fingerprint_Does_Not_Flag()
    {
        var subject = await InsertUserAsync("subject", PayoutAddressA, RefundAddressA);
        var other = await InsertUserAsync("other", PayoutAddressB, RefundAddressB);

        await InsertLoginAsync(subject.Id, "203.0.113.10", deviceFingerprint: "fp-shared");
        await InsertLoginAsync(other.Id, "203.0.113.11", deviceFingerprint: "fp-shared");

        var result = await BuildDetector().EvaluateAsync(subject.Id, CancellationToken.None);

        Assert.Equal(MultiAccountEvaluationStatus.NoSignal, result.Status);
    }

    [Fact]
    public async Task Wallet_Match_Surfaces_Ip_And_Fingerprint_Supporting_Signals()
    {
        var subject = await InsertUserAsync("subject", PayoutAddressA, RefundAddressA);
        var twin = await InsertUserAsync("twin", PayoutAddressA, RefundAddressB);
        var ipShared = await InsertUserAsync("ipShared", PayoutAddressB, RefundAddressB);
        var fpShared = await InsertUserAsync("fpShared", PayoutAddressB, RefundAddressB);

        await InsertLoginAsync(subject.Id, "198.51.100.7", "fp-subject");
        await InsertLoginAsync(ipShared.Id, "198.51.100.7", deviceFingerprint: null);
        await InsertLoginAsync(fpShared.Id, "10.0.0.1", "fp-subject");

        var result = await BuildDetector().EvaluateAsync(subject.Id, CancellationToken.None);

        Assert.Equal(MultiAccountEvaluationStatus.Flagged, result.Status);
        Assert.Equal(2, result.SupportingSignalCount);

        var flag = await Context.Set<FraudFlag>().SingleAsync(f => f.UserId == subject.Id);
        var detail = JsonSerializer.Deserialize<JsonElement>(flag.Details);
        var supporting = detail.GetProperty("supportingSignals").EnumerateArray().ToArray();
        Assert.Equal(2, supporting.Length);

        var types = supporting.Select(s => s.GetProperty("type").GetString()).ToHashSet();
        Assert.Contains("IP_ADDRESS", types);
        Assert.Contains("DEVICE_FINGERPRINT", types);

        // Linked accounts on each supporting signal point to the corresponding twin
        // — not the wallet twin, which is already in the primary linkedAccounts list.
        var ipSignal = supporting.First(s => s.GetProperty("type").GetString() == "IP_ADDRESS");
        Assert.Contains(
            ipSignal.GetProperty("linkedAccounts").EnumerateArray(),
            a => a.GetProperty("steamId").GetString() == ipShared.SteamId);

        var primaryLinked = detail.GetProperty("linkedAccounts").EnumerateArray()
            .Select(a => a.GetProperty("steamId").GetString())
            .ToArray();
        Assert.Contains(twin.SteamId, primaryLinked);
    }

    [Fact]
    public async Task Source_Address_Match_Excluded_When_In_Exchange_List()
    {
        var subject = await InsertUserAsync("subject", PayoutAddressA, RefundAddressA);
        var twin = await InsertUserAsync("twin", PayoutAddressA, RefundAddressB);
        var unrelated = await InsertUserAsync("unrelated", PayoutAddressB, RefundAddressB);

        await ConfigureExchangeAddressesAsync(ExchangeAddress);
        await InsertBuyerPaymentAsync(buyerId: subject.Id, fromAddress: ExchangeAddress);
        await InsertBuyerPaymentAsync(buyerId: unrelated.Id, fromAddress: ExchangeAddress);

        var result = await BuildDetector().EvaluateAsync(subject.Id, CancellationToken.None);

        Assert.Equal(MultiAccountEvaluationStatus.Flagged, result.Status);
        var flag = await Context.Set<FraudFlag>().SingleAsync(f => f.UserId == subject.Id);
        var detail = JsonSerializer.Deserialize<JsonElement>(flag.Details);
        var supporting = detail.GetProperty("supportingSignals").EnumerateArray().ToArray();
        Assert.DoesNotContain(supporting, s => s.GetProperty("type").GetString() == "SOURCE_ADDRESS");

        // Anchor the wallet match so the test still flags despite the exclusion.
        Assert.Equal(twin.SteamId, detail.GetProperty("linkedAccounts")[0].GetProperty("steamId").GetString());
    }

    [Fact]
    public async Task Source_Address_Match_Surfaces_When_Not_In_Exchange_List()
    {
        var subject = await InsertUserAsync("subject", PayoutAddressA, RefundAddressA);
        await InsertUserAsync("twin", PayoutAddressA, RefundAddressB);
        var sharedSource = await InsertUserAsync("sharedSource", PayoutAddressB, RefundAddressB);

        var nonExchangeSource = "TPersonalSourceAddress0123456789AB";
        await ConfigureExchangeAddressesAsync(MultiAccountDetector.ExchangeAddressesNoneMarker);
        await InsertBuyerPaymentAsync(buyerId: subject.Id, fromAddress: nonExchangeSource);
        await InsertBuyerPaymentAsync(buyerId: sharedSource.Id, fromAddress: nonExchangeSource);

        var result = await BuildDetector().EvaluateAsync(subject.Id, CancellationToken.None);

        Assert.Equal(MultiAccountEvaluationStatus.Flagged, result.Status);
        var flag = await Context.Set<FraudFlag>().SingleAsync(f => f.UserId == subject.Id);
        var detail = JsonSerializer.Deserialize<JsonElement>(flag.Details);
        var supporting = detail.GetProperty("supportingSignals").EnumerateArray().ToArray();

        var sourceSignal = supporting.SingleOrDefault(
            s => s.GetProperty("type").GetString() == "SOURCE_ADDRESS");
        Assert.NotEqual(default, sourceSignal);
        Assert.Equal(nonExchangeSource, sourceSignal.GetProperty("value").GetString());
        Assert.Contains(
            sourceSignal.GetProperty("linkedAccounts").EnumerateArray(),
            a => a.GetProperty("steamId").GetString() == sharedSource.SteamId);
    }

    // ---------- Idempotency ----------

    [Fact]
    public async Task Existing_Pending_Flag_Returns_AlreadyFlagged()
    {
        var subject = await InsertUserAsync("subject", PayoutAddressA, RefundAddressA);
        await InsertUserAsync("twin", PayoutAddressA, RefundAddressB);
        await InsertExistingFlagAsync(subject.Id, ReviewStatus.PENDING);

        var result = await BuildDetector().EvaluateAsync(subject.Id, CancellationToken.None);

        Assert.Equal(MultiAccountEvaluationStatus.AlreadyFlagged, result.Status);
        Assert.Equal(1, await Context.Set<FraudFlag>().CountAsync(f => f.UserId == subject.Id));
        Assert.Empty(_outbox.Published);
    }

    [Fact]
    public async Task Existing_Approved_Flag_Returns_AlreadyFlagged()
    {
        var subject = await InsertUserAsync("subject", PayoutAddressA, RefundAddressA);
        await InsertUserAsync("twin", PayoutAddressA, RefundAddressB);
        await InsertExistingFlagAsync(subject.Id, ReviewStatus.APPROVED);

        var result = await BuildDetector().EvaluateAsync(subject.Id, CancellationToken.None);

        Assert.Equal(MultiAccountEvaluationStatus.AlreadyFlagged, result.Status);
    }

    [Fact]
    public async Task Existing_Rejected_Flag_Does_Not_Short_Circuit()
    {
        var subject = await InsertUserAsync("subject", PayoutAddressA, RefundAddressA);
        await InsertUserAsync("twin", PayoutAddressA, RefundAddressB);
        await InsertExistingFlagAsync(subject.Id, ReviewStatus.REJECTED);

        var result = await BuildDetector().EvaluateAsync(subject.Id, CancellationToken.None);

        Assert.Equal(MultiAccountEvaluationStatus.Flagged, result.Status);
        Assert.Equal(2, await Context.Set<FraudFlag>().CountAsync(f => f.UserId == subject.Id));
    }

    [Fact]
    public async Task Existing_Soft_Deleted_Flag_Does_Not_Short_Circuit()
    {
        var subject = await InsertUserAsync("subject", PayoutAddressA, RefundAddressA);
        await InsertUserAsync("twin", PayoutAddressA, RefundAddressB);
        await InsertExistingFlagAsync(subject.Id, ReviewStatus.PENDING, isDeleted: true);

        var result = await BuildDetector().EvaluateAsync(subject.Id, CancellationToken.None);

        Assert.Equal(MultiAccountEvaluationStatus.Flagged, result.Status);
    }

    // ---------- Helpers ----------

    private MultiAccountDetector BuildDetector()
    {
        var auditLogger = new AuditLogger(Context, _clock);
        var limits = new TransactionLimitsProvider(Context);
        var scheduler = new NoopJobScheduler();
        var scheduling = new TimeoutSchedulingService(Context, scheduler, _clock);
        var freeze = new TimeoutFreezeService(Context, scheduler, scheduling, _clock);
        var flagService = new FraudFlagService(Context, auditLogger, _outbox, limits, freeze, _clock);
        return new MultiAccountDetector(Context, flagService);
    }

    private static int _userCounter = 1;

    private async Task<User> InsertUserAsync(
        string handle,
        string? payout,
        string? refund,
        bool isDeleted = false,
        bool isDeactivated = false)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            SteamId = $"7656119854000{Interlocked.Increment(ref _userCounter):D4}",
            SteamDisplayName = handle,
            DefaultPayoutAddress = payout,
            DefaultRefundAddress = refund,
            IsDeleted = isDeleted,
            DeletedAt = isDeleted ? _clock.GetUtcNow().UtcDateTime : null,
            IsDeactivated = isDeactivated,
            DeactivatedAt = isDeactivated ? _clock.GetUtcNow().UtcDateTime : null,
        };
        Context.Set<User>().Add(user);
        await Context.SaveChangesAsync();
        return user;
    }

    private async Task InsertLoginAsync(Guid userId, string ip, string? deviceFingerprint)
    {
        var login = new UserLoginLog
        {
            UserId = userId,
            IpAddress = ip,
            DeviceFingerprint = deviceFingerprint,
            UserAgent = "test/1.0",
            CreatedAt = _clock.GetUtcNow().UtcDateTime,
        };
        Context.Set<UserLoginLog>().Add(login);
        await Context.SaveChangesAsync();
    }

    private static int _hdWalletCounter = 1;

    private async Task InsertBuyerPaymentAsync(Guid buyerId, string fromAddress)
    {
        // The detector only needs the BlockchainTransaction join — give it a
        // minimal CREATED transaction with the right buyer id, a payment
        // address (CK_BlockchainTransactions_Type_BuyerPayment requires
        // PaymentAddressId NOT NULL for BUYER_PAYMENT rows) and a payment row.
        var seller = await InsertUserAsync(
            $"seller-{Guid.NewGuid():N}", PayoutAddressB, RefundAddressB);
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var tx = new Transaction
        {
            Id = Guid.NewGuid(),
            Status = TransactionStatus.CREATED,
            SellerId = seller.Id,
            BuyerId = buyerId,
            BuyerIdentificationMethod = BuyerIdentificationMethod.STEAM_ID,
            TargetBuyerSteamId = "76561198555000000",
            ItemAssetId = Guid.NewGuid().GetHashCode().ToString("X").PadLeft(8, '0') + "ma",
            ItemClassId = "class-ma",
            ItemName = "MA Test Item",
            StablecoinType = StablecoinType.USDT,
            Price = 50m,
            CommissionRate = 0.03m,
            CommissionAmount = 1.5m,
            TotalAmount = 51.5m,
            SellerPayoutAddress = PayoutAddressB,
            PaymentTimeoutMinutes = 60,
            AcceptDeadline = nowUtc + TimeSpan.FromMinutes(60),
        };
        Context.Set<Transaction>().Add(tx);

        var paymentAddress = new PaymentAddress
        {
            Id = Guid.NewGuid(),
            TransactionId = tx.Id,
            Address = $"TPay{Guid.NewGuid():N}"[..34],
            HdWalletIndex = Interlocked.Increment(ref _hdWalletCounter),
            ExpectedAmount = 51.5m,
            ExpectedToken = StablecoinType.USDT,
            MonitoringStatus = MonitoringStatus.ACTIVE,
            MonitoringExpiresAt = nowUtc.AddDays(30),
        };
        Context.Set<PaymentAddress>().Add(paymentAddress);

        var payment = new BlockchainTransaction
        {
            Id = Guid.NewGuid(),
            TransactionId = tx.Id,
            PaymentAddressId = paymentAddress.Id,
            Type = BlockchainTransactionType.BUYER_PAYMENT,
            FromAddress = fromAddress,
            ToAddress = paymentAddress.Address,
            Amount = 51.5m,
            Token = StablecoinType.USDT,
            Status = BlockchainTransactionStatus.CONFIRMED,
            ConfirmationCount = 20,
            CreatedAt = nowUtc,
            ConfirmedAt = nowUtc,
        };
        Context.Set<BlockchainTransaction>().Add(payment);
        await Context.SaveChangesAsync();
    }

    private async Task InsertExistingFlagAsync(
        Guid userId, ReviewStatus status, bool isDeleted = false)
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var flag = new FraudFlag
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TransactionId = null,
            Scope = FraudFlagScope.ACCOUNT_LEVEL,
            Type = FraudFlagType.MULTI_ACCOUNT,
            Status = status,
            Details = "{}",
            IsDeleted = isDeleted,
            DeletedAt = isDeleted ? nowUtc : null,
        };
        if (status is ReviewStatus.APPROVED or ReviewStatus.REJECTED)
        {
            flag.ReviewedAt = nowUtc;
            flag.ReviewedByAdminId = SeedConstants.SystemUserId;
        }
        Context.Set<FraudFlag>().Add(flag);
        await Context.SaveChangesAsync();
    }

    private async Task ConfigureExchangeAddressesAsync(string csv)
    {
        var existing = await Context.Set<SystemSetting>()
            .FirstOrDefaultAsync(s => s.Key == MultiAccountDetector.ExchangeAddressesSettingKey);
        if (existing is null)
        {
            existing = new SystemSetting
            {
                Id = Guid.NewGuid(),
                Key = MultiAccountDetector.ExchangeAddressesSettingKey,
                Value = csv,
                IsConfigured = true,
                DataType = "string",
                Category = "Fraud",
                Description = "test seed",
            };
            Context.Set<SystemSetting>().Add(existing);
        }
        else
        {
            existing.Value = csv;
            existing.IsConfigured = true;
        }
        await Context.SaveChangesAsync();
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

    private sealed class NoopJobScheduler : IBackgroundJobScheduler
    {
        public string Schedule<T>(Expression<Action<T>> methodCall, TimeSpan delay)
            => Guid.NewGuid().ToString("N");
        public string Enqueue<T>(Expression<Action<T>> methodCall)
            => Guid.NewGuid().ToString("N");
        public bool Delete(string jobId) => true;
        public void AddOrUpdateRecurring<T>(string jobId, Expression<Action<T>> methodCall, string cronExpression) { }
    }
}
