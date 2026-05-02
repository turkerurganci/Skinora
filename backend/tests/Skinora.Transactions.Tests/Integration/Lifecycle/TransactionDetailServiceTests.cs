using Microsoft.Extensions.Time.Testing;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Shared.Tests.Integration;
using Skinora.Transactions.Application.Lifecycle;
using Skinora.Transactions.Domain.Entities;
using Skinora.Transactions.Infrastructure.Persistence;
using Skinora.Users.Domain.Entities;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.Transactions.Tests.Integration.Lifecycle;

/// <summary>
/// Integration coverage for <see cref="TransactionDetailService"/>
/// (T46 — 07 §7.5). Verifies the public-vs-authenticated contract split,
/// role-based payload (seller / buyer / non-party 403), state-blocked
/// section nullability, EMERGENCY_HOLD action freeze, and timeout
/// remainingSeconds.
/// </summary>
public class TransactionDetailServiceTests : IntegrationTestBase
{
    static TransactionDetailServiceTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        TransactionsModuleDbRegistration.RegisterTransactionsModule();
        Skinora.Platform.Infrastructure.Persistence.PlatformModuleDbRegistration.RegisterPlatformModule();
    }

    private const string ValidWallet = "TXyzABCDEFGHJKLMNPQRSTUVWXYZ234567";
    private const string SellerSteamId = "76561198000000090";
    private const string BuyerSteamId = "76561198000000091";

    private User _seller = null!;
    private User _buyer = null!;
    private FakeTimeProvider _clock = null!;

    protected override async Task SeedAsync(AppDbContext context)
    {
        _seller = new User
        {
            Id = Guid.NewGuid(),
            SteamId = SellerSteamId,
            SteamDisplayName = "SellerPlayer",
            SteamAvatarUrl = "https://steamcdn.example/seller.jpg",
            DefaultPayoutAddress = ValidWallet,
            MobileAuthenticatorVerified = true,
            CompletedTransactionCount = 24,
            SuccessfulTransactionRate = 0.96m,
        };
        _buyer = new User
        {
            Id = Guid.NewGuid(),
            SteamId = BuyerSteamId,
            SteamDisplayName = "BuyerPlayer",
            SteamAvatarUrl = "https://steamcdn.example/buyer.jpg",
            MobileAuthenticatorVerified = true,
            CompletedTransactionCount = 8,
            SuccessfulTransactionRate = 0.84m,
        };
        context.Set<User>().AddRange(_seller, _buyer);
        await context.SaveChangesAsync();

        _clock = new FakeTimeProvider(new DateTimeOffset(2026, 5, 2, 12, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task Returns_Public_Variant_When_Caller_Is_Anonymous()
    {
        var transaction = await CreateTransactionAsync(TransactionStatus.CREATED);

        var sut = BuildSut();
        var outcome = await sut.GetAsync(transaction.Id, callerId: null, callerSteamId: null, CancellationToken.None);

        Assert.Equal(TransactionDetailStatus.Found, outcome.Status);
        Assert.NotNull(outcome.Body);
        Assert.Null(outcome.Body.UserRole);
        Assert.Equal("100.00", outcome.Body.Price);
        // Public variant suppresses commission, total, full party data, and
        // gives availableActions{ canAccept=false, requiresLogin=true }.
        Assert.Null(outcome.Body.CommissionRate);
        Assert.Null(outcome.Body.CommissionAmount);
        Assert.Null(outcome.Body.TotalAmount);
        Assert.Null(outcome.Body.Buyer);
        Assert.Null(outcome.Body.Timeout);
        Assert.False(outcome.Body.AvailableActions.CanAccept);
        Assert.True(outcome.Body.AvailableActions.RequiresLogin!.Value);
        Assert.Null(outcome.Body.AvailableActions.CanCancel);
        // Seller exposed: display name only.
        Assert.Equal("SellerPlayer", outcome.Body.Seller.DisplayName);
        Assert.Null(outcome.Body.Seller.SteamId);
    }

    [Fact]
    public async Task Returns_Buyer_View_When_Caller_Is_Buyer_Of_Created_Transaction()
    {
        var transaction = await CreateTransactionAsync(TransactionStatus.CREATED, buyerId: _buyer.Id);

        var sut = BuildSut();
        var outcome = await sut.GetAsync(transaction.Id, _buyer.Id, callerSteamId: null, CancellationToken.None);

        Assert.Equal(TransactionDetailStatus.Found, outcome.Status);
        Assert.Equal("buyer", outcome.Body!.UserRole);
        // Buyer block only fills from ACCEPTED onwards (07 §7.5 conditional table).
        Assert.Null(outcome.Body.Buyer);
        // Authenticated commission/total surface filled in.
        Assert.Equal("2.00", outcome.Body.CommissionAmount);
        Assert.Equal("102.00", outcome.Body.TotalAmount);
        // Seller reputation ROUND(0.96 × 5, 1, ToZero) = 4.8.
        Assert.Equal(4.8m, outcome.Body.Seller.ReputationScore);
        Assert.Equal(24, outcome.Body.Seller.CompletedTransactionCount);
        // canAccept gated on buyer + CREATED + BuyerId is null.
        Assert.False(outcome.Body.AvailableActions.CanAccept);  // BuyerId already set
        Assert.True(outcome.Body.AvailableActions.CanCancel!.Value);
        // requiresLogin field suppressed for authenticated callers.
        Assert.Null(outcome.Body.AvailableActions.RequiresLogin);
    }

    [Fact]
    public async Task Target_Buyer_Can_View_Created_Transaction_Before_Accepting()
    {
        // 03 §3.2 step 1: the named Steam buyer must be able to read the
        // detail page before deciding to accept. BuyerId is still null at
        // that point; role resolution falls through to Steam ID match.
        var transaction = await CreateTransactionAsync(TransactionStatus.CREATED);

        var sut = BuildSut();
        var outcome = await sut.GetAsync(transaction.Id, _buyer.Id, _buyer.SteamId, CancellationToken.None);

        Assert.Equal(TransactionDetailStatus.Found, outcome.Status);
        Assert.Equal("buyer", outcome.Body!.UserRole);
        Assert.True(outcome.Body.AvailableActions.CanAccept);
    }

    [Fact]
    public async Task Returns_Seller_View_With_Invite_Info_For_Open_Link()
    {
        var transaction = await CreateTransactionAsync(
            TransactionStatus.CREATED,
            method: BuyerIdentificationMethod.OPEN_LINK);

        var sut = BuildSut();
        var outcome = await sut.GetAsync(transaction.Id, _seller.Id, callerSteamId: null, CancellationToken.None);

        Assert.Equal(TransactionDetailStatus.Found, outcome.Status);
        Assert.Equal("seller", outcome.Body!.UserRole);
        Assert.NotNull(outcome.Body.InviteInfo);
        Assert.StartsWith("/invite/", outcome.Body.InviteInfo.InviteUrl);
        Assert.False(outcome.Body.InviteInfo.BuyerRegistered);
    }

    [Fact]
    public async Task Returns_Buyer_Block_Once_Transaction_Is_Accepted()
    {
        var transaction = await CreateTransactionAsync(
            TransactionStatus.ACCEPTED,
            buyerId: _buyer.Id);

        var sut = BuildSut();
        var outcome = await sut.GetAsync(transaction.Id, _seller.Id, callerSteamId: null, CancellationToken.None);

        Assert.Equal(TransactionDetailStatus.Found, outcome.Status);
        Assert.NotNull(outcome.Body!.Buyer);
        Assert.Equal("BuyerPlayer", outcome.Body.Buyer.DisplayName);
        Assert.Equal(BuyerSteamId, outcome.Body.Buyer.SteamId);
        // Seller view after accept: canAccept=false (buyer is set + status not CREATED).
        Assert.False(outcome.Body.AvailableActions.CanAccept);
    }

    [Fact]
    public async Task Returns_403_For_Non_Party_Authenticated_Caller()
    {
        var transaction = await CreateTransactionAsync(TransactionStatus.CREATED);
        var stranger = new User
        {
            Id = Guid.NewGuid(),
            SteamId = "76561198000099999",
            SteamDisplayName = "Stranger",
        };
        Context.Set<User>().Add(stranger);
        await Context.SaveChangesAsync();

        var sut = BuildSut();
        var outcome = await sut.GetAsync(transaction.Id, stranger.Id, callerSteamId: null, CancellationToken.None);

        Assert.Equal(TransactionDetailStatus.NotAParty, outcome.Status);
        Assert.Equal(TransactionErrorCodes.NotAParty, outcome.ErrorCode);
    }

    [Fact]
    public async Task Returns_404_For_Unknown_Transaction()
    {
        var sut = BuildSut();
        var outcome = await sut.GetAsync(Guid.NewGuid(), _buyer.Id, callerSteamId: null, CancellationToken.None);

        Assert.Equal(TransactionDetailStatus.NotFound, outcome.Status);
        Assert.Equal(TransactionErrorCodes.TransactionNotFound, outcome.ErrorCode);
    }

    [Fact]
    public async Task Surfaces_Active_Accept_Timeout_With_Remaining_Seconds()
    {
        var transaction = await CreateTransactionAsync(TransactionStatus.CREATED);

        var sut = BuildSut();
        var outcome = await sut.GetAsync(transaction.Id, _seller.Id, callerSteamId: null, CancellationToken.None);

        Assert.NotNull(outcome.Body!.Timeout);
        Assert.Equal("accept", outcome.Body.Timeout.Type);
        Assert.True(outcome.Body.Timeout.RemainingSeconds > 0);
        Assert.False(outcome.Body.Timeout.Frozen);
    }

    [Fact]
    public async Task Emergency_Hold_Forces_All_Actions_False()
    {
        var transaction = await CreateTransactionAsync(TransactionStatus.ACCEPTED, buyerId: _buyer.Id);
        // 06 §3.5 invariant set:
        //   IsOnHold=1 ↔ EmergencyHold{At,Reason,ByAdminId} NOT NULL
        //   IsOnHold=1 ↔ TimeoutFrozenAt + Reason='EMERGENCY_HOLD' + RemainingSeconds NOT NULL
        transaction.IsOnHold = true;
        transaction.EmergencyHoldAt = _clock.GetUtcNow().UtcDateTime;
        transaction.EmergencyHoldReason = "Sanctions match";
        // FK_Transactions_Users_EmergencyHoldByAdminId — must point at a real
        // user row. Reuse the seller as the audit-time admin stub.
        transaction.EmergencyHoldByAdminId = _seller.Id;
        transaction.PreviousStatusBeforeHold = (int)TransactionStatus.ACCEPTED;
        transaction.TimeoutFrozenAt = transaction.EmergencyHoldAt;
        transaction.TimeoutFreezeReason = TimeoutFreezeReason.EMERGENCY_HOLD;
        transaction.TimeoutRemainingSeconds = 0;
        Context.Set<Transaction>().Update(transaction);
        await Context.SaveChangesAsync();

        var sut = BuildSut();
        var outcome = await sut.GetAsync(transaction.Id, _buyer.Id, callerSteamId: null, CancellationToken.None);

        Assert.False(outcome.Body!.AvailableActions.CanAccept);
        Assert.False(outcome.Body.AvailableActions.CanCancel!.Value);
        Assert.False(outcome.Body.AvailableActions.CanDispute!.Value);
        Assert.False(outcome.Body.AvailableActions.CanEscalate!.Value);
        Assert.NotNull(outcome.Body.HoldInfo);
    }

    [Fact]
    public async Task Flagged_State_Surfaces_Flag_Info()
    {
        var transaction = await CreateTransactionAsync(TransactionStatus.FLAGGED);
        transaction.AcceptDeadline = null;       // 06 §3.5 FLAGGED invariant
        Context.Set<Transaction>().Update(transaction);
        await Context.SaveChangesAsync();

        var sut = BuildSut();
        var outcome = await sut.GetAsync(transaction.Id, _seller.Id, callerSteamId: null, CancellationToken.None);

        Assert.NotNull(outcome.Body!.FlagInfo);
        Assert.Equal("PRICE_DEVIATION", outcome.Body.FlagInfo.FlagType);
        Assert.Null(outcome.Body.Timeout); // FLAGGED has no active deadline
    }

    private async Task<Transaction> CreateTransactionAsync(
        TransactionStatus status,
        Guid? buyerId = null,
        BuyerIdentificationMethod method = BuyerIdentificationMethod.STEAM_ID)
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var transaction = new Transaction
        {
            Id = Guid.NewGuid(),
            Status = status,
            SellerId = _seller.Id,
            BuyerId = buyerId,
            BuyerIdentificationMethod = method,
            TargetBuyerSteamId = method == BuyerIdentificationMethod.STEAM_ID ? BuyerSteamId : null,
            InviteToken = method == BuyerIdentificationMethod.OPEN_LINK ? "T46-detail-test" : null,
            ItemAssetId = "27348562891",
            ItemClassId = "abc-class",
            ItemName = "AK-47 | Redline",
            ItemType = "Rifle",
            ItemExterior = "Field-Tested",
            ItemIconUrl = "https://steamcdn.example/item.png",
            StablecoinType = StablecoinType.USDT,
            Price = 100m,
            CommissionRate = 0.02m,
            CommissionAmount = 2m,
            TotalAmount = 102m,
            SellerPayoutAddress = ValidWallet,
            BuyerRefundAddress = buyerId.HasValue ? ValidWallet : null,
            PaymentTimeoutMinutes = 1440,
            AcceptDeadline = status == TransactionStatus.CREATED ? nowUtc.AddHours(1) : null,
            AcceptedAt = status == TransactionStatus.ACCEPTED ? nowUtc.AddMinutes(-5) : null,
        };
        Context.Set<Transaction>().Add(transaction);
        await Context.SaveChangesAsync();
        return transaction;
    }

    private TransactionDetailService BuildSut() => new(Context, _clock);
}
