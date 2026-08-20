using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Skinora.Shared.BackgroundJobs;
using Skinora.Shared.Domain;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Interfaces;
using Skinora.Shared.Persistence;
using Skinora.Shared.Tests.Integration;
using Skinora.Transactions.Application.Lifecycle;
using Skinora.Transactions.Application.Steam;
using Skinora.Transactions.Application.Timeouts;
using Skinora.Transactions.Domain.Entities;
using Skinora.Transactions.Infrastructure.Persistence;
using Skinora.Users.Application.Settings;
using Skinora.Users.Domain.Entities;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.Transactions.Tests.Integration.Lifecycle;

/// <summary>
/// T123 — end-to-end coverage for <see cref="TransactionReadinessService"/>
/// (07 §7.6a, 03 §2.3). Covers the three gates, the ACCEPTED → SELLER_CONFIRMED
/// transition, the payment window, the 02 §9.2 delivery baseline and the
/// per-rejection error codes.
/// </summary>
public class TransactionReadinessServiceTests : IntegrationTestBase
{
    static TransactionReadinessServiceTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        TransactionsModuleDbRegistration.RegisterTransactionsModule();
        Skinora.Platform.Infrastructure.Persistence.PlatformModuleDbRegistration.RegisterPlatformModule();
    }

    private const string ValidWallet1 = "TXyzABCDEFGHJKLMNPQRSTUVWXYZ234567";
    private const string ValidWallet2 = "TabcDEFGHJKLMNPQRSTUVWXYZ234567Xyz";
    private const string SellerSteamId = "76561198000000090";
    private const string BuyerSteamId = "76561198000000091";
    private const string ItemAssetId = "27348562891";
    private const string ItemClassId = "310776959";
    private const string ItemInstanceId = "188530139";
    private const ulong SteamId64ToId32Offset = 76561197960265728UL;

    private static readonly string BuyerTradeUrl =
        $"https://steamcommunity.com/tradeoffer/new/?partner={ulong.Parse(BuyerSteamId) - SteamId64ToId32Offset}&token=AbCdEfGh";

    private User _seller = null!;
    private User _buyer = null!;
    private FakeTimeProvider _clock = null!;
    private RecordingOutboxService _outbox = null!;
    private FakeSteamInventoryReader _inventory = null!;
    private CountingTradeHoldChecker _tradeHold = null!;
    private RecordingJobScheduler _scheduler = null!;

    protected override async Task SeedAsync(AppDbContext context)
    {
        _seller = new User
        {
            Id = Guid.NewGuid(),
            SteamId = SellerSteamId,
            SteamDisplayName = "Seller",
            DefaultPayoutAddress = ValidWallet1,
            MobileAuthenticatorVerified = true,
        };
        _buyer = new User
        {
            Id = Guid.NewGuid(),
            SteamId = BuyerSteamId,
            SteamDisplayName = "Buyer",
            MobileAuthenticatorVerified = true,
        };
        context.Set<User>().AddRange(_seller, _buyer);
        await context.SaveChangesAsync();

        _clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 13, 12, 0, 0, TimeSpan.Zero));
        _outbox = new RecordingOutboxService();
        _tradeHold = new CountingTradeHoldChecker(new TradeHoldResult(true, true, null));
        _scheduler = new RecordingJobScheduler();

        // Default world: the seller still holds the listed, tradeable item and
        // the buyer holds nothing of that class.
        _inventory = new FakeSteamInventoryReader();
        _inventory.Register(SellerSteamId, ItemSnapshot(ItemAssetId, tradeable: true));
    }

    // ---------- Happy path ----------

    [Fact]
    public async Task Happy_Path_Transitions_To_SellerConfirmed_And_Opens_Payment_Window()
    {
        var transaction = await CreateAcceptedTransactionAsync();
        var nowUtc = _clock.GetUtcNow().UtcDateTime;

        var outcome = await BuildSut().ConfirmReadyAsync(
            _seller.Id, transaction.Id, CancellationToken.None);

        Assert.Equal(ConfirmReadyStatus.Confirmed, outcome.Status);
        Assert.NotNull(outcome.Body);
        Assert.Equal(TransactionStatus.SELLER_CONFIRMED, outcome.Body!.Status);
        Assert.Equal(nowUtc, outcome.Body.SellerReadyConfirmedAt);
        Assert.Equal(nowUtc.AddMinutes(1440), outcome.Body.PaymentDeadline);
        Assert.True(outcome.Body.BuyerInventoryVisible);

        var persisted = await Context.Set<Transaction>().AsNoTracking()
            .SingleAsync(t => t.Id == transaction.Id);
        Assert.Equal(TransactionStatus.SELLER_CONFIRMED, persisted.Status);
        // 06 §3.5 — NOT NULL from SELLER_CONFIRMED onwards.
        Assert.Equal(nowUtc, persisted.SellerReadyConfirmedAt);
        Assert.Equal(nowUtc.AddMinutes(1440), persisted.PaymentDeadline);
    }

    [Fact]
    public async Task Happy_Path_Publishes_The_StatusChanged_Event_WP19_Consumes()
    {
        // The buyer's PAYMENT_WINDOW_OPEN notification (03 §3.4 step 1) rides
        // this event; T117 deleted its custodial producer and left the consumer
        // with none. This is that producer.
        var transaction = await CreateAcceptedTransactionAsync();

        await BuildSut().ConfirmReadyAsync(_seller.Id, transaction.Id, CancellationToken.None);

        var evt = Assert.IsType<TransactionStatusChangedEvent>(Assert.Single(_outbox.Published));
        Assert.Equal(transaction.Id, evt.TransactionId);
        Assert.Equal(TransactionStatus.ACCEPTED, evt.FromStatus);
        Assert.Equal(TransactionStatus.SELLER_CONFIRMED, evt.ToStatus);
    }

    [Fact]
    public async Task Happy_Path_Arms_The_Payment_Monitor_When_A_Deposit_Address_Exists()
    {
        // T139 — the deposit address had existed since creation but nothing was
        // watching it: the sidecar's active monitor had no backend caller at
        // all, so a real buyer's transfer never produced payment-detected and
        // the transaction sat in SELLER_CONFIRMED until it timed out. This
        // event is that caller.
        var transaction = await CreateAcceptedTransactionAsync();
        var address = await SeedPaymentAddressAsync(transaction);

        await BuildSut().ConfirmReadyAsync(_seller.Id, transaction.Id, CancellationToken.None);

        var evt = Assert.IsType<PaymentMonitorStartRequestedEvent>(
            Assert.Single(_outbox.Published, e => e is PaymentMonitorStartRequestedEvent));
        Assert.Equal(transaction.Id, evt.TransactionId);
        Assert.Equal(address.Id, evt.PaymentAddressId);
        Assert.Equal(address.Address, evt.Address);
        Assert.Equal(StablecoinType.USDT, evt.ExpectedToken);
        Assert.Equal("TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t", evt.ExpectedContractAddress);
    }

    [Fact]
    public async Task Arming_Rides_The_Same_Unit_Of_Work_As_The_Transition()
    {
        // Both events must be in the same outbox batch as the state change: a
        // rolled-back confirmation must not leave a monitor armed on an address
        // the buyer was never told about (09 §13.3).
        var transaction = await CreateAcceptedTransactionAsync();
        await SeedPaymentAddressAsync(transaction);

        await BuildSut().ConfirmReadyAsync(_seller.Id, transaction.Id, CancellationToken.None);

        Assert.Equal(2, _outbox.Published.Count);
        Assert.Contains(_outbox.Published, e => e is TransactionStatusChangedEvent);
        Assert.Contains(_outbox.Published, e => e is PaymentMonitorStartRequestedEvent);
    }

    [Fact]
    public async Task A_Missing_Deposit_Address_Does_Not_Block_The_Confirmation()
    {
        // Allocation is best-effort at creation and swept by
        // EnsurePaymentAddressJob; EnsurePaymentMonitorJob arms whatever exists
        // a minute later. Blocking the seller here would punish them for a
        // sidecar outage whose only effect is on the buyer's ability to pay.
        var transaction = await CreateAcceptedTransactionAsync();

        var outcome = await BuildSut().ConfirmReadyAsync(
            _seller.Id, transaction.Id, CancellationToken.None);

        Assert.Equal(ConfirmReadyStatus.Confirmed, outcome.Status);
        Assert.DoesNotContain(_outbox.Published, e => e is PaymentMonitorStartRequestedEvent);
    }

    private async Task<PaymentAddress> SeedPaymentAddressAsync(Transaction transaction)
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var address = new PaymentAddress
        {
            Id = Guid.NewGuid(),
            TransactionId = transaction.Id,
            Address = "TReadinessDepositAddrFakeFakeFake1",
            HdWalletIndex = 7100,
            ExpectedAmount = transaction.TotalAmount,
            ExpectedToken = transaction.StablecoinType,
            MonitoringStatus = MonitoringStatus.ACTIVE,
            CreatedAt = nowUtc,
            UpdatedAt = nowUtc,
        };
        Context.Set<PaymentAddress>().Add(address);
        await Context.SaveChangesAsync();
        return address;
    }

    [Fact]
    public async Task Happy_Path_Writes_A_History_Row_Attributed_To_The_Seller()
    {
        var transaction = await CreateAcceptedTransactionAsync();

        await BuildSut().ConfirmReadyAsync(_seller.Id, transaction.Id, CancellationToken.None);

        var history = await Context.Set<TransactionHistory>().AsNoTracking()
            .Where(h => h.TransactionId == transaction.Id)
            .SingleAsync(h => h.NewStatus == TransactionStatus.SELLER_CONFIRMED);
        Assert.Equal(TransactionStatus.ACCEPTED, history.PreviousStatus);
        Assert.Equal(ActorType.USER, history.ActorType);
        Assert.Equal(_seller.Id, history.ActorId);
    }

    [Fact]
    public async Task Happy_Path_Schedules_The_Payment_Timeout_Job()
    {
        // First production caller SchedulePaymentTimeoutAsync has ever had: the
        // custodial leg that used to arm the payment window died with T117.
        var transaction = await CreateAcceptedTransactionAsync();

        await BuildSut().ConfirmReadyAsync(_seller.Id, transaction.Id, CancellationToken.None);

        Assert.NotEmpty(_scheduler.Scheduled);
        var persisted = await Context.Set<Transaction>().AsNoTracking()
            .SingleAsync(t => t.Id == transaction.Id);
        // Committed in the SAME SaveChanges as the transition (09 §13.3).
        Assert.False(string.IsNullOrEmpty(persisted.PaymentTimeoutJobId));
    }

    // ---------- AC: cache-free inventory read ----------

    [Fact]
    public async Task Item_Check_And_Baseline_Both_Bypass_The_Sidecar_Cache()
    {
        // 03 §2.3 md.1 / 07 §7.6a: "envanter ÖNBELLEKSİZ okunur". A 120-second
        // cache entry can still show an item traded away ninety seconds ago,
        // and a stale baseline silently omits items the buyer just acquired —
        // which would later read as a delivery that never happened.
        var transaction = await CreateAcceptedTransactionAsync();

        await BuildSut().ConfirmReadyAsync(_seller.Id, transaction.Id, CancellationToken.None);

        Assert.Equal([InventoryReadFreshness.Fresh], _inventory.ItemReadFreshness);
        Assert.Equal([InventoryReadFreshness.Fresh], _inventory.BaselineReadFreshness);
    }

    // ---------- AC: ITEM_NO_LONGER_AVAILABLE ----------

    [Fact]
    public async Task Item_Gone_From_Inventory_Returns_ItemNoLongerAvailable()
    {
        var transaction = await CreateAcceptedTransactionAsync();
        _inventory = new FakeSteamInventoryReader(); // seller holds nothing

        var outcome = await BuildSut().ConfirmReadyAsync(
            _seller.Id, transaction.Id, CancellationToken.None);

        Assert.Equal(ConfirmReadyStatus.ItemNoLongerAvailable, outcome.Status);
        Assert.Equal(TransactionErrorCodes.ItemNoLongerAvailable, outcome.ErrorCode);
        await AssertUnchangedAsync(transaction.Id);
    }

    [Fact]
    public async Task Item_No_Longer_Tradeable_Returns_ItemNoLongerAvailable()
    {
        // 07 §7.6a defines the code as "item envanterde yok VEYA artık tradeable
        // değil" — from the buyer's side the two are equally undeliverable.
        var transaction = await CreateAcceptedTransactionAsync();
        _inventory = new FakeSteamInventoryReader();
        _inventory.Register(SellerSteamId, ItemSnapshot(ItemAssetId, tradeable: false));

        var outcome = await BuildSut().ConfirmReadyAsync(
            _seller.Id, transaction.Id, CancellationToken.None);

        Assert.Equal(ConfirmReadyStatus.ItemNoLongerAvailable, outcome.Status);
        await AssertUnchangedAsync(transaction.Id);
    }

    [Fact]
    public async Task Seller_Inventory_Private_Is_Not_Reported_As_Item_Gone()
    {
        // The T121 rule applied to this endpoint: a hidden profile says nothing
        // about the asset. Reporting it as ITEM_NO_LONGER_AVAILABLE would send
        // a seller to hunt for an item sitting untouched in their inventory,
        // and 409 would assert a fact the platform never observed.
        var transaction = await CreateAcceptedTransactionAsync();
        _inventory.ForcedVisibility = InventoryVisibility.Private;

        var outcome = await BuildSut().ConfirmReadyAsync(
            _seller.Id, transaction.Id, CancellationToken.None);

        Assert.Equal(ConfirmReadyStatus.InventoryPrivate, outcome.Status);
        Assert.Equal(TransactionErrorCodes.InventoryPrivate, outcome.ErrorCode);
        Assert.NotEqual(ConfirmReadyStatus.ItemNoLongerAvailable, outcome.Status);
        await AssertUnchangedAsync(transaction.Id);
    }

    [Fact]
    public async Task Seller_Inventory_Unreachable_Fails_Closed_With_SteamUnavailable()
    {
        var transaction = await CreateAcceptedTransactionAsync();
        _inventory.ForcedVisibility = InventoryVisibility.Unavailable;

        var outcome = await BuildSut().ConfirmReadyAsync(
            _seller.Id, transaction.Id, CancellationToken.None);

        Assert.Equal(ConfirmReadyStatus.SteamUnavailable, outcome.Status);
        Assert.Equal(TransactionErrorCodes.SteamUnavailable, outcome.ErrorCode);
        await AssertUnchangedAsync(transaction.Id);
    }

    [Fact]
    public async Task The_Three_Unreadable_Outcomes_Map_To_Three_Distinct_Codes()
    {
        // Guard against a future refactor collapsing them back onto one code —
        // the exact regression T121 removed one layer down.
        var gone = await RunWithInventoryAsync(
            "27348562801", _ => new FakeSteamInventoryReader());
        var hidden = await RunWithInventoryAsync("27348562802", inv =>
        {
            inv.ForcedVisibility = InventoryVisibility.Private;
            return inv;
        });
        var down = await RunWithInventoryAsync("27348562803", inv =>
        {
            inv.ForcedVisibility = InventoryVisibility.Unavailable;
            return inv;
        });

        Assert.Equal(TransactionErrorCodes.ItemNoLongerAvailable, gone.ErrorCode);
        Assert.Equal(TransactionErrorCodes.InventoryPrivate, hidden.ErrorCode);
        Assert.Equal(TransactionErrorCodes.SteamUnavailable, down.ErrorCode);
        Assert.Equal(3, new[] { gone.ErrorCode, hidden.ErrorCode, down.ErrorCode }.Distinct().Count());
    }

    // ---------- AC: buyer Mobile Authenticator ----------

    [Fact]
    public async Task Buyer_Mobile_Authenticator_Inactive_Returns_Its_Own_Code()
    {
        // NOT MOBILE_AUTHENTICATOR_REQUIRED: the caller here is the seller and
        // the fix belongs to the buyer, so a shared code would render as
        // "enable your authenticator" to the wrong person (07 §7.6a).
        var transaction = await CreateAcceptedTransactionAsync();
        _tradeHold = new CountingTradeHoldChecker(new TradeHoldResult(true, false, null));

        var outcome = await BuildSut().ConfirmReadyAsync(
            _seller.Id, transaction.Id, CancellationToken.None);

        Assert.Equal(ConfirmReadyStatus.BuyerMobileAuthenticatorInactive, outcome.Status);
        Assert.Equal(TransactionErrorCodes.BuyerMobileAuthenticatorInactive, outcome.ErrorCode);
        Assert.NotEqual(TransactionErrorCodes.MobileAuthenticatorRequired, outcome.ErrorCode);
        await AssertUnchangedAsync(transaction.Id);
    }

    [Fact]
    public async Task Trade_Hold_Probe_Unavailable_Fails_Closed()
    {
        var transaction = await CreateAcceptedTransactionAsync();
        _tradeHold = new CountingTradeHoldChecker(new TradeHoldResult(false, false, null));

        var outcome = await BuildSut().ConfirmReadyAsync(
            _seller.Id, transaction.Id, CancellationToken.None);

        Assert.Equal(ConfirmReadyStatus.SteamUnavailable, outcome.Status);
        await AssertUnchangedAsync(transaction.Id);
    }

    [Fact]
    public async Task Trade_Hold_Probe_Uses_The_Buyers_Own_Stored_Trade_Url_Token()
    {
        // The probe must answer for the same pair the delivery will use: the
        // buyer's SteamID and the token from the URL fixed at accept time.
        var transaction = await CreateAcceptedTransactionAsync();

        await BuildSut().ConfirmReadyAsync(_seller.Id, transaction.Id, CancellationToken.None);

        Assert.Equal(1, _tradeHold.CallCount);
        Assert.Equal(BuyerSteamId, _tradeHold.LastSteamId);
        Assert.Equal("AbCdEfGh", _tradeHold.LastAccessToken);
    }

    [Fact]
    public async Task Item_Check_Failure_Never_Spends_The_Trade_Hold_Round_Trip()
    {
        // 07 §7.6a orders the gates cheapest-failure-first, and every gate is a
        // Steam call queued at 1 req/s.
        var transaction = await CreateAcceptedTransactionAsync();
        _inventory = new FakeSteamInventoryReader();

        await BuildSut().ConfirmReadyAsync(_seller.Id, transaction.Id, CancellationToken.None);

        Assert.Equal(0, _tradeHold.CallCount);
    }

    // ---------- AC: baseline ----------

    [Fact]
    public async Task Baseline_Counts_Existing_Copies_Of_The_Item_Class()
    {
        // 02 §9.2: matching is by COUNT, not presence. T122 measured a real
        // inventory holding 9 copies of one class — a presence check would
        // never see a delivery into it.
        var transaction = await CreateAcceptedTransactionAsync();
        _inventory.Register(BuyerSteamId, ItemSnapshot("99999999901", tradeable: true));
        _inventory.Register(BuyerSteamId, ItemSnapshot("99999999902", tradeable: true));

        var outcome = await BuildSut().ConfirmReadyAsync(
            _seller.Id, transaction.Id, CancellationToken.None);

        Assert.Equal(ConfirmReadyStatus.Confirmed, outcome.Status);
        var persisted = await Context.Set<Transaction>().AsNoTracking()
            .SingleAsync(t => t.Id == transaction.Id);
        Assert.Equal(2, persisted.BuyerBaselineClassCount);
        var storedAssetIds = JsonSerializer.Deserialize<string[]>(persisted.BuyerBaselineAssetIds!);
        Assert.NotNull(storedAssetIds);
        Assert.Equal(["99999999901", "99999999902"], storedAssetIds);
        Assert.NotNull(persisted.BuyerBaselineCapturedAt);
    }

    [Fact]
    public async Task Baseline_Of_Zero_Is_Recorded_As_Evidence_Not_As_Absence()
    {
        // A buyer who owns none of the skin yet has a perfectly good baseline:
        // count 0, captured. This must be distinguishable from "we could not
        // look" — the whole point of BuyerBaselineCapturedAt.
        var transaction = await CreateAcceptedTransactionAsync();

        await BuildSut().ConfirmReadyAsync(_seller.Id, transaction.Id, CancellationToken.None);

        var persisted = await Context.Set<Transaction>().AsNoTracking()
            .SingleAsync(t => t.Id == transaction.Id);
        Assert.Equal(0, persisted.BuyerBaselineClassCount);
        Assert.NotNull(persisted.BuyerBaselineCapturedAt);
    }

    [Theory]
    [InlineData(InventoryVisibility.Private)]
    [InlineData(InventoryVisibility.Unavailable)]
    public async Task Unreadable_Buyer_Inventory_Does_Not_Block_The_Transaction(
        InventoryVisibility visibility)
    {
        // 03 §2.3 md.3 / 02 §9.2 — the transaction advances; only the inventory
        // evidence path closes, leaving buyer confirmation as the sole route.
        var transaction = await CreateAcceptedTransactionAsync();
        _inventory.ForcedBaselineVisibility = visibility;

        var outcome = await BuildSut().ConfirmReadyAsync(
            _seller.Id, transaction.Id, CancellationToken.None);

        Assert.Equal(ConfirmReadyStatus.Confirmed, outcome.Status);
        Assert.False(outcome.Body!.BuyerInventoryVisible);

        var persisted = await Context.Set<Transaction>().AsNoTracking()
            .SingleAsync(t => t.Id == transaction.Id);
        Assert.Equal(TransactionStatus.SELLER_CONFIRMED, persisted.Status);

        // The three columns stay NULL. Writing zeros would make an unread
        // inventory indistinguishable from an empty one, and the later delta
        // would read every pre-existing copy as a fresh delivery.
        Assert.Null(persisted.BuyerBaselineClassCount);
        Assert.Null(persisted.BuyerBaselineAssetIds);
        Assert.Null(persisted.BuyerBaselineCapturedAt);
    }

    [Fact]
    public async Task Oversized_Baseline_Asset_List_Is_Truncated_But_Count_Stays_Exact()
    {
        // 06 §3.5 caps the column at 400 chars. A whale's inventory must not
        // turn a legitimate confirmation into a 500 — and the truncation must
        // not touch ClassCount, which is what 02 §9.2 actually decides on.
        var transaction = await CreateAcceptedTransactionAsync();
        for (var i = 0; i < 60; i++)
            _inventory.Register(BuyerSteamId, ItemSnapshot($"9999999{i:D4}", tradeable: true));

        var outcome = await BuildSut().ConfirmReadyAsync(
            _seller.Id, transaction.Id, CancellationToken.None);

        Assert.Equal(ConfirmReadyStatus.Confirmed, outcome.Status);
        var persisted = await Context.Set<Transaction>().AsNoTracking()
            .SingleAsync(t => t.Id == transaction.Id);
        Assert.Equal(60, persisted.BuyerBaselineClassCount);
        Assert.True(persisted.BuyerBaselineAssetIds!.Length <= 400);
        var stored = JsonSerializer.Deserialize<string[]>(persisted.BuyerBaselineAssetIds)!;
        Assert.InRange(stored.Length, 1, 59);
    }

    // ---------- Guards ----------

    [Fact]
    public async Task Buyer_Cannot_Confirm_Readiness()
    {
        var transaction = await CreateAcceptedTransactionAsync();

        var outcome = await BuildSut().ConfirmReadyAsync(
            _buyer.Id, transaction.Id, CancellationToken.None);

        Assert.Equal(ConfirmReadyStatus.NotAParty, outcome.Status);
        Assert.Equal(TransactionErrorCodes.NotAParty, outcome.ErrorCode);
        await AssertUnchangedAsync(transaction.Id);
    }

    [Theory]
    [InlineData(TransactionStatus.CREATED)]
    [InlineData(TransactionStatus.SELLER_CONFIRMED)]
    [InlineData(TransactionStatus.PAYMENT_RECEIVED)]
    [InlineData(TransactionStatus.COMPLETED)]
    public async Task Only_ACCEPTED_Can_Confirm_Readiness(TransactionStatus status)
    {
        var transaction = await CreateAcceptedTransactionAsync(status);

        var outcome = await BuildSut().ConfirmReadyAsync(
            _seller.Id, transaction.Id, CancellationToken.None);

        Assert.Equal(ConfirmReadyStatus.InvalidStateTransition, outcome.Status);
        Assert.Equal(TransactionErrorCodes.InvalidStateTransition, outcome.ErrorCode);
    }

    [Fact]
    public async Task Second_Call_Is_Rejected_Rather_Than_Re_Opening_The_Window()
    {
        // Not idempotent by design (unlike confirm-receipt, 07 §7.6b): a second
        // confirmation would re-arm PaymentDeadline and hand the buyer a fresh
        // window they never earned, and re-take the baseline AFTER the seller
        // may already have sent — absorbing the delivery into the reference.
        var transaction = await CreateAcceptedTransactionAsync();
        var sut = BuildSut();
        await sut.ConfirmReadyAsync(_seller.Id, transaction.Id, CancellationToken.None);
        var firstDeadline = (await Context.Set<Transaction>().AsNoTracking()
            .SingleAsync(t => t.Id == transaction.Id)).PaymentDeadline;

        _clock.Advance(TimeSpan.FromMinutes(30));
        var second = await BuildSut().ConfirmReadyAsync(
            _seller.Id, transaction.Id, CancellationToken.None);

        Assert.Equal(ConfirmReadyStatus.InvalidStateTransition, second.Status);
        var persisted = await Context.Set<Transaction>().AsNoTracking()
            .SingleAsync(t => t.Id == transaction.Id);
        Assert.Equal(firstDeadline, persisted.PaymentDeadline);
    }

    [Fact]
    public async Task Emergency_Hold_Blocks_Confirmation_Without_Calling_Steam()
    {
        // 05 §4.5 freezes every trigger. The early exit also spares three Steam
        // round-trips for a transaction that cannot advance.
        var transaction = await CreateAcceptedTransactionAsync();
        await using (var ctx = CreateContext())
        {
            var tracked = await ctx.Set<Transaction>().SingleAsync(t => t.Id == transaction.Id);
            tracked.IsOnHold = true;
            tracked.EmergencyHoldAt = _clock.GetUtcNow().UtcDateTime;
            tracked.EmergencyHoldReason = "test";
            tracked.EmergencyHoldByAdminId = _seller.Id;
            tracked.TimeoutFrozenAt = _clock.GetUtcNow().UtcDateTime;
            tracked.TimeoutFreezeReason = TimeoutFreezeReason.EMERGENCY_HOLD;
            tracked.TimeoutRemainingSeconds = 600;
            await ctx.SaveChangesAsync();
        }

        var outcome = await BuildSut().ConfirmReadyAsync(
            _seller.Id, transaction.Id, CancellationToken.None);

        Assert.Equal(ConfirmReadyStatus.InvalidStateTransition, outcome.Status);
        Assert.Equal(0, _tradeHold.CallCount);
        Assert.Empty(_inventory.ItemReadFreshness);
    }

    [Fact]
    public async Task Unknown_Transaction_Returns_NotFound()
    {
        var outcome = await BuildSut().ConfirmReadyAsync(
            _seller.Id, Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(ConfirmReadyStatus.NotFound, outcome.Status);
        Assert.Equal(TransactionErrorCodes.TransactionNotFound, outcome.ErrorCode);
    }

    [Fact]
    public async Task Every_Rejection_Leaves_The_Outbox_Empty()
    {
        // A rejected confirmation must not tell the buyer to pay.
        var transaction = await CreateAcceptedTransactionAsync();
        _inventory = new FakeSteamInventoryReader();

        await BuildSut().ConfirmReadyAsync(_seller.Id, transaction.Id, CancellationToken.None);

        Assert.Empty(_outbox.Published);
    }

    // ---------- Helpers ----------

    /// <summary>
    /// Runs one confirm-ready against a freshly configured inventory. Each call
    /// gets its own asset id: <c>UQ_Transactions_SellerId_ItemAssetId_Active</c>
    /// forbids one seller holding two active listings of the same asset, so
    /// reusing the id would fail at insert rather than at the assertion.
    /// </summary>
    private async Task<ConfirmReadyOutcome> RunWithInventoryAsync(
        string assetId,
        Func<FakeSteamInventoryReader, FakeSteamInventoryReader> configure)
    {
        var transaction = await CreateAcceptedTransactionAsync(assetId: assetId);
        var reader = new FakeSteamInventoryReader();
        reader.Register(SellerSteamId, ItemSnapshot(assetId, tradeable: true));
        _inventory = configure(reader);
        return await BuildSut().ConfirmReadyAsync(
            _seller.Id, transaction.Id, CancellationToken.None);
    }

    private async Task AssertUnchangedAsync(Guid transactionId)
    {
        var persisted = await Context.Set<Transaction>().AsNoTracking()
            .SingleAsync(t => t.Id == transactionId);
        Assert.Equal(TransactionStatus.ACCEPTED, persisted.Status);
        Assert.Null(persisted.SellerReadyConfirmedAt);
        Assert.Null(persisted.PaymentDeadline);
        Assert.Null(persisted.BuyerBaselineCapturedAt);
    }

    private static InventoryItemSnapshot ItemSnapshot(string assetId, bool tradeable) =>
        new(
            AssetId: assetId,
            ClassId: ItemClassId,
            InstanceId: ItemInstanceId,
            Name: "AK-47 | Redline",
            MarketHashName: "AK-47 | Redline (Field-Tested)",
            IconUrl: null,
            Exterior: "Field-Tested",
            Type: "Rifle",
            InspectLink: null,
            IsTradeable: tradeable);

    private async Task<Transaction> CreateAcceptedTransactionAsync(
        TransactionStatus status = TransactionStatus.ACCEPTED,
        string assetId = ItemAssetId)
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var transaction = new Transaction
        {
            Id = Guid.NewGuid(),
            Status = status,
            SellerId = _seller.Id,
            BuyerId = _buyer.Id,
            BuyerIdentificationMethod = BuyerIdentificationMethod.STEAM_ID,
            TargetBuyerSteamId = BuyerSteamId,
            BuyerRefundAddress = ValidWallet2,
            BuyerTradeUrl = BuyerTradeUrl,
            ItemAssetId = assetId,
            ItemClassId = ItemClassId,
            ItemInstanceId = ItemInstanceId,
            ItemName = "AK-47 | Redline",
            StablecoinType = StablecoinType.USDT,
            Price = 100m,
            CommissionRate = 0.02m,
            CommissionAmount = 2m,
            TotalAmount = 102m,
            SellerPayoutAddress = ValidWallet1,
            PaymentTimeoutMinutes = 1440,
            AcceptedAt = status == TransactionStatus.CREATED ? null : nowUtc.AddMinutes(-5),
            SellerConfirmDeadline = status == TransactionStatus.ACCEPTED ? nowUtc.AddHours(1) : null,
            AcceptDeadline = status == TransactionStatus.CREATED ? nowUtc.AddHours(1) : null,
            // 06 §3.5 cumulative-milestone rule: states past SELLER_CONFIRMED
            // carry the stamp forward, so the fixture must too.
            SellerReadyConfirmedAt =
                status is TransactionStatus.SELLER_CONFIRMED or TransactionStatus.PAYMENT_RECEIVED
                    or TransactionStatus.COMPLETED
                    ? nowUtc.AddMinutes(-1)
                    : null,
        };
        Context.Set<Transaction>().Add(transaction);
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();
        return transaction;
    }

    private TransactionReadinessService BuildSut() =>
        new(
            Context,
            _inventory,
            // The real U17 parser, not a stub: the token this endpoint probes
            // with must come out of the same contract the accept endpoint wrote.
            new TradeUrlParser(),
            _tradeHold,
            new TimeoutSchedulingService(Context, _scheduler, _clock),
            _outbox,
            NullLogger<TransactionReadinessService>.Instance,
            _clock);

    private sealed class RecordingOutboxService : IOutboxService
    {
        public List<IDomainEvent> Published { get; } = [];

        public Task PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default)
        {
            Published.Add(domainEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class CountingTradeHoldChecker : ITradeHoldChecker
    {
        private readonly TradeHoldResult _result;
        public CountingTradeHoldChecker(TradeHoldResult result) => _result = result;

        public int CallCount { get; private set; }
        public string? LastSteamId { get; private set; }
        public string? LastAccessToken { get; private set; }

        public Task<TradeHoldResult> CheckAsync(
            string steamId64, string tradeOfferAccessToken, CancellationToken cancellationToken)
        {
            CallCount++;
            LastSteamId = steamId64;
            LastAccessToken = tradeOfferAccessToken;
            return Task.FromResult(_result);
        }
    }

    /// <summary>
    /// In-memory <see cref="IBackgroundJobScheduler"/> — hands back synthetic
    /// job ids so the SUT's "job ids commit with the transition" contract
    /// (09 §13.3) is observable without a Hangfire storage.
    /// </summary>
    private sealed class RecordingJobScheduler : IBackgroundJobScheduler
    {
        private int _next;

        public List<TimeSpan> Scheduled { get; } = [];

        public string Schedule<T>(System.Linq.Expressions.Expression<Action<T>> methodCall, TimeSpan delay)
        {
            Scheduled.Add(delay);
            return $"job-{++_next}";
        }

        public string Enqueue<T>(System.Linq.Expressions.Expression<Action<T>> methodCall)
            => $"job-{++_next}";

        public bool Delete(string jobId) => true;

        public void AddOrUpdateRecurring<T>(
            string jobId,
            System.Linq.Expressions.Expression<Action<T>> methodCall,
            string cronExpression)
        {
        }
    }
}
