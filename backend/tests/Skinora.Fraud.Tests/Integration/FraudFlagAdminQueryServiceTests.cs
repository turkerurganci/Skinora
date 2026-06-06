using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Skinora.Fraud.Application.Flags;
using Skinora.Fraud.Domain.Entities;
using Skinora.Fraud.Infrastructure.Persistence;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Shared.Tests.Integration;
using Skinora.Transactions.Domain.Entities;
using Skinora.Transactions.Infrastructure.Persistence;
using Skinora.Users.Application.Reputation;
using Skinora.Users.Domain.Entities;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.Fraud.Tests.Integration;

/// <summary>
/// Integration coverage for the AD2 / AD3 read paths owned by
/// <see cref="FraudFlagAdminQueryService"/>. Verifies the contract
/// surfaced by 07 §9.2 (page envelope + <c>pendingCount</c> badge),
/// 07 §9.3 (type-specific <c>flagDetail</c>) and the
/// <c>historicalTransactionCount</c> rollup.
/// </summary>
public class FraudFlagAdminQueryServiceTests : IntegrationTestBase
{
    static FraudFlagAdminQueryServiceTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        TransactionsModuleDbRegistration.RegisterTransactionsModule();
        FraudModuleDbRegistration.RegisterFraudModule();
    }

    private FakeTimeProvider _clock = null!;
    private User _seller = null!;
    private User _buyer = null!;

    protected override async Task SeedAsync(AppDbContext context)
    {
        _clock = new FakeTimeProvider(new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero));

        _seller = new User
        {
            Id = Guid.NewGuid(),
            SteamId = "76561198541000001",
            SteamDisplayName = "Seller",
            SteamAvatarUrl = "https://avatars.example/s.png",
        };
        _buyer = new User
        {
            Id = Guid.NewGuid(),
            SteamId = "76561198541000002",
            SteamDisplayName = "Buyer",
        };
        context.Set<User>().AddRange(_seller, _buyer);
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task ListAsync_Returns_Newest_First_With_PendingCount()
    {
        // Three flags: 2 pending + 1 reviewed.
        await SeedFlagAsync(scope: FraudFlagScope.ACCOUNT_LEVEL,
            type: FraudFlagType.MULTI_ACCOUNT, status: ReviewStatus.PENDING);
        await SeedFlagAsync(scope: FraudFlagScope.ACCOUNT_LEVEL,
            type: FraudFlagType.ABNORMAL_BEHAVIOR, status: ReviewStatus.PENDING);
        await SeedFlagAsync(scope: FraudFlagScope.ACCOUNT_LEVEL,
            type: FraudFlagType.MULTI_ACCOUNT, status: ReviewStatus.APPROVED,
            adminReviewed: true);

        var sut = BuildSut();

        var result = await sut.ListAsync(
            new FraudFlagListQuery(null, null, null, null, null, null, null, Page: 1, PageSize: 20),
            CancellationToken.None);

        Assert.Equal(3, result.TotalCount);
        Assert.Equal(2, result.PendingCount);
        Assert.Equal(3, result.Items.Count);
        // Default sort: CreatedAt desc — newest item is first.
        Assert.True(result.Items[0].CreatedAt >= result.Items[1].CreatedAt);
        Assert.True(result.Items[1].CreatedAt >= result.Items[2].CreatedAt);
    }

    [Fact]
    public async Task ListAsync_Filters_By_Type_And_ReviewStatus()
    {
        await SeedFlagAsync(scope: FraudFlagScope.ACCOUNT_LEVEL,
            type: FraudFlagType.MULTI_ACCOUNT, status: ReviewStatus.PENDING);
        await SeedFlagAsync(scope: FraudFlagScope.ACCOUNT_LEVEL,
            type: FraudFlagType.ABNORMAL_BEHAVIOR, status: ReviewStatus.PENDING);

        var sut = BuildSut();

        var byType = await sut.ListAsync(
            new FraudFlagListQuery(null, FraudFlagType.MULTI_ACCOUNT, null, null, null, null, null, 1, 20),
            CancellationToken.None);
        Assert.Single(byType.Items);
        Assert.Equal(FraudFlagType.MULTI_ACCOUNT, byType.Items[0].Type);

        var byStatus = await sut.ListAsync(
            new FraudFlagListQuery(null, null, ReviewStatus.PENDING, null, null, null, null, 1, 20),
            CancellationToken.None);
        Assert.Equal(2, byStatus.Items.Count);
    }

    [Fact]
    public async Task ListAsync_Filters_By_Scope()
    {
        // One account-level flag + one transaction-level flag.
        await SeedFlagAsync(scope: FraudFlagScope.ACCOUNT_LEVEL,
            type: FraudFlagType.MULTI_ACCOUNT, status: ReviewStatus.PENDING);
        var tx = await SeedTransactionAsync(_seller.Id, _buyer.Id, TransactionStatus.FLAGGED);
        await SeedFlagAsync(scope: FraudFlagScope.TRANSACTION_PRE_CREATE,
            type: FraudFlagType.PRICE_DEVIATION, status: ReviewStatus.PENDING,
            transactionId: tx.Id);

        var sut = BuildSut();

        var accountFlags = await sut.ListAsync(
            new FraudFlagListQuery(FraudFlagScope.ACCOUNT_LEVEL, null, null, null, null, null, null, 1, 20),
            CancellationToken.None);
        Assert.Single(accountFlags.Items);
        Assert.Equal(FraudFlagScope.ACCOUNT_LEVEL, accountFlags.Items[0].Scope);

        var txFlags = await sut.ListAsync(
            new FraudFlagListQuery(FraudFlagScope.TRANSACTION_PRE_CREATE, null, null, null, null, null, null, 1, 20),
            CancellationToken.None);
        Assert.Single(txFlags.Items);
        Assert.Equal(FraudFlagScope.TRANSACTION_PRE_CREATE, txFlags.Items[0].Scope);
    }

    [Fact]
    public async Task GetDetailAsync_Returns_Null_For_Unknown_Id()
    {
        var sut = BuildSut();
        var result = await sut.GetDetailAsync(Guid.NewGuid(), CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task GetDetailAsync_Returns_PriceDeviation_FlagDetail_AndCounts()
    {
        var tx = await SeedTransactionAsync(_seller.Id, _buyer.Id, TransactionStatus.FLAGGED);
        var details = JsonSerializer.Serialize(new
        {
            inputPrice = 120m,
            marketPrice = 50m,
            deviationPercent = 140m,
        });
        var flag = await SeedFlagAsync(
            scope: FraudFlagScope.TRANSACTION_PRE_CREATE,
            type: FraudFlagType.PRICE_DEVIATION,
            status: ReviewStatus.PENDING,
            transactionId: tx.Id,
            details: details);

        // Two completed historical transactions for the seller.
        await SeedTransactionAsync(_seller.Id, _buyer.Id, TransactionStatus.COMPLETED);
        await SeedTransactionAsync(_seller.Id, _buyer.Id, TransactionStatus.COMPLETED);

        var sut = BuildSut();
        var detail = await sut.GetDetailAsync(flag.Id, CancellationToken.None);

        Assert.NotNull(detail);
        Assert.Equal(flag.Id, detail!.Id);
        Assert.Equal(_seller.Id, detail.UserId);
        Assert.Equal(FraudFlagScope.TRANSACTION_PRE_CREATE, detail.Scope);
        Assert.NotNull(detail.Transaction);
        Assert.Equal(tx.Id, detail.Transaction!.Id);
        Assert.NotNull(detail.Seller);
        Assert.Equal("Seller", detail.Seller!.DisplayName);
        Assert.NotNull(detail.Buyer);
        Assert.Equal("Buyer", detail.Buyer!.DisplayName);
        Assert.Equal(2, detail.HistoricalTransactionCount);

        // 07 §9.3 seller block — extended trust-signal fields. Test users were
        // seeded with default User.CompletedTransactionCount = 0 and a fresh
        // CreatedAt (now), so account age renders as "0 gün" and the reputation
        // calculator returns null (T43 thresholds not met). The fields are
        // present and shaped correctly.
        Assert.Equal(0, detail.Seller.CompletedTransactionCount);
        Assert.Null(detail.Seller.ReputationScore);
        Assert.False(string.IsNullOrEmpty(detail.Seller.AccountAge));

        var payload = Assert.IsType<PriceDeviationFlagDetail>(detail.FlagDetail);
        Assert.Equal(120m, payload.InputPrice);
        Assert.Equal(50m, payload.MarketPrice);
        Assert.Equal(140m, payload.DeviationPercent);
    }

    [Fact]
    public async Task GetDetailAsync_Falls_Back_To_Raw_When_Details_Json_Is_Malformed()
    {
        var tx = await SeedTransactionAsync(_seller.Id, _buyer.Id, TransactionStatus.FLAGGED);
        var flag = await SeedFlagAsync(
            scope: FraudFlagScope.TRANSACTION_PRE_CREATE,
            type: FraudFlagType.PRICE_DEVIATION,
            status: ReviewStatus.PENDING,
            transactionId: tx.Id,
            details: "this is not json");

        var sut = BuildSut();
        var detail = await sut.GetDetailAsync(flag.Id, CancellationToken.None);

        Assert.NotNull(detail);
        Assert.NotNull(detail!.FlagDetail);
        // Fallback shape exposes the raw payload — see FraudFlagAdminQueryService.ParseDetail.
        var rawProperty = detail.FlagDetail!.GetType().GetProperty("raw");
        Assert.NotNull(rawProperty);
        Assert.Equal("this is not json", rawProperty!.GetValue(detail.FlagDetail));
    }

    [Fact]
    public async Task GetDetailAsync_Returns_MultiAccount_SupportingSignals()
    {
        // K10 — supportingSignals persisted by MultiAccountDetector must round-trip
        // through the AD3 DTO (07 §9.3:1742).
        var details = JsonSerializer.Serialize(new
        {
            matchType = "WALLET_PAYOUT",
            matchValue = "TWalletAddr123",
            linkedAccounts = new[]
            {
                new { steamId = "76561198000000010", displayName = "Alt One" },
            },
            supportingSignals = new[]
            {
                new
                {
                    type = "IP_ADDRESS",
                    value = "203.0.113.7",
                    linkedAccounts = new[]
                    {
                        new { steamId = "76561198000000011", displayName = "Alt Two" },
                    },
                },
            },
        });
        var flag = await SeedFlagAsync(
            scope: FraudFlagScope.ACCOUNT_LEVEL,
            type: FraudFlagType.MULTI_ACCOUNT,
            status: ReviewStatus.PENDING,
            details: details);

        var sut = BuildSut();
        var detail = await sut.GetDetailAsync(flag.Id, CancellationToken.None);

        Assert.NotNull(detail);
        var payload = Assert.IsType<MultiAccountFlagDetail>(detail!.FlagDetail);
        Assert.Equal("WALLET_PAYOUT", payload.MatchType);
        Assert.Equal("TWalletAddr123", payload.MatchValue);
        Assert.Single(payload.LinkedAccounts);
        var signal = Assert.Single(payload.SupportingSignals);
        Assert.Equal("IP_ADDRESS", signal.Type);
        Assert.Equal("203.0.113.7", signal.Value);
        var signalLinked = Assert.Single(signal.LinkedAccounts);
        Assert.Equal("Alt Two", signalLinked.DisplayName);
    }

    [Fact]
    public async Task GetDetailAsync_Returns_ActiveTransactions_With_Role_And_Hold()
    {
        // K9 — flagged user's active (non-terminal) transactions; either party;
        // terminal states excluded; held rows kept and marked.
        await SeedTransactionAsync(_seller.Id, _buyer.Id, TransactionStatus.CREATED);
        await SeedTransactionAsync(_seller.Id, _buyer.Id, TransactionStatus.ITEM_ESCROWED, isOnHold: true);
        await SeedTransactionAsync(_buyer.Id, _seller.Id, TransactionStatus.PAYMENT_RECEIVED);
        await SeedTransactionAsync(_seller.Id, _buyer.Id, TransactionStatus.COMPLETED);
        await SeedTransactionAsync(_seller.Id, _buyer.Id, TransactionStatus.CANCELLED_ADMIN);

        var flag = await SeedFlagAsync(
            scope: FraudFlagScope.ACCOUNT_LEVEL,
            type: FraudFlagType.MULTI_ACCOUNT,
            status: ReviewStatus.PENDING);

        var sut = BuildSut();
        var detail = await sut.GetDetailAsync(flag.Id, CancellationToken.None);

        Assert.NotNull(detail);
        Assert.Equal(3, detail!.ActiveTransactions.Count);
        Assert.DoesNotContain(detail.ActiveTransactions,
            t => t.Status is TransactionStatus.COMPLETED or TransactionStatus.CANCELLED_ADMIN);

        var held = Assert.Single(detail.ActiveTransactions, t => t.IsOnHold);
        Assert.Equal(FlagTransactionRole.SELLER, held.Role);
        Assert.Equal(TransactionStatus.ITEM_ESCROWED, held.Status);

        var asBuyer = Assert.Single(detail.ActiveTransactions, t => t.Role == FlagTransactionRole.BUYER);
        Assert.Equal(TransactionStatus.PAYMENT_RECEIVED, asBuyer.Status);
        Assert.False(asBuyer.IsOnHold);
    }

    [Fact]
    public async Task ListAsync_Account_Flag_Populates_Signal_And_ActiveCount()
    {
        // K2 — account-flag rows carry signalSummary / linkedAccountCount /
        // activeTransactionCount (04 §8.2).
        var details = JsonSerializer.Serialize(new
        {
            matchType = "WALLET_PAYOUT",
            matchValue = "TListAddr",
            linkedAccounts = new[]
            {
                new { steamId = "1", displayName = "A" },
                new { steamId = "2", displayName = "B" },
            },
            supportingSignals = Array.Empty<object>(),
        });
        await SeedFlagAsync(
            scope: FraudFlagScope.ACCOUNT_LEVEL,
            type: FraudFlagType.MULTI_ACCOUNT,
            status: ReviewStatus.PENDING,
            details: details);

        // Two active + one terminal for the flagged user.
        await SeedTransactionAsync(_seller.Id, _buyer.Id, TransactionStatus.CREATED);
        await SeedTransactionAsync(_seller.Id, _buyer.Id, TransactionStatus.ITEM_ESCROWED);
        await SeedTransactionAsync(_seller.Id, _buyer.Id, TransactionStatus.COMPLETED);

        var sut = BuildSut();
        var result = await sut.ListAsync(
            new FraudFlagListQuery(FraudFlagScope.ACCOUNT_LEVEL, null, null, null, null, null, null, 1, 20),
            CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal("TListAddr", item.SignalSummary);
        Assert.Equal(2, item.LinkedAccountCount);
        Assert.Equal(2, item.ActiveTransactionCount);
    }

    [Fact]
    public async Task ListAsync_Transaction_Flag_Leaves_Account_Fields_Null()
    {
        var tx = await SeedTransactionAsync(_seller.Id, _buyer.Id, TransactionStatus.FLAGGED);
        await SeedFlagAsync(
            scope: FraudFlagScope.TRANSACTION_PRE_CREATE,
            type: FraudFlagType.PRICE_DEVIATION,
            status: ReviewStatus.PENDING,
            transactionId: tx.Id);

        var sut = BuildSut();
        var result = await sut.ListAsync(
            new FraudFlagListQuery(FraudFlagScope.TRANSACTION_PRE_CREATE, null, null, null, null, null, null, 1, 20),
            CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Null(item.SignalSummary);
        Assert.Null(item.LinkedAccountCount);
        Assert.Null(item.ActiveTransactionCount);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private FraudFlagAdminQueryService BuildSut()
    {
        // T43 reputation: thresholds high enough that the test users (no completed
        // tx, fresh CreatedAt) never satisfy → ReputationScore = null. Detail tests
        // that exercise the populated branch override `_seller`/`_buyer` state.
        var thresholds = new StubReputationThresholdsProvider(
            new ReputationThresholds(MinAccountAgeDays: 30, MinCompletedTransactions: 5));
        var calculator = new ReputationScoreCalculator(thresholds);
        return new FraudFlagAdminQueryService(Context, calculator, _clock);
    }

    private sealed class StubReputationThresholdsProvider : IReputationThresholdsProvider
    {
        private readonly ReputationThresholds _values;
        public StubReputationThresholdsProvider(ReputationThresholds values) => _values = values;
        public Task<ReputationThresholds> GetAsync(CancellationToken cancellationToken)
            => Task.FromResult(_values);
    }

    private async Task<FraudFlag> SeedFlagAsync(
        FraudFlagScope scope,
        FraudFlagType type,
        ReviewStatus status,
        Guid? transactionId = null,
        bool adminReviewed = false,
        string? details = null)
    {
        var flag = new FraudFlag
        {
            Id = Guid.NewGuid(),
            UserId = _seller.Id,
            TransactionId = scope == FraudFlagScope.TRANSACTION_PRE_CREATE
                ? transactionId ?? throw new ArgumentException("transactionId required for pre-create scope.")
                : null,
            Scope = scope,
            Type = type,
            Status = status,
            Details = details ?? "{}",
            ReviewedAt = adminReviewed ? _clock.GetUtcNow().UtcDateTime : null,
            ReviewedByAdminId = adminReviewed ? _buyer.Id : null,
        };

        Context.Set<FraudFlag>().Add(flag);
        await Context.SaveChangesAsync();
        await Task.Delay(2); // ensure deterministic CreatedAt ordering across rows
        return flag;
    }

    private async Task<Transaction> SeedTransactionAsync(
        Guid sellerId, Guid buyerId, TransactionStatus status, bool isOnHold = false)
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var tx = new Transaction
        {
            Id = Guid.NewGuid(),
            Status = status,
            IsOnHold = isOnHold,
            // SQL Server enforces CK_Transactions_FreezeHold_Reverse/Forward +
            // Freeze{Active,Passive}: a held row must carry the EMERGENCY_HOLD
            // freeze trio. (SQLite ignores these CHECKs, so this only matters on
            // the CI mssql runner.) Mirrors the T44/T50 emergency-hold stamp.
            TimeoutFrozenAt = isOnHold ? nowUtc : null,
            TimeoutFreezeReason = isOnHold ? Skinora.Shared.Enums.TimeoutFreezeReason.EMERGENCY_HOLD : null,
            TimeoutRemainingSeconds = isOnHold ? 3600 : null,
            SellerId = sellerId,
            BuyerId = buyerId,
            BuyerIdentificationMethod = BuyerIdentificationMethod.STEAM_ID,
            TargetBuyerSteamId = "76561198555000001",
            // ItemAssetId column is HasMaxLength(20).
            ItemAssetId = Guid.NewGuid().GetHashCode().ToString("X").PadLeft(8, '0') + "test",
            ItemClassId = "class-1",
            ItemName = "Listed Item",
            ItemIconUrl = "https://cdn/icon.png",
            StablecoinType = StablecoinType.USDT,
            Price = 120m,
            CommissionRate = 0.03m,
            CommissionAmount = 3.6m,
            TotalAmount = 123.6m,
            MarketPriceAtCreation = 50m,
            SellerPayoutAddress = "TXqH2JBkDgGWyCFg4GZzg8eUjG5JMZ7hPL",
            PaymentTimeoutMinutes = 60,
            AcceptDeadline = status == TransactionStatus.FLAGGED ? null : nowUtc + TimeSpan.FromMinutes(60),
            CompletedAt = status == TransactionStatus.COMPLETED ? nowUtc : null,
        };
        Context.Set<Transaction>().Add(tx);
        await Context.SaveChangesAsync();
        return tx;
    }
}
