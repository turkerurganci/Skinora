using Microsoft.Extensions.Time.Testing;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Shared.Tests.Integration;
using Skinora.Transactions.Application.Lifecycle;
using Skinora.Transactions.Application.Steam;
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
        // WP20 — canAccept = buyer + CREATED (07 §7.5 / 03 §3.2:195). A
        // registered STEAM_ID buyer has BuyerId set at create time yet must
        // still be able to accept; role=="buyer" already proves eligibility.
        Assert.True(outcome.Body.AvailableActions.CanAccept);  // BuyerId set, still acceptable
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
        // WP20 — status is the EMERGENCY_HOLD overlay (07 §7.1/§7.5), not the
        // raw underlying ACCEPTED state, so the FE hold banner + frozen panel
        // (keyed off status=="EMERGENCY_HOLD") fire on the detail surface.
        Assert.Equal("EMERGENCY_HOLD", outcome.Body.Status);
    }

    [Fact]
    public async Task Registered_Steam_Id_Buyer_Can_Accept_Created_Transaction()
    {
        // WP20 (canAccept fix) — a registered STEAM_ID target buyer has BuyerId
        // resolved + set at create time (TransactionCreationService), so the
        // pre-fix `&& BuyerId is null` clause wrongly disabled their Accept
        // button even though the accept endpoint admits them. canAccept must be
        // true: buyer + CREATED (07 §7.5 / 03 §3.2:195).
        var transaction = await CreateTransactionAsync(TransactionStatus.CREATED, buyerId: _buyer.Id);

        var sut = BuildSut();
        var outcome = await sut.GetAsync(transaction.Id, _buyer.Id, callerSteamId: null, CancellationToken.None);

        Assert.Equal(TransactionDetailStatus.Found, outcome.Status);
        Assert.Equal("buyer", outcome.Body!.UserRole);
        Assert.True(outcome.Body.AvailableActions.CanAccept);
        // CREATED is the raw status here (not on hold) — no EMERGENCY_HOLD overlay.
        Assert.Equal("CREATED", outcome.Body.Status);
    }

    [Fact]
    public async Task Held_Created_Transaction_Freezes_Accept_And_Projects_Emergency_Hold()
    {
        // WP20 — even with the widened canAccept (buyer + CREATED), the IsOnHold
        // early-return keeps a held CREATED transaction at all-false, and the
        // status projects to the EMERGENCY_HOLD overlay (CHANGE B) so the FE
        // surfaces the frozen panel instead of the (now defensive-only) Accept
        // form. 06 §3.5 hold invariant set mirrors the ACCEPTED hold test.
        var transaction = await CreateTransactionAsync(TransactionStatus.CREATED, buyerId: _buyer.Id);
        transaction.IsOnHold = true;
        transaction.EmergencyHoldAt = _clock.GetUtcNow().UtcDateTime;
        transaction.EmergencyHoldReason = "Manual review";
        transaction.EmergencyHoldByAdminId = _seller.Id;
        transaction.PreviousStatusBeforeHold = (int)TransactionStatus.CREATED;
        transaction.TimeoutFrozenAt = transaction.EmergencyHoldAt;
        transaction.TimeoutFreezeReason = TimeoutFreezeReason.EMERGENCY_HOLD;
        transaction.TimeoutRemainingSeconds = 0;
        Context.Set<Transaction>().Update(transaction);
        await Context.SaveChangesAsync();

        var sut = BuildSut();
        var outcome = await sut.GetAsync(transaction.Id, _buyer.Id, callerSteamId: null, CancellationToken.None);

        Assert.Equal("EMERGENCY_HOLD", outcome.Body!.Status);
        Assert.False(outcome.Body.AvailableActions.CanAccept);
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

    // ---- GET /transactions/by-invite/:token (07 §7.5a, F-INVITE-01) ----

    [Fact]
    public async Task GetByInvite_Unauthenticated_Returns_Public_Variant()
    {
        var transaction = await CreateTransactionAsync(
            TransactionStatus.CREATED,
            method: BuyerIdentificationMethod.OPEN_LINK,
            inviteToken: "inv-unauth-token");

        var sut = BuildSut();
        var outcome = await sut.GetByInviteTokenAsync(
            "inv-unauth-token", callerId: null, callerSteamId: null, CancellationToken.None);

        Assert.Equal(TransactionDetailStatus.Found, outcome.Status);
        Assert.NotNull(outcome.Body);
        Assert.Equal(transaction.Id, outcome.Body.Id);
        Assert.Null(outcome.Body.UserRole);
        Assert.Equal("100.00", outcome.Body.Price);
        // Trimmed public shape — no commission/total/timeout, requiresLogin.
        Assert.Null(outcome.Body.CommissionAmount);
        Assert.Null(outcome.Body.TotalAmount);
        Assert.Null(outcome.Body.Timeout);
        Assert.False(outcome.Body.AvailableActions.CanAccept);
        Assert.True(outcome.Body.AvailableActions.RequiresLogin!.Value);
        Assert.Equal("SellerPlayer", outcome.Body.Seller.DisplayName);
        Assert.Null(outcome.Body.Seller.SteamId);
    }

    [Fact]
    public async Task GetByInvite_Authenticated_Stranger_Is_Prospective_Buyer()
    {
        var transaction = await CreateTransactionAsync(
            TransactionStatus.CREATED,
            method: BuyerIdentificationMethod.OPEN_LINK,
            inviteToken: "inv-prospect-token");
        var prospectiveBuyerId = Guid.NewGuid(); // token holder, not yet a party

        var sut = BuildSut();
        var outcome = await sut.GetByInviteTokenAsync(
            "inv-prospect-token", prospectiveBuyerId, callerSteamId: null, CancellationToken.None);

        Assert.Equal(TransactionDetailStatus.Found, outcome.Status);
        Assert.Equal(transaction.Id, outcome.Body!.Id);
        Assert.Equal("buyer", outcome.Body.UserRole);
        // Prospective buyer sees the full acceptance surface...
        Assert.Equal("2.00", outcome.Body.CommissionAmount);
        Assert.Equal("102.00", outcome.Body.TotalAmount);
        Assert.Equal(4.8m, outcome.Body.Seller.ReputationScore);
        Assert.True(outcome.Body.AvailableActions.CanAccept);
        // ...but cancel/dispute belong to actual parties only.
        Assert.Null(outcome.Body.AvailableActions.CanCancel);
        Assert.Null(outcome.Body.AvailableActions.CanDispute);
        Assert.Null(outcome.Body.AvailableActions.RequiresLogin);
        // Not yet a party → no buyer block, no seller-only invite link.
        Assert.Null(outcome.Body.Buyer);
        Assert.Null(outcome.Body.InviteInfo);
    }

    [Fact]
    public async Task GetByInvite_Seller_Sees_Seller_View_With_Invite_Link()
    {
        await CreateTransactionAsync(
            TransactionStatus.CREATED,
            method: BuyerIdentificationMethod.OPEN_LINK,
            inviteToken: "inv-seller-token");

        var sut = BuildSut();
        var outcome = await sut.GetByInviteTokenAsync(
            "inv-seller-token", _seller.Id, callerSteamId: null, CancellationToken.None);

        Assert.Equal(TransactionDetailStatus.Found, outcome.Status);
        Assert.Equal("seller", outcome.Body!.UserRole);
        Assert.NotNull(outcome.Body.InviteInfo);
        Assert.StartsWith("/invite/", outcome.Body.InviteInfo.InviteUrl);
        // Seller cannot accept their own listing.
        Assert.False(outcome.Body.AvailableActions.CanAccept);
    }

    [Fact]
    public async Task GetByInvite_Unknown_Token_Returns_404()
    {
        var sut = BuildSut();
        var outcome = await sut.GetByInviteTokenAsync(
            "no-such-token", callerId: null, callerSteamId: null, CancellationToken.None);

        Assert.Equal(TransactionDetailStatus.NotFound, outcome.Status);
        Assert.Equal(TransactionErrorCodes.TransactionNotFound, outcome.ErrorCode);
    }

    [Fact]
    public async Task GetByInvite_Empty_Token_Returns_404_Without_Matching_SteamId_Rows()
    {
        // A blank token must NOT translate to "InviteToken IS NULL" and match
        // STEAM_ID transactions — defensive guard in the service.
        await CreateTransactionAsync(TransactionStatus.CREATED); // STEAM_ID, InviteToken null

        var sut = BuildSut();
        var outcome = await sut.GetByInviteTokenAsync(
            string.Empty, callerId: null, callerSteamId: null, CancellationToken.None);

        Assert.Equal(TransactionDetailStatus.NotFound, outcome.Status);
    }

    [Fact]
    public async Task GetByInvite_Spent_Invite_NonParty_Falls_Back_To_Public_Shape()
    {
        // Already accepted (BuyerId set, status ACCEPTED). A non-party token
        // holder gets the trimmed public shape (FE renders "unavailable"),
        // while the buyer who accepted still gets their buyer view.
        var transaction = await CreateTransactionAsync(
            TransactionStatus.ACCEPTED,
            buyerId: _buyer.Id,
            method: BuyerIdentificationMethod.OPEN_LINK,
            inviteToken: "inv-spent-token");

        var sut = BuildSut();

        var stranger = await sut.GetByInviteTokenAsync(
            "inv-spent-token", Guid.NewGuid(), callerSteamId: null, CancellationToken.None);
        Assert.Equal(TransactionDetailStatus.Found, stranger.Status);
        Assert.Null(stranger.Body!.UserRole); // public/unavailable shape
        Assert.False(stranger.Body.AvailableActions.CanAccept);

        var buyer = await sut.GetByInviteTokenAsync(
            "inv-spent-token", _buyer.Id, callerSteamId: null, CancellationToken.None);
        Assert.Equal(TransactionDetailStatus.Found, buyer.Status);
        Assert.Equal("buyer", buyer.Body!.UserRole);
        Assert.Equal(transaction.Id, buyer.Body.Id);
    }

    [Fact]
    public async Task Completed_SellerView_Surfaces_PayoutBreakdown()
    {
        var transaction = await CreateTransactionAsync(
            TransactionStatus.COMPLETED, buyerId: _buyer.Id);
        await AddConfirmedPayoutAsync(transaction.Id, netAmount: 99.70m, gasSnapshot: 0.50m);

        var sut = BuildSut();
        var outcome = await sut.GetAsync(
            transaction.Id, callerId: _seller.Id, callerSteamId: SellerSteamId, CancellationToken.None);

        Assert.Equal(TransactionDetailStatus.Found, outcome.Status);
        var payout = outcome.Body!.SellerPayout;
        Assert.NotNull(payout);
        Assert.Equal("100.00", payout!.GrossAmount);
        Assert.Equal("99.70", payout.NetAmount);
        Assert.Equal("0.30", payout.GasFeeFromSeller);
        Assert.Equal("0.20", payout.GasFeeFromCommission);
        Assert.Equal("0.50", payout.GasFee);
        Assert.Equal(ValidWallet, payout.WalletAddress);
        Assert.Equal("0xPayoutConfirmed", payout.TxHash);
    }

    [Fact]
    public async Task Completed_BuyerView_Omits_PayoutBreakdown()
    {
        var transaction = await CreateTransactionAsync(
            TransactionStatus.COMPLETED, buyerId: _buyer.Id);
        await AddConfirmedPayoutAsync(transaction.Id, netAmount: 99.70m, gasSnapshot: 0.50m);

        var sut = BuildSut();
        var outcome = await sut.GetAsync(
            transaction.Id, callerId: _buyer.Id, callerSteamId: BuyerSteamId, CancellationToken.None);

        Assert.Equal(TransactionDetailStatus.Found, outcome.Status);
        Assert.Null(outcome.Body!.SellerPayout);
    }

    [Fact]
    public async Task Cancelled_BuyerView_Surfaces_RefundBreakdown()
    {
        // WP2 — the buyer's view of a cancelled, payment-refunded transaction
        // reconstructs the 07 §7.5 refund breakdown from the BUYER_REFUND row:
        // originalAmount = net (100) + gas snapshot (2) = 102 = TotalAmount.
        var transaction = await CreateTransactionAsync(
            TransactionStatus.CANCELLED_ADMIN, buyerId: _buyer.Id);
        await AddConfirmedRefundAsync(transaction.Id, netAmount: 100m, gasSnapshot: 2m);

        var sut = BuildSut();
        var outcome = await sut.GetAsync(
            transaction.Id, callerId: _buyer.Id, callerSteamId: BuyerSteamId, CancellationToken.None);

        Assert.Equal(TransactionDetailStatus.Found, outcome.Status);
        var refund = outcome.Body!.Refund;
        Assert.NotNull(refund);
        Assert.Equal("102.00", refund!.OriginalAmount);
        Assert.Equal("2.00", refund.GasFee);
        Assert.Equal("100.00", refund.NetRefundAmount);
        Assert.Equal(ValidWallet, refund.RefundAddress);
        Assert.Equal("0xRefundConfirmed", refund.TxHash);
    }

    [Fact]
    public async Task Cancelled_SellerView_Omits_RefundBreakdown()
    {
        var transaction = await CreateTransactionAsync(
            TransactionStatus.CANCELLED_ADMIN, buyerId: _buyer.Id);
        await AddConfirmedRefundAsync(transaction.Id, netAmount: 100m, gasSnapshot: 2m);

        var sut = BuildSut();
        var outcome = await sut.GetAsync(
            transaction.Id, callerId: _seller.Id, callerSteamId: SellerSteamId, CancellationToken.None);

        Assert.Equal(TransactionDetailStatus.Found, outcome.Status);
        Assert.Null(outcome.Body!.Refund);
    }

    private async Task AddConfirmedRefundAsync(Guid transactionId, decimal netAmount, decimal gasSnapshot)
    {
        Context.Set<BlockchainTransaction>().Add(new BlockchainTransaction
        {
            Id = Guid.NewGuid(),
            TransactionId = transactionId,
            Type = BlockchainTransactionType.BUYER_REFUND,
            TxHash = "0xRefundConfirmed",
            FromAddress = "THotWallet0000000000000000000000000",
            ToAddress = ValidWallet,
            Amount = netAmount,
            Token = StablecoinType.USDT,
            GasFee = gasSnapshot,
            Status = BlockchainTransactionStatus.CONFIRMED,
            BlockNumber = 1_500_000L,
            ConfirmationCount = 20,
            RetryCount = 0,
            CreatedAt = _clock.GetUtcNow().UtcDateTime,
            ConfirmedAt = _clock.GetUtcNow().UtcDateTime,
        });
        await Context.SaveChangesAsync();
    }

    private static bool IsCancelledStatus(TransactionStatus status) =>
        status is TransactionStatus.CANCELLED_TIMEOUT
            or TransactionStatus.CANCELLED_SELLER
            or TransactionStatus.CANCELLED_BUYER
            or TransactionStatus.CANCELLED_ADMIN;

    private async Task AddConfirmedPayoutAsync(Guid transactionId, decimal netAmount, decimal gasSnapshot)
    {
        Context.Set<BlockchainTransaction>().Add(new BlockchainTransaction
        {
            Id = Guid.NewGuid(),
            TransactionId = transactionId,
            Type = BlockchainTransactionType.SELLER_PAYOUT,
            TxHash = "0xPayoutConfirmed",
            FromAddress = "THotWallet0000000000000000000000000",
            ToAddress = ValidWallet,
            Amount = netAmount,
            Token = StablecoinType.USDT,
            GasFee = gasSnapshot,
            Status = BlockchainTransactionStatus.CONFIRMED,
            BlockNumber = 1_500_000L,
            ConfirmationCount = 20,
            RetryCount = 0,
            CreatedAt = _clock.GetUtcNow().UtcDateTime,
            ConfirmedAt = _clock.GetUtcNow().UtcDateTime,
        });
        await Context.SaveChangesAsync();
    }

    // ---------- T123: payment block disclosure (07 §7.5, 03 §2.3) ----------

    [Theory]
    [InlineData(TransactionStatus.CREATED)]
    [InlineData(TransactionStatus.ACCEPTED)]
    public async Task Payment_Block_Is_Hidden_Before_The_Seller_Confirms_Readiness(
        TransactionStatus status)
    {
        // 03 §2.3: the address is allocated at creation but must not be shown
        // until the seller re-confirms the item is still sendable — otherwise
        // the buyer pays into a stale listing.
        var transaction = await CreateTransactionAsync(status, buyerId: _buyer.Id);
        await AddPaymentAddressAsync(transaction.Id);

        var outcome = await BuildSut().GetAsync(
            transaction.Id, _buyer.Id, BuyerSteamId, CancellationToken.None);

        Assert.Null(outcome.Body!.Payment);
    }

    [Fact]
    public async Task Payment_Block_Is_Disclosed_Once_The_Seller_Has_Confirmed_Readiness()
    {
        var transaction = await CreateTransactionAsync(
            TransactionStatus.SELLER_CONFIRMED, buyerId: _buyer.Id, sellerConfirmed: true);
        await AddPaymentAddressAsync(transaction.Id);

        var outcome = await BuildSut().GetAsync(
            transaction.Id, _buyer.Id, BuyerSteamId, CancellationToken.None);

        var payment = outcome.Body!.Payment;
        Assert.NotNull(payment);
        Assert.Equal(PaymentDepositAddress, payment!.Address);
        Assert.Equal("102.00", payment.ExpectedAmount);
        Assert.Equal(StablecoinType.USDT, payment.Stablecoin);
        Assert.Equal("Tron (TRC-20)", payment.Network);
        // T124+ fill these once payments land; 07 §7.5 scopes txHash to
        // PAYMENT_RECEIVED onwards.
        Assert.Null(payment.TxHash);
    }

    [Fact]
    public async Task Payment_Block_Survives_A_Cancellation_That_Happened_After_The_Window_Opened()
    {
        // 03 §5.4 — a late/lost transfer has to be matched against the address
        // it was sent to, so hiding it on cancellation would strand the buyer.
        // 06 §3.5 keeps milestone stamps cumulative across CANCELLED_*.
        var transaction = await CreateTransactionAsync(
            TransactionStatus.CANCELLED_TIMEOUT, buyerId: _buyer.Id, sellerConfirmed: true);
        await AddPaymentAddressAsync(transaction.Id);

        var outcome = await BuildSut().GetAsync(
            transaction.Id, _buyer.Id, BuyerSteamId, CancellationToken.None);

        Assert.NotNull(outcome.Body!.Payment);
    }

    [Fact]
    public async Task Payment_Block_Stays_Hidden_On_A_Cancellation_From_Before_The_Window()
    {
        // The address was allocated at creation, so a status-set gate would
        // leak it here even though the window never opened.
        var transaction = await CreateTransactionAsync(
            TransactionStatus.CANCELLED_TIMEOUT, buyerId: _buyer.Id);
        await AddPaymentAddressAsync(transaction.Id);

        var outcome = await BuildSut().GetAsync(
            transaction.Id, _buyer.Id, BuyerSteamId, CancellationToken.None);

        Assert.Null(outcome.Body!.Payment);
    }

    [Fact]
    public async Task Payment_Block_Is_Never_Shown_To_A_Public_Viewer()
    {
        // A deposit address handed to a stranger is an invitation to phish the
        // buyer with it.
        var transaction = await CreateTransactionAsync(
            TransactionStatus.SELLER_CONFIRMED, buyerId: _buyer.Id, sellerConfirmed: true);
        await AddPaymentAddressAsync(transaction.Id);

        var outcome = await BuildSut().GetAsync(
            transaction.Id, callerId: null, callerSteamId: null, CancellationToken.None);

        Assert.Null(outcome.Body!.Payment);
    }

    [Fact]
    public async Task Payment_Block_Is_Omitted_When_Allocation_Has_Not_Landed_Yet()
    {
        // Allocation is retried by EnsurePaymentAddressJob, so a missing row is
        // a transient gap. Omitting the block beats emitting an empty address
        // the UI would render as payable.
        var transaction = await CreateTransactionAsync(
            TransactionStatus.SELLER_CONFIRMED, buyerId: _buyer.Id, sellerConfirmed: true);

        var outcome = await BuildSut().GetAsync(
            transaction.Id, _buyer.Id, BuyerSteamId, CancellationToken.None);

        Assert.Null(outcome.Body!.Payment);
    }

    private const string PaymentDepositAddress = "TPaymentAddr1234567890abcdef1234";

    private async Task AddPaymentAddressAsync(Guid transactionId)
    {
        Context.Set<PaymentAddress>().Add(new PaymentAddress
        {
            Id = Guid.NewGuid(),
            TransactionId = transactionId,
            Address = PaymentDepositAddress,
            HdWalletIndex = 1,
            ExpectedAmount = 102m,
            ExpectedToken = StablecoinType.USDT,
            MonitoringStatus = MonitoringStatus.ACTIVE,
        });
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();
    }

    private async Task<Transaction> CreateTransactionAsync(
        TransactionStatus status,
        Guid? buyerId = null,
        BuyerIdentificationMethod method = BuyerIdentificationMethod.STEAM_ID,
        string? inviteToken = null,
        string? buyerTradeUrl = null,
        bool sellerConfirmed = false,
        bool baselineCaptured = false)
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
            InviteToken = method == BuyerIdentificationMethod.OPEN_LINK
                ? (inviteToken ?? "T46-detail-test")
                : null,
            ItemAssetId = Guid.NewGuid().ToString("N")[..12],
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
            BuyerTradeUrl = buyerTradeUrl,
            PaymentTimeoutMinutes = 1440,
            AcceptDeadline = status == TransactionStatus.CREATED ? nowUtc.AddHours(1) : null,
            AcceptedAt = status == TransactionStatus.ACCEPTED ? nowUtc.AddMinutes(-5) : null,
            // T123 — the payment-block gate reads this stamp (03 §2.3 step 4).
            SellerReadyConfirmedAt = sellerConfirmed ? nowUtc.AddMinutes(-3) : null,
            // T135 — the buyerInventoryVisible projection reads this column's
            // NULL-ness (06 §3.5). Left NULL when the readiness read failed.
            BuyerBaselineCapturedAt = baselineCaptured ? nowUtc.AddMinutes(-3) : null,
            BuyerBaselineClassCount = baselineCaptured ? 0 : null,
            // CK_Transactions_Cancel — CANCELLED_* requires these three.
            CancelledBy = IsCancelledStatus(status) ? CancelledByType.ADMIN : null,
            CancelReason = IsCancelledStatus(status) ? "admin cancel (test)" : null,
            CancelledAt = IsCancelledStatus(status) ? nowUtc.AddMinutes(-2) : null,
        };
        Context.Set<Transaction>().Add(transaction);
        await Context.SaveChangesAsync();
        return transaction;
    }

    private TransactionDetailService BuildSut() => new(Context, _clock);

    // v3.0 — the platform creates no trade offers, so there is no offer to look
    // up. In PAYMENT_RECEIVED the seller's CTA is the buyer's own trade URL,
    // read straight off the transaction (04 §7, 02 §2.2 step 6).

    [Fact]
    public async Task SteamTradeOfferUrl_Surfaces_BuyerTradeUrl_To_Seller_In_PaymentReceived()
    {
        var transaction = await CreateTransactionAsync(
            TransactionStatus.PAYMENT_RECEIVED,
            buyerId: _buyer.Id,
            buyerTradeUrl: "https://steamcommunity.com/tradeoffer/new/?partner=9988&token=xyz");

        var outcome = await BuildSut()
            .GetAsync(transaction.Id, _seller.Id, SellerSteamId, CancellationToken.None);

        Assert.Equal(TransactionDetailStatus.Found, outcome.Status);
        Assert.Equal(
            "https://steamcommunity.com/tradeoffer/new/?partner=9988&token=xyz",
            outcome.Body!.SteamTradeOfferUrl);
    }

    [Fact]
    public async Task SteamTradeOfferUrl_Hidden_From_Buyer_In_PaymentReceived()
    {
        // The buyer has nothing to do on Steam in this state; surfacing their
        // own trade URL back to them would be noise at best.
        var transaction = await CreateTransactionAsync(
            TransactionStatus.PAYMENT_RECEIVED,
            buyerId: _buyer.Id,
            buyerTradeUrl: "https://steamcommunity.com/tradeoffer/new/?partner=7766&token=abc");

        var outcome = await BuildSut()
            .GetAsync(transaction.Id, _buyer.Id, BuyerSteamId, CancellationToken.None);

        Assert.Null(outcome.Body!.SteamTradeOfferUrl);
    }

    [Fact]
    public async Task SteamTradeOfferUrl_Null_For_State_Without_A_Pending_Delivery()
    {
        var transaction = await CreateTransactionAsync(
            TransactionStatus.ACCEPTED,
            buyerId: _buyer.Id,
            buyerTradeUrl: "https://steamcommunity.com/tradeoffer/new/?partner=1&token=t");

        var outcome = await BuildSut()
            .GetAsync(transaction.Id, _seller.Id, SellerSteamId, CancellationToken.None);

        Assert.Null(outcome.Body!.SteamTradeOfferUrl);
    }

    // ---------- T135: buyerInventoryVisible (07 §7.5, 03 §2.3 step 3) ----------
    //
    // The flag tells both parties whether the 02 §9.2 inventory-evidence path
    // is open. It is a projection of BuyerBaselineCapturedAt's NULL-ness, gated
    // on the SAME milestone stamp as the sibling `payment` block, so the four
    // cases below mirror the T123 payment-block cases deliberately.

    [Theory]
    [InlineData(TransactionStatus.CREATED)]
    [InlineData(TransactionStatus.ACCEPTED)]
    public async Task BuyerInventoryVisible_Is_Unknown_Before_The_Seller_Confirms_Readiness(
        TransactionStatus status)
    {
        // "Not read yet" must not be reported as "visible": the baseline read
        // happens inside confirm-ready, so before that stamp there is no answer
        // and the field is suppressed entirely (07 §7.5).
        var transaction = await CreateTransactionAsync(status, buyerId: _buyer.Id);

        var outcome = await BuildSut().GetAsync(
            transaction.Id, _seller.Id, SellerSteamId, CancellationToken.None);

        Assert.Null(outcome.Body!.BuyerInventoryVisible);
    }

    [Fact]
    public async Task BuyerInventoryVisible_Is_True_When_The_Baseline_Was_Captured()
    {
        var transaction = await CreateTransactionAsync(
            TransactionStatus.SELLER_CONFIRMED,
            buyerId: _buyer.Id,
            sellerConfirmed: true,
            baselineCaptured: true);

        var outcome = await BuildSut().GetAsync(
            transaction.Id, _seller.Id, SellerSteamId, CancellationToken.None);

        Assert.True(outcome.Body!.BuyerInventoryVisible);
    }

    [Fact]
    public async Task BuyerInventoryVisible_Is_False_When_The_Baseline_Read_Failed()
    {
        // 06 §3.5: a NULL BuyerBaselineCapturedAt IS the signal that the
        // evidence path is closed — the seller confirmed, the read did not land.
        var transaction = await CreateTransactionAsync(
            TransactionStatus.SELLER_CONFIRMED,
            buyerId: _buyer.Id,
            sellerConfirmed: true,
            baselineCaptured: false);

        var outcome = await BuildSut().GetAsync(
            transaction.Id, _seller.Id, SellerSteamId, CancellationToken.None);

        Assert.False(outcome.Body!.BuyerInventoryVisible);
    }

    [Fact]
    public async Task BuyerInventoryVisible_Reaches_The_Buyer_Too()
    {
        // 03 §3.5 — the obligation created by a false flag ("only your own
        // 'Teslim Aldım' can prove delivery") is the BUYER's, and the buyer
        // never sees the §7.6a confirm-ready reply the seller got it from.
        var transaction = await CreateTransactionAsync(
            TransactionStatus.PAYMENT_RECEIVED,
            buyerId: _buyer.Id,
            sellerConfirmed: true,
            baselineCaptured: false);

        var outcome = await BuildSut().GetAsync(
            transaction.Id, _buyer.Id, BuyerSteamId, CancellationToken.None);

        Assert.False(outcome.Body!.BuyerInventoryVisible);
    }

    [Fact]
    public async Task BuyerInventoryVisible_Survives_A_Cancellation_That_Happened_After_The_Read()
    {
        // Same rule as the payment block: the read really did happen, and a
        // dispute or an admin may still need to know which evidence path
        // existed. A status-set gate would erase that.
        var transaction = await CreateTransactionAsync(
            TransactionStatus.CANCELLED_TIMEOUT,
            buyerId: _buyer.Id,
            sellerConfirmed: true,
            baselineCaptured: true);

        var outcome = await BuildSut().GetAsync(
            transaction.Id, _seller.Id, SellerSteamId, CancellationToken.None);

        Assert.True(outcome.Body!.BuyerInventoryVisible);
    }

    [Fact]
    public async Task BuyerInventoryVisible_Is_Never_Shown_To_A_Public_Viewer()
    {
        // A visitor who is not a party carries no delivery obligation, and the
        // readability of a stranger's Steam inventory is not theirs to learn.
        var transaction = await CreateTransactionAsync(
            TransactionStatus.SELLER_CONFIRMED,
            buyerId: _buyer.Id,
            sellerConfirmed: true,
            baselineCaptured: true);

        var outcome = await BuildSut().GetAsync(
            transaction.Id, callerId: null, callerSteamId: null, CancellationToken.None);

        Assert.Null(outcome.Body!.BuyerInventoryVisible);
    }

    [Fact]
    public async Task BuyerInventoryVisible_Is_Hidden_From_A_Stranger_Holding_A_Spent_Invite()
    {
        // The reachable stranger case, and the one with real stakes: the invite
        // path hands a non-party authenticated caller `role = null` once the
        // invite is spent (GetByInviteTokenAsync's final else). Unlike a
        // prospective buyer — only possible while the transaction is still
        // CREATED, so its baseline has never been read — a spent invite can point
        // at a transaction that passed the readiness milestone long ago. This is
        // therefore the one path where a stranger holding a stale link could
        // otherwise learn whether the buyer's Steam inventory is readable.
        //
        // What protects it is BuildResponseAsync's `if (role is null) return
        // BuildPublicResponse(...)`, not the `role is null` clause inside
        // BuildBuyerInventoryVisible — a mutation probe showed that clause is
        // unreachable defence-in-depth, kept only to mirror its two siblings
        // (BuildPaymentAsync, BuildSellerPayoutAsync). The assertion below is
        // about the OUTCOME, which is what the caller actually gets.
        var transaction = await CreateTransactionAsync(
            TransactionStatus.SELLER_CONFIRMED,
            buyerId: _buyer.Id,
            method: BuyerIdentificationMethod.OPEN_LINK,
            inviteToken: "inv-spent-token",
            sellerConfirmed: true,
            baselineCaptured: true);

        var stranger = Guid.NewGuid();
        var outcome = await BuildSut().GetByInviteTokenAsync(
            "inv-spent-token", stranger, callerSteamId: null, CancellationToken.None);

        Assert.Equal(TransactionDetailStatus.Found, outcome.Status);
        // Guard rails for the setup itself: if either of these drifts the test
        // would stop exercising the intended path while still passing.
        Assert.Null(outcome.Body!.UserRole);
        Assert.Null(outcome.Body.BuyerInventoryVisible);
    }
}
