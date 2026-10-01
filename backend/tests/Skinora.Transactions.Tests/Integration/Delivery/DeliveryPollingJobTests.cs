using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Skinora.Shared.Domain;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Interfaces;
using Skinora.Shared.Persistence;
using Skinora.Shared.Tests.Integration;
using Skinora.Transactions.Application.Delivery;
using Skinora.Transactions.Application.Settlement;
using Skinora.Transactions.Application.Steam;
using Skinora.Transactions.Domain.Entities;
using Skinora.Transactions.Infrastructure.Persistence;
using Skinora.Transactions.Tests.Integration.Lifecycle;
using Skinora.Users.Domain.Entities;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.Transactions.Tests.Integration.Delivery;

/// <summary>
/// P2P-DeliveryPollingJob (owner decision 2026-10-02) — the pre-deadline
/// delivery poll. Real engine, real database, fake Steam.
/// </summary>
/// <remarks>
/// <para>
/// The suite follows the poll's two promises. It acts only on evidence seen
/// together in one round — record and ask the buyer while the launch gate is
/// closed, deliver while it is open, and touch nothing for anything less. And it
/// is cheap: one seller read while the item has not moved, rows outside its
/// scope never read at all, a run that stops at the first unreadable seller.
/// </para>
/// </remarks>
public class DeliveryPollingJobTests : IntegrationTestBase
{
    static DeliveryPollingJobTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        TransactionsModuleDbRegistration.RegisterTransactionsModule();
        Skinora.Platform.Infrastructure.Persistence.PlatformModuleDbRegistration.RegisterPlatformModule();
    }

    private const string SellerSteamId = "76561198000000190";
    private const string BuyerSteamId = "76561198000000191";
    private const string AdminSteamId = "76561198000000192";
    private const string ItemAssetId = "27348562891";
    private const string ItemClassId = "310776959";
    private const string ItemInstanceId = "188530139";
    private const string ValidWallet1 = "TXyzABCDEFGHJKLMNPQRSTUVWXYZ234567";
    private const string ValidWallet2 = "TabcDEFGHJKLMNPQRSTUVWXYZ234567Xyz";
    private const ulong SteamId64ToId32Offset = 76561197960265728UL;

    // HasFieldsForAccepted — the DeliverItem guard re-checks the accept fields.
    private static readonly string BuyerTradeUrl =
        "https://steamcommunity.com/tradeoffer/new/"
        + $"?partner={ulong.Parse(BuyerSteamId) - SteamId64ToId32Offset}&token=AbCdEfGh";

    private User _seller = null!;
    private User _buyer = null!;
    private User _admin = null!;
    private FakeTimeProvider _clock = null!;
    private FakeSteamInventoryReader _inventory = null!;
    private RecordingOutboxService _outbox = null!;

    protected override async Task SeedAsync(AppDbContext context)
    {
        _seller = new User
        {
            Id = Guid.NewGuid(),
            SteamId = SellerSteamId,
            SteamDisplayName = "Seller",
            DefaultPayoutAddress = ValidWallet1,
        };
        _buyer = new User { Id = Guid.NewGuid(), SteamId = BuyerSteamId, SteamDisplayName = "Buyer" };
        _admin = new User { Id = Guid.NewGuid(), SteamId = AdminSteamId, SteamDisplayName = "Admin" };
        context.Set<User>().AddRange(_seller, _buyer, _admin);
        await context.SaveChangesAsync();

        _clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero));
        _inventory = new FakeSteamInventoryReader();
        _outbox = new RecordingOutboxService();
    }

    // ================= What the poll acts on =================

    [Fact]
    public async Task Seller_Still_Holding_The_Item_Costs_One_Read_And_Changes_Nothing_Else()
    {
        var transaction = await CreateAwaitingDeliveryAsync();
        RegisterSellerStillHoldsItem();
        RegisterBuyerCopies("99887766");   // a copy from elsewhere — must not matter

        var summary = await BuildSut().ExecuteAsync();

        Assert.Equal(new DeliveryPollingRunSummary(1, 0, 0), summary);
        // The whole round: one fresh seller read, no buyer read.
        Assert.Equal([InventoryReadFreshness.Fresh], _inventory.ItemReadFreshness);
        Assert.Empty(_inventory.BaselineReadFreshness);

        var persisted = await ReloadAsync(transaction.Id);
        Assert.Equal(TransactionStatus.PAYMENT_RECEIVED, persisted.Status);
        Assert.Equal(DeliveryEvidence.NONE, persisted.DeliveryEvidence);
        Assert.Equal(_clock.GetUtcNow().UtcDateTime, persisted.DeliveryPolledAt);
        Assert.Empty(_outbox.Published);
        Assert.Equal(0, await CaptureCountAsync(transaction.Id));
    }

    [Fact]
    public async Task Gate_Closed_Records_The_Evidence_And_Asks_The_Buyer_Exactly_Once()
    {
        var transaction = await CreateAwaitingDeliveryAsync();
        RegisterBuyerCopies("99887766");   // seller's asset unregistered ⇒ gone

        var summary = await BuildSut().ExecuteAsync();

        Assert.Equal(new DeliveryPollingRunSummary(1, 0, 1), summary);
        var persisted = await ReloadAsync(transaction.Id);
        // Money does not move on the inference: still awaiting the buyer.
        Assert.Equal(TransactionStatus.PAYMENT_RECEIVED, persisted.Status);
        Assert.Null(persisted.DeliveryVerifiedAt);
        Assert.Null(persisted.PayoutEligibleAt);
        Assert.Equal(
            DeliveryEvidence.SELLER_ASSET_GONE | DeliveryEvidence.INVENTORY_DELTA,
            persisted.DeliveryEvidence);

        // DEPLOY_RUNBOOK §H.3 reads this row; observed now, not at the deadline.
        var capture = await Context.Set<DeliveryEvidenceCapture>().AsNoTracking()
            .SingleAsync(c => c.TransactionId == transaction.Id);
        Assert.True(capture.AutoReleaseGated);
        Assert.Equal(nameof(DeliveryVerdict.InventoryEvidencePendingReview), capture.Verdict);
        Assert.Equal(_clock.GetUtcNow().UtcDateTime, capture.ObservedAt);

        // Every read that decided it asked Steam, not the sidecar cache.
        Assert.All(_inventory.ItemReadFreshness, f => Assert.Equal(InventoryReadFreshness.Fresh, f));
        Assert.Equal([InventoryReadFreshness.Fresh], _inventory.BaselineReadFreshness);

        var asked = Assert.IsType<DeliveryDetectedEvent>(Assert.Single(_outbox.Published));
        Assert.Equal(transaction.Id, asked.TransactionId);
        Assert.Equal(_buyer.Id, asked.BuyerId);
        Assert.Equal("AK-47 | Redline", asked.ItemName);

        // The recorded evidence is now sufficient, so the row leaves the poll's
        // scope: no second read, no second notification.
        _clock.Advance(TimeSpan.FromHours(1));
        var again = await BuildSut().ExecuteAsync();

        Assert.Equal(0, again.Examined);
        Assert.Single(_outbox.Published);
        Assert.Equal(1, await CaptureCountAsync(transaction.Id));
    }

    [Fact]
    public async Task Gate_Open_Delivers_Through_The_Transition_The_Timeout_Round_Uses()
    {
        await Context.ConfigureSettingAsync(DeliveryVerificationService.AutoReleaseSettingKey, "true");
        var transaction = await CreateAwaitingDeliveryAsync();
        RegisterBuyerCopies("99887766");

        var summary = await BuildSut().ExecuteAsync();

        Assert.Equal(new DeliveryPollingRunSummary(1, 1, 0), summary);
        var persisted = await ReloadAsync(transaction.Id);
        Assert.Equal(TransactionStatus.ITEM_DELIVERED, persisted.Status);
        Assert.Equal(_clock.GetUtcNow().UtcDateTime, persisted.DeliveryVerifiedAt);
        Assert.Equal(
            _clock.GetUtcNow().UtcDateTime.AddDays(SettlementSettingsProvider.DefaultSettlementDays),
            persisted.PayoutEligibleAt);
        Assert.Equal("99887766", persisted.DeliveredBuyerAssetId);
        Assert.Null(persisted.BuyerConfirmedReceiptAt);

        var history = await Context.Set<TransactionHistory>().AsNoTracking()
            .SingleAsync(h => h.TransactionId == transaction.Id);
        Assert.Equal(nameof(TransactionTrigger.DeliverItem), history.Trigger);
        Assert.Equal(ActorType.SYSTEM, history.ActorType);

        // The realtime relay is told; the buyer is not asked to confirm what
        // the platform has already concluded.
        var changed = Assert.IsType<TransactionStatusChangedEvent>(Assert.Single(_outbox.Published));
        Assert.Equal(TransactionStatus.ITEM_DELIVERED, changed.ToStatus);
    }

    [Fact]
    public async Task Item_Gone_From_The_Seller_But_Not_Yet_At_The_Buyer_Writes_Nothing()
    {
        // The misdelivery signature — or simply Steam's buyer side lagging
        // (B1 is unmeasured). Before the deadline the poll leaves it to the
        // dispute and deadline rounds.
        var transaction = await CreateAwaitingDeliveryAsync();

        var summary = await BuildSut().ExecuteAsync();

        Assert.Equal(new DeliveryPollingRunSummary(1, 0, 0), summary);
        var persisted = await ReloadAsync(transaction.Id);
        Assert.Equal(DeliveryEvidence.NONE, persisted.DeliveryEvidence);
        Assert.Equal(_clock.GetUtcNow().UtcDateTime, persisted.DeliveryPolledAt);
        Assert.Empty(_outbox.Published);
        Assert.Equal(0, await CaptureCountAsync(transaction.Id));
    }

    [Fact]
    public async Task A_Recorded_Half_Plus_A_Newly_Observed_Half_Is_Not_Enough()
    {
        // An earlier round recorded INVENTORY_DELTA alone (the buyer got a copy
        // from somewhere). This round sees the asset leave the seller but no new
        // copy at the buyer. The engine ORs both into "sufficient"; the poll
        // must not, or two unrelated observations would add up to a delivery.
        var transaction = await CreateAwaitingDeliveryAsync(
            evidence: DeliveryEvidence.INVENTORY_DELTA);

        var summary = await BuildSut().ExecuteAsync();

        Assert.Equal(new DeliveryPollingRunSummary(1, 0, 0), summary);
        var persisted = await ReloadAsync(transaction.Id);
        Assert.Equal(DeliveryEvidence.INVENTORY_DELTA, persisted.DeliveryEvidence);
        Assert.Empty(_outbox.Published);
        Assert.Equal(0, await CaptureCountAsync(transaction.Id));
    }

    // ================= What the poll never reads =================

    [Fact]
    public async Task An_Overdue_Delivery_Is_Left_To_The_Deadline_Scanner()
    {
        await CreateAwaitingDeliveryAsync(deadlineInMinutes: -1);

        var summary = await BuildSut().ExecuteAsync();

        Assert.Equal(0, summary.Examined);
        Assert.Empty(_inventory.ItemReadFreshness);
    }

    [Fact]
    public async Task A_Transaction_Without_A_Buyer_Baseline_Is_Never_Read()
    {
        // No baseline, no inventory evidence (02 §9.2) — a read could only
        // spend budget.
        await CreateAwaitingDeliveryAsync(withBaseline: false);

        var summary = await BuildSut().ExecuteAsync();

        Assert.Equal(0, summary.Examined);
        Assert.Empty(_inventory.ItemReadFreshness);
    }

    [Theory]
    [InlineData(DeliveryEvidence.BUYER_CONFIRMED)]
    [InlineData(DeliveryEvidence.SELLER_ASSET_GONE | DeliveryEvidence.INVENTORY_DELTA)]
    [InlineData(DeliveryEvidence.BUYER_CONFIRMED | DeliveryEvidence.SELLER_ASSET_GONE)]
    public async Task Evidence_That_Already_Settles_The_Delivery_Is_Not_Polled(DeliveryEvidence evidence)
    {
        await CreateAwaitingDeliveryAsync(evidence: evidence);

        var summary = await BuildSut().ExecuteAsync();

        Assert.Equal(0, summary.Examined);
    }

    [Fact]
    public async Task A_Transaction_Frozen_For_A_Steam_Outage_Is_Not_Polled()
    {
        // Frozen without an emergency hold (PlatformHealthProbeJob, 02 §3.3):
        // reading Steam during its own outage would only spend budget.
        await CreateAwaitingDeliveryAsync(freezeReason: TimeoutFreezeReason.STEAM_OUTAGE);

        var summary = await BuildSut().ExecuteAsync();

        Assert.Equal(0, summary.Examined);
    }

    [Fact]
    public async Task A_Cancelled_Transaction_Is_Not_Polled_Even_Inside_Its_Old_Window()
    {
        // A seller cancelling during PAYMENT_RECEIVED leaves the deadline in the
        // future and the baseline captured; only the status says it is over.
        await CreateAwaitingDeliveryAsync(status: TransactionStatus.CANCELLED_SELLER);

        var summary = await BuildSut().ExecuteAsync();

        Assert.Equal(0, summary.Examined);
    }

    [Fact]
    public async Task A_Never_Polled_Transaction_Goes_Before_One_Whose_Recheck_Has_Come_Round()
    {
        var polledLongAgo = await CreateAwaitingDeliveryAsync(
            paymentReceivedHoursAgo: 3, polledMinutesAgo: 30);
        var neverPolled = await CreateAwaitingDeliveryAsync(
            paymentReceivedHoursAgo: 1, assetId: "27348562892");
        _inventory.Register(SellerSteamId, NewSnapshot(ItemAssetId));
        _inventory.Register(SellerSteamId, NewSnapshot("27348562892"));

        await BuildSut().ExecuteAsync();

        // The older payment is due again, but a row never looked at outranks it.
        Assert.NotNull((await ReloadAsync(neverPolled.Id)).DeliveryPolledAt);
        Assert.Equal(
            _clock.GetUtcNow().UtcDateTime.AddMinutes(-30),
            (await ReloadAsync(polledLongAgo.Id)).DeliveryPolledAt);
    }

    [Fact]
    public async Task A_Held_Transaction_Is_Not_Polled()
    {
        await CreateAwaitingDeliveryAsync(onHold: true);

        var summary = await BuildSut().ExecuteAsync();

        Assert.Equal(0, summary.Examined);
    }

    [Fact]
    public async Task A_Transaction_Polled_Within_The_Recheck_Interval_Waits_Its_Turn()
    {
        var transaction = await CreateAwaitingDeliveryAsync();
        RegisterSellerStillHoldsItem();

        await BuildSut().ExecuteAsync();
        _clock.Advance(TimeSpan.FromSeconds(599));
        var tooSoon = await BuildSut().ExecuteAsync();
        _clock.Advance(TimeSpan.FromSeconds(1));
        var due = await BuildSut().ExecuteAsync();

        Assert.Equal(0, tooSoon.Examined);
        Assert.Equal(1, due.Examined);
        Assert.Equal(2, _inventory.ItemReadFreshness.Count);
        Assert.Equal(_clock.GetUtcNow().UtcDateTime, (await ReloadAsync(transaction.Id)).DeliveryPolledAt);
    }

    [Fact]
    public async Task One_Transaction_Per_Run_Never_Polled_First()
    {
        var older = await CreateAwaitingDeliveryAsync(paymentReceivedHoursAgo: 3);
        var newer = await CreateAwaitingDeliveryAsync(paymentReceivedHoursAgo: 1, assetId: "27348562892");
        _inventory.Register(SellerSteamId, NewSnapshot(ItemAssetId));
        _inventory.Register(SellerSteamId, NewSnapshot("27348562892"));

        await BuildSut().ExecuteAsync();
        var afterFirst = await ReloadAsync(older.Id);
        var newerAfterFirst = await ReloadAsync(newer.Id);
        _clock.Advance(TimeSpan.FromMinutes(1));
        await BuildSut().ExecuteAsync();

        // The payment that has waited longest goes first; the next minute takes
        // the one never polled rather than the one just polled.
        Assert.NotNull(afterFirst.DeliveryPolledAt);
        Assert.Null(newerAfterFirst.DeliveryPolledAt);
        Assert.NotNull((await ReloadAsync(newer.Id)).DeliveryPolledAt);
    }

    [Fact]
    public async Task A_Disabled_Poll_Reads_Nothing()
    {
        await CreateAwaitingDeliveryAsync();

        var summary = await BuildSut(new DeliveryPollingOptions { Enabled = false }).ExecuteAsync();

        Assert.Equal(0, summary.Examined);
        Assert.Empty(_inventory.ItemReadFreshness);
    }

    [Fact]
    public async Task An_Unreadable_Seller_Inventory_Stops_The_Run()
    {
        // Steam is limiting or down; the second row would only spend another
        // read into the same wall.
        var first = await CreateAwaitingDeliveryAsync(paymentReceivedHoursAgo: 3);
        var second = await CreateAwaitingDeliveryAsync(paymentReceivedHoursAgo: 1, assetId: "27348562892");
        _inventory.ForcedVisibility = InventoryVisibility.Unavailable;

        var summary = await BuildSut(new DeliveryPollingOptions { BatchSize = 2 }).ExecuteAsync();

        Assert.Equal(1, summary.Examined);
        Assert.Single(_inventory.ItemReadFreshness);
        // The failed row steps aside; the untouched one keeps its place.
        Assert.NotNull((await ReloadAsync(first.Id)).DeliveryPolledAt);
        Assert.Null((await ReloadAsync(second.Id)).DeliveryPolledAt);
    }

    [Fact]
    public async Task A_Concurrent_Update_Drops_The_Whole_Round()
    {
        // The buyer confirms receipt while the poll is reading Steam: the row's
        // RowVersion moves under it. Nothing from the round may be saved — not
        // the stamp, not the flags, not the outbox event.
        var transaction = await CreateAwaitingDeliveryAsync();
        RegisterBuyerCopies("99887766");
        _inventory.OnItemRead = async () =>
        {
            _inventory.OnItemRead = null;
            await using var other = CreateContext();
            var row = await other.Set<Transaction>().SingleAsync(t => t.Id == transaction.Id);
            row.BuyerConfirmedReceiptAt = _clock.GetUtcNow().UtcDateTime;
            await other.SaveChangesAsync();
        };

        var summary = await BuildSut().ExecuteAsync();

        Assert.Equal(new DeliveryPollingRunSummary(1, 0, 0), summary);
        var persisted = await ReloadAsync(transaction.Id);
        Assert.Null(persisted.DeliveryPolledAt);
        Assert.Equal(DeliveryEvidence.NONE, persisted.DeliveryEvidence);
        Assert.Equal(0, await CaptureCountAsync(transaction.Id));
    }

    [Fact]
    public async Task A_Lost_Concurrent_Update_Does_Not_Take_The_Next_Row_Down_With_It()
    {
        // The failed round's entities must leave the unit of work: still
        // tracked, they would be saved again with the next row and fail it too.
        var raced = await CreateAwaitingDeliveryAsync(paymentReceivedHoursAgo: 3);
        var next = await CreateAwaitingDeliveryAsync(paymentReceivedHoursAgo: 1, assetId: "27348562892");
        _inventory.Register(SellerSteamId, NewSnapshot(ItemAssetId));
        _inventory.Register(SellerSteamId, NewSnapshot("27348562892"));
        _inventory.OnItemRead = async () =>
        {
            _inventory.OnItemRead = null;
            await using var other = CreateContext();
            var row = await other.Set<Transaction>().SingleAsync(t => t.Id == raced.Id);
            row.BuyerConfirmedReceiptAt = _clock.GetUtcNow().UtcDateTime;
            await other.SaveChangesAsync();
        };

        var summary = await BuildSut(new DeliveryPollingOptions { BatchSize = 2 }).ExecuteAsync();

        Assert.Equal(2, summary.Examined);
        Assert.Null((await ReloadAsync(raced.Id)).DeliveryPolledAt);
        Assert.Equal(_clock.GetUtcNow().UtcDateTime, (await ReloadAsync(next.Id)).DeliveryPolledAt);
    }

    [Fact]
    public async Task A_Row_Settled_While_An_Earlier_Row_Was_Being_Read_Is_Skipped()
    {
        // The batch is chosen up front, but each row is re-checked on its own
        // turn: the buyer confirmed the second one while Steam answered for the
        // first, so reading it now would spend budget on a settled delivery.
        var first = await CreateAwaitingDeliveryAsync(paymentReceivedHoursAgo: 3);
        var second = await CreateAwaitingDeliveryAsync(paymentReceivedHoursAgo: 1, assetId: "27348562892");
        _inventory.Register(SellerSteamId, NewSnapshot(ItemAssetId));
        _inventory.Register(SellerSteamId, NewSnapshot("27348562892"));
        _inventory.OnItemRead = async () =>
        {
            _inventory.OnItemRead = null;
            await using var other = CreateContext();
            var row = await other.Set<Transaction>().SingleAsync(t => t.Id == second.Id);
            row.DeliveryEvidence = DeliveryEvidence.BUYER_CONFIRMED;
            row.BuyerConfirmedReceiptAt = _clock.GetUtcNow().UtcDateTime;
            await other.SaveChangesAsync();
        };

        var summary = await BuildSut(new DeliveryPollingOptions { BatchSize = 2 }).ExecuteAsync();

        Assert.Equal(1, summary.Examined);
        Assert.Single(_inventory.ItemReadFreshness);
        Assert.NotNull((await ReloadAsync(first.Id)).DeliveryPolledAt);
        Assert.Null((await ReloadAsync(second.Id)).DeliveryPolledAt);
    }

    // ================= Helpers =================

    private DeliveryPollingJob BuildSut(DeliveryPollingOptions? options = null) =>
        new(Context,
            _inventory,
            // The real engine: the poll's whole argument is about which of its
            // verdicts it acts on.
            new DeliveryVerificationService(
                Context, _inventory, NullLogger<DeliveryVerificationService>.Instance, _clock),
            new SettlementSettingsProvider(Context),
            _outbox,
            Options.Create(options ?? new DeliveryPollingOptions()),
            NullLogger<DeliveryPollingJob>.Instance,
            _clock);

    private void RegisterSellerStillHoldsItem() =>
        _inventory.Register(SellerSteamId, NewSnapshot(ItemAssetId));

    private void RegisterBuyerCopies(params string[] assetIds)
    {
        foreach (var assetId in assetIds)
            _inventory.Register(BuyerSteamId, NewSnapshot(assetId));
    }

    private static InventoryItemSnapshot NewSnapshot(string assetId) =>
        new(AssetId: assetId,
            ClassId: ItemClassId,
            InstanceId: ItemInstanceId,
            Name: "AK-47 | Redline",
            MarketHashName: "AK-47 | Redline (Field-Tested)",
            IconUrl: null,
            Exterior: "Field-Tested",
            Type: "Rifle",
            InspectLink: null,
            IsTradeable: true);

    private async Task<Transaction> ReloadAsync(Guid transactionId)
    {
        Context.ChangeTracker.Clear();
        return await Context.Set<Transaction>().AsNoTracking().FirstAsync(t => t.Id == transactionId);
    }

    private Task<int> CaptureCountAsync(Guid transactionId) =>
        Context.Set<DeliveryEvidenceCapture>().AsNoTracking()
            .CountAsync(c => c.TransactionId == transactionId);

    /// <summary>
    /// A paid transaction inside its delivery window — the state the poll is for.
    /// </summary>
    private async Task<Transaction> CreateAwaitingDeliveryAsync(
        DeliveryEvidence evidence = DeliveryEvidence.NONE,
        bool onHold = false,
        bool withBaseline = true,
        int deadlineInMinutes = 45,
        int paymentReceivedHoursAgo = 1,
        string assetId = ItemAssetId,
        TimeoutFreezeReason? freezeReason = null,
        TransactionStatus status = TransactionStatus.PAYMENT_RECEIVED,
        int? polledMinutesAgo = null)
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
            AcceptedAt = nowUtc.AddHours(-paymentReceivedHoursAgo - 2),
            SellerReadyConfirmedAt = nowUtc.AddHours(-paymentReceivedHoursAgo - 1),
            PaymentReceivedAt = nowUtc.AddHours(-paymentReceivedHoursAgo),
            DeliveryDeadline = nowUtc.AddMinutes(deadlineInMinutes),
            DeliveryEvidence = evidence,
            BuyerBaselineClassCount = withBaseline ? 0 : null,
            BuyerBaselineAssetIds = withBaseline ? JsonSerializer.Serialize(Array.Empty<string>()) : null,
            BuyerBaselineCapturedAt = withBaseline ? nowUtc.AddHours(-paymentReceivedHoursAgo - 1) : null,
            // CK_Transactions_Hold + CK_Transactions_FreezeHold_Reverse.
            IsOnHold = onHold,
            EmergencyHoldAt = onHold ? nowUtc.AddMinutes(-5) : null,
            EmergencyHoldReason = onHold ? "Test emergency hold" : null,
            EmergencyHoldByAdminId = onHold ? _admin.Id : null,
            TimeoutFrozenAt = onHold || freezeReason is not null ? nowUtc.AddMinutes(-5) : null,
            TimeoutFreezeReason = onHold ? TimeoutFreezeReason.EMERGENCY_HOLD : freezeReason,
            TimeoutRemainingSeconds = onHold || freezeReason is not null ? 3600 : null,
            // CK_Transactions_Cancel — a cancelled row carries who, why and when.
            CancelledBy = status == TransactionStatus.CANCELLED_SELLER ? CancelledByType.SELLER : null,
            CancelReason = status == TransactionStatus.CANCELLED_SELLER ? "Test cancellation" : null,
            CancelledAt = status == TransactionStatus.CANCELLED_SELLER ? nowUtc.AddMinutes(-2) : null,
            DeliveryPolledAt = polledMinutesAgo is { } ago ? nowUtc.AddMinutes(-ago) : null,
        };
        Context.Set<Transaction>().Add(transaction);
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();
        return transaction;
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
