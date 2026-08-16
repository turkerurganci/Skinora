using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Skinora.Shared.Domain;
using Skinora.Shared.Enums;
using Skinora.Shared.Interfaces;
using Skinora.Shared.Persistence;
using Skinora.Shared.Persistence.Outbox;
using Skinora.Shared.Tests.Integration;
using Skinora.Transactions.Application.Lifecycle;
using Skinora.Transactions.Application.Pricing;
using Skinora.Transactions.Application.Steam;
using Skinora.Transactions.Domain.Entities;
using Skinora.Transactions.Infrastructure.Persistence;
using Skinora.Users.Application.Wallet;
using Skinora.Users.Domain.Entities;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.Transactions.Tests.Integration.Lifecycle;

/// <summary>
/// End-to-end coverage for the <c>POST /transactions</c> creation pipeline
/// (T45 — 07 §7.2, 03 §2.2). Tests focus on the FLAGGED-vs-CREATED status
/// decision, snapshot fidelity (item, wallet, market price), outbox event
/// emission and per-validator rejection codes.
/// </summary>
public class TransactionCreationServiceTests : IntegrationTestBase
{
    static TransactionCreationServiceTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        TransactionsModuleDbRegistration.RegisterTransactionsModule();
        Skinora.Platform.Infrastructure.Persistence.PlatformModuleDbRegistration.RegisterPlatformModule();
    }

    private const string ValidWallet = "TXyzABCDEFGHJKLMNPQRSTUVWXYZ234567";
    private const string SellerSteamId = "76561198000000060";
    private const string BuyerSteamId = "76561198000000061";
    private const string ItemAssetId = "27348562891";

    private User _seller = null!;
    private FakeTimeProvider _clock = null!;
    private FakeSteamInventoryReader _inventory = null!;
    private FakeMarketPriceProvider _marketPrice = null!;
    private RecordingOutboxService _outbox = null!;
    private RecordingPaymentAddressAllocator _allocator = null!;

    /// <summary>
    /// T128 — swappable so one test can force an <c>InviteToken</c> collision
    /// and prove that a unique violation on the <em>other</em> index is not
    /// reported as ITEM_ALREADY_LISTED.
    /// </summary>
    private IInvitationCodeGenerator _inviteCodes = new InvitationCodeGenerator();

    protected override async Task SeedAsync(AppDbContext context)
    {
        _seller = new User
        {
            Id = Guid.NewGuid(),
            SteamId = SellerSteamId,
            SteamDisplayName = "Seller",
            DefaultPayoutAddress = ValidWallet,
            MobileAuthenticatorVerified = true,
        };
        context.Set<User>().Add(_seller);
        await context.SaveChangesAsync();

        await context.ConfigureSettingAsync(TransactionLimitsProvider.MaxConcurrentKey, "5");
        await context.ConfigureSettingAsync(TransactionLimitsProvider.PaymentTimeoutMinKey, "360");      // 6h
        await context.ConfigureSettingAsync(TransactionLimitsProvider.PaymentTimeoutMaxKey, "4320");    // 72h
        await context.ConfigureSettingAsync(TransactionLimitsProvider.PaymentTimeoutDefaultKey, "1440"); // 24h
        await context.ConfigureSettingAsync(TransactionLimitsProvider.AcceptTimeoutKey, "60");
        await context.ConfigureSettingAsync(TransactionLimitsProvider.MinAmountKey, "10");
        await context.ConfigureSettingAsync(TransactionLimitsProvider.MaxAmountKey, "50000");
        await context.ConfigureSettingAsync(TransactionLimitsProvider.CommissionRateKey, "0.02");
        await context.ConfigureSettingAsync(TransactionLimitsProvider.OpenLinkEnabledKey, "false");
        await context.ConfigureSettingAsync(FraudPreCheckService.DeviationThresholdKey, "0.20");

        _clock = new FakeTimeProvider(new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero));
        _inventory = new FakeSteamInventoryReader();
        _marketPrice = new FakeMarketPriceProvider();
        _outbox = new RecordingOutboxService();
        _allocator = new RecordingPaymentAddressAllocator();

        _inventory.Register(SellerSteamId, new InventoryItemSnapshot(
            AssetId: ItemAssetId,
            ClassId: "abc-class",
            InstanceId: "abc-instance",
            Name: "AK-47 | Redline",
            MarketHashName: "AK-47 | Redline (Field-Tested)",
            IconUrl: "https://example/icon.png",
            Exterior: "Field-Tested",
            Type: "Rifle",
            InspectLink: null,
            IsTradeable: true));
    }

    [Fact]
    public async Task Happy_Path_Creates_Transaction_And_Emits_Outbox_Event()
    {
        var sut = BuildSut();
        var request = ValidRequest();

        var outcome = await sut.CreateAsync(_seller.Id, request, CancellationToken.None);

        Assert.Equal(CreateTransactionStatus.Created, outcome.Status);
        Assert.NotNull(outcome.Body);
        Assert.Equal(TransactionStatus.CREATED, outcome.Body.Status);
        Assert.Null(outcome.Body.FlagReason);

        var persisted = await Context.Set<Transaction>()
            .AsNoTracking()
            .SingleAsync(t => t.Id == outcome.Body.Id);
        Assert.Equal(TransactionStatus.CREATED, persisted.Status);
        Assert.Equal(_seller.Id, persisted.SellerId);
        Assert.Equal(BuyerSteamId, persisted.TargetBuyerSteamId);
        Assert.Equal(100m, persisted.Price);
        Assert.Equal(2m, persisted.CommissionAmount); // 100 × 0.02 → 2.000000
        Assert.Equal(102m, persisted.TotalAmount);
        Assert.Equal(ValidWallet, persisted.SellerPayoutAddress);
        Assert.Equal("AK-47 | Redline", persisted.ItemName);
        Assert.NotNull(persisted.AcceptDeadline);
        Assert.Equal(BuyerIdentificationMethod.STEAM_ID, persisted.BuyerIdentificationMethod);

        // STEAM_ID method ⇒ InviteToken NULL (CK_Transactions_BuyerMethod_SteamId
        // 06 §3.5). Unregistered buyer still finds the transaction via the
        // public /transactions/:id link once they authenticate.
        Assert.Null(persisted.InviteToken);

        Assert.Single(_outbox.Published);
        var evt = Assert.IsType<Skinora.Shared.Events.TransactionCreatedEvent>(_outbox.Published[0]);
        Assert.Equal(persisted.Id, evt.TransactionId);
        Assert.Equal(_seller.Id, evt.SellerId);
        Assert.Null(evt.BuyerId);
    }

    [Fact]
    public async Task Resolves_Buyer_Id_When_Steam_Id_Belongs_To_Registered_User()
    {
        var buyer = new User
        {
            Id = Guid.NewGuid(),
            SteamId = BuyerSteamId,
            SteamDisplayName = "Buyer",
            MobileAuthenticatorVerified = true,
        };
        Context.Set<User>().Add(buyer);
        await Context.SaveChangesAsync();

        var sut = BuildSut();
        var outcome = await sut.CreateAsync(_seller.Id, ValidRequest(), CancellationToken.None);

        Assert.Equal(CreateTransactionStatus.Created, outcome.Status);
        var persisted = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == outcome.Body!.Id);
        Assert.Equal(buyer.Id, persisted.BuyerId);
        Assert.Null(persisted.InviteToken); // registered buyer does not need invite link
    }

    [Fact]
    public async Task Flags_Transaction_When_Price_Deviation_Exceeds_Threshold()
    {
        _marketPrice.Price = 50m; // quoted 100, market 50 → 100% deviation, threshold 20%.
        var sut = BuildSut();

        var outcome = await sut.CreateAsync(_seller.Id, ValidRequest(), CancellationToken.None);

        Assert.Equal(CreateTransactionStatus.Created, outcome.Status);
        Assert.Equal(TransactionStatus.FLAGGED, outcome.Body!.Status);
        Assert.Equal("PRICE_DEVIATION", outcome.Body.FlagReason);

        var persisted = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == outcome.Body.Id);
        Assert.Equal(TransactionStatus.FLAGGED, persisted.Status);
        Assert.Null(persisted.AcceptDeadline);
        Assert.Equal(50m, persisted.MarketPriceAtCreation);
    }

    [Fact]
    public async Task Flags_Transaction_When_High_Volume_Count_Threshold_Exceeded()
    {
        // Pre-seed 6 prior transactions inside the 24h window — count threshold is 5.
        await ConfigureHighVolumeAsync(periodHours: 24, countThreshold: 5, amountThreshold: 1_000_000m);
        await SeedRecentTransactionsAsync(count: 6, eachAmount: 10m);

        var sut = BuildSut();
        var outcome = await sut.CreateAsync(_seller.Id, ValidRequest(), CancellationToken.None);

        Assert.Equal(CreateTransactionStatus.Created, outcome.Status);
        Assert.Equal(TransactionStatus.FLAGGED, outcome.Body!.Status);
        Assert.Equal("HIGH_VOLUME", outcome.Body.FlagReason);

        var persisted = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == outcome.Body.Id);
        Assert.Equal(TransactionStatus.FLAGGED, persisted.Status);
        Assert.Null(persisted.AcceptDeadline);
    }

    [Fact]
    public async Task Flags_Transaction_When_High_Volume_Amount_Threshold_Exceeded()
    {
        // 3 prior transactions × 5,000 USDT = 15,000 — amount threshold 10,000.
        await ConfigureHighVolumeAsync(periodHours: 24, countThreshold: 1000, amountThreshold: 10_000m);
        await SeedRecentTransactionsAsync(count: 3, eachAmount: 5_000m);

        var sut = BuildSut();
        var outcome = await sut.CreateAsync(_seller.Id, ValidRequest(), CancellationToken.None);

        Assert.Equal(TransactionStatus.FLAGGED, outcome.Body!.Status);
        Assert.Equal("HIGH_VOLUME", outcome.Body.FlagReason);
    }

    [Fact]
    public async Task Does_Not_Flag_For_High_Volume_When_Prior_Transactions_Outside_Window()
    {
        await ConfigureHighVolumeAsync(periodHours: 24, countThreshold: 1, amountThreshold: 1m);
        // Seed 5 transactions 48h ago — outside the 24h rolling window.
        var olderUtc = _clock.GetUtcNow().UtcDateTime.AddHours(-48);
        await SeedRecentTransactionsAsync(count: 5, eachAmount: 100m, createdAtUtc: olderUtc);

        var sut = BuildSut();
        var outcome = await sut.CreateAsync(_seller.Id, ValidRequest(), CancellationToken.None);

        Assert.Equal(CreateTransactionStatus.Created, outcome.Status);
        Assert.Equal(TransactionStatus.CREATED, outcome.Body!.Status);
        Assert.Null(outcome.Body.FlagReason);
    }

    [Fact]
    public async Task Flags_Transaction_For_Dormant_Account_Anomaly()
    {
        // Backdate seller to 90 days old, 0 completed transactions, attempt
        // a 100 USDT transaction with a 50 USDT dormant value threshold.
        await BackdateSellerAsync(_seller.Id, ageDays: 90);
        await Context.ConfigureSettingAsync(FraudPreCheckService.DormantMinAgeDaysKey, "30");
        await Context.ConfigureSettingAsync(FraudPreCheckService.DormantValueThresholdKey, "50");

        var sut = BuildSut();
        var outcome = await sut.CreateAsync(_seller.Id, ValidRequest(), CancellationToken.None);

        Assert.Equal(CreateTransactionStatus.Created, outcome.Status);
        Assert.Equal(TransactionStatus.FLAGGED, outcome.Body!.Status);
        Assert.Equal("ABNORMAL_BEHAVIOR", outcome.Body.FlagReason);

        var persisted = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == outcome.Body.Id);
        Assert.Equal(TransactionStatus.FLAGGED, persisted.Status);
        Assert.Null(persisted.AcceptDeadline);
    }

    [Fact]
    public async Task Does_Not_Flag_New_Account_For_Dormant_Anomaly()
    {
        // Brand-new seller (default CreatedAt) with 0 completed transactions
        // and a high attempted amount — caught by T39 new-account limits, not
        // the dormant rule. The dormant rule explicitly requires age >= min.
        await Context.ConfigureSettingAsync(FraudPreCheckService.DormantMinAgeDaysKey, "30");
        await Context.ConfigureSettingAsync(FraudPreCheckService.DormantValueThresholdKey, "50");

        var sut = BuildSut();
        var outcome = await sut.CreateAsync(_seller.Id, ValidRequest(), CancellationToken.None);

        Assert.Equal(CreateTransactionStatus.Created, outcome.Status);
        Assert.Equal(TransactionStatus.CREATED, outcome.Body!.Status);
        Assert.Null(outcome.Body.FlagReason);
    }

    [Fact]
    public async Task Rejects_Create_When_Seller_Suspended()
    {
        // T105a — a suspended seller cannot start a transaction (02 §14.0).
        var seller = await Context.Set<User>().SingleAsync(u => u.Id == _seller.Id);
        seller.IsSuspended = true;
        await Context.SaveChangesAsync();

        var sut = BuildSut();
        var outcome = await sut.CreateAsync(_seller.Id, ValidRequest(), CancellationToken.None);

        Assert.Equal(CreateTransactionStatus.SellerNotFound, outcome.Status);
    }

    [Fact]
    public async Task Rejects_Below_Minimum_Price()
    {
        var sut = BuildSut();
        var outcome = await sut.CreateAsync(_seller.Id, ValidRequest() with { Price = "5.00" }, CancellationToken.None);

        Assert.Equal(CreateTransactionStatus.PriceOutOfRange, outcome.Status);
        Assert.Equal(TransactionErrorCodes.PriceOutOfRange, outcome.ErrorCode);
    }

    [Fact]
    public async Task Rejects_Timeout_Below_Configured_Range()
    {
        var sut = BuildSut();
        var outcome = await sut.CreateAsync(_seller.Id, ValidRequest() with { PaymentTimeoutHours = 1 }, CancellationToken.None);

        Assert.Equal(CreateTransactionStatus.TimeoutOutOfRange, outcome.Status);
        Assert.Equal(TransactionErrorCodes.TimeoutOutOfRange, outcome.ErrorCode);
    }

    [Fact]
    public async Task Rejects_Open_Link_When_Disabled()
    {
        var sut = BuildSut();
        var request = ValidRequest() with
        {
            BuyerIdentificationMethod = BuyerIdentificationMethod.OPEN_LINK,
            BuyerSteamId = null,
        };

        var outcome = await sut.CreateAsync(_seller.Id, request, CancellationToken.None);

        Assert.Equal(CreateTransactionStatus.OpenLinkDisabled, outcome.Status);
        Assert.Equal(TransactionErrorCodes.OpenLinkDisabled, outcome.ErrorCode);
    }

    [Fact]
    public async Task Allows_Open_Link_When_Enabled_And_Issues_Invite_Token()
    {
        await Context.ConfigureSettingAsync(TransactionLimitsProvider.OpenLinkEnabledKey, "true");

        var sut = BuildSut();
        var request = ValidRequest() with
        {
            BuyerIdentificationMethod = BuyerIdentificationMethod.OPEN_LINK,
            BuyerSteamId = null,
        };

        var outcome = await sut.CreateAsync(_seller.Id, request, CancellationToken.None);

        Assert.Equal(CreateTransactionStatus.Created, outcome.Status);
        Assert.NotNull(outcome.Body);
        var persisted = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == outcome.Body.Id);
        Assert.NotNull(persisted.InviteToken);
        Assert.StartsWith("/invite/", outcome.Body.InviteUrl);
    }

    [Fact]
    public async Task Rejects_Invalid_Wallet_Format()
    {
        var sut = BuildSut();
        var outcome = await sut.CreateAsync(
            _seller.Id,
            ValidRequest() with { SellerWalletAddress = "NOT_A_TRC20_ADDRESS" },
            CancellationToken.None);

        Assert.Equal(CreateTransactionStatus.InvalidWallet, outcome.Status);
        Assert.Equal(TransactionErrorCodes.InvalidWalletAddress, outcome.ErrorCode);
    }

    [Fact]
    public async Task Rejects_When_Item_Not_In_Inventory()
    {
        var sut = BuildSut();
        var outcome = await sut.CreateAsync(
            _seller.Id,
            ValidRequest() with { ItemAssetId = "missing" },
            CancellationToken.None);

        Assert.Equal(CreateTransactionStatus.ItemNotInInventory, outcome.Status);
        Assert.Equal(TransactionErrorCodes.ItemNotInInventory, outcome.ErrorCode);
    }

    [Fact]
    public async Task Rejects_With_InventoryPrivate_When_Seller_Inventory_Hidden()
    {
        // T121 — 08 §2.3: a hidden profile says nothing about the asset. The
        // seller's actual fix is to make the profile public, which the old
        // ITEM_NOT_IN_INVENTORY answer could never have told them. The asset ID
        // is the registered, present one on purpose: the outcome must be driven
        // by visibility, not by the item lookup.
        _inventory.ForcedVisibility = InventoryVisibility.Private;

        var sut = BuildSut();
        var outcome = await sut.CreateAsync(_seller.Id, ValidRequest(), CancellationToken.None);

        Assert.Equal(CreateTransactionStatus.InventoryPrivate, outcome.Status);
        Assert.Equal(TransactionErrorCodes.InventoryPrivate, outcome.ErrorCode);
        Assert.NotEqual(TransactionErrorCodes.ItemNotInInventory, outcome.ErrorCode);
    }

    [Fact]
    public async Task Rejects_With_SteamUnavailable_When_Inventory_Cannot_Be_Read()
    {
        // T121 — a Steam outage is absence of information, not a missing item
        // (08 §2.3). Retryable, so it must not be reported with a code that
        // sends the seller looking for an item sitting in their inventory.
        _inventory.ForcedVisibility = InventoryVisibility.Unavailable;

        var sut = BuildSut();
        var outcome = await sut.CreateAsync(_seller.Id, ValidRequest(), CancellationToken.None);

        Assert.Equal(CreateTransactionStatus.SteamUnavailable, outcome.Status);
        Assert.Equal(TransactionErrorCodes.SteamUnavailable, outcome.ErrorCode);
        Assert.NotEqual(TransactionErrorCodes.ItemNotInInventory, outcome.ErrorCode);
    }

    [Fact]
    public async Task Inventory_Visibility_Drives_Three_Distinct_Create_Outcomes()
    {
        // The AC in one place: the three inventory answers must not collapse
        // onto a single create-path verdict.
        _inventory.ForcedVisibility = null;
        var absent = await BuildSut().CreateAsync(
            _seller.Id, ValidRequest() with { ItemAssetId = "missing" }, CancellationToken.None);

        _inventory.ForcedVisibility = InventoryVisibility.Private;
        var hidden = await BuildSut().CreateAsync(_seller.Id, ValidRequest(), CancellationToken.None);

        _inventory.ForcedVisibility = InventoryVisibility.Unavailable;
        var down = await BuildSut().CreateAsync(_seller.Id, ValidRequest(), CancellationToken.None);

        Assert.Equal(3, new[] { absent.Status, hidden.Status, down.Status }.Distinct().Count());
        Assert.Equal(3, new[] { absent.ErrorCode, hidden.ErrorCode, down.ErrorCode }.Distinct().Count());
    }

    [Fact]
    public async Task Rejects_When_Item_Has_Trade_Lock()
    {
        _inventory.Register(SellerSteamId, new InventoryItemSnapshot(
            "locked", "c", null, "Locked Item", "Locked Item", null, null, null, null, IsTradeable: false));

        var sut = BuildSut();
        var outcome = await sut.CreateAsync(
            _seller.Id,
            ValidRequest() with { ItemAssetId = "locked" },
            CancellationToken.None);

        Assert.Equal(CreateTransactionStatus.ItemNotTradeable, outcome.Status);
        Assert.Equal(TransactionErrorCodes.ItemNotTradeable, outcome.ErrorCode);
    }

    [Fact]
    public async Task Rejects_Mobile_Authenticator_Not_Verified_Via_Eligibility()
    {
        _seller.MobileAuthenticatorVerified = false;
        Context.Set<User>().Update(_seller);
        await Context.SaveChangesAsync();

        var sut = BuildSut();
        var outcome = await sut.CreateAsync(_seller.Id, ValidRequest(), CancellationToken.None);

        Assert.Equal(CreateTransactionStatus.EligibilityFailed, outcome.Status);
        Assert.Equal(TransactionErrorCodes.MobileAuthenticatorRequired, outcome.ErrorCode);
    }

    // ---- T70 — payment-address inline allocation -----------------------------

    [Fact]
    public async Task Invokes_PaymentAddress_Allocator_For_CREATED_Transactions()
    {
        var sut = BuildSut();
        var outcome = await sut.CreateAsync(_seller.Id, ValidRequest(), CancellationToken.None);

        Assert.Equal(CreateTransactionStatus.Created, outcome.Status);
        Assert.Equal(TransactionStatus.CREATED, outcome.Body!.Status);
        Assert.Single(_allocator.Allocations);
        Assert.Equal(outcome.Body.Id, _allocator.Allocations[0]);
    }

    [Fact]
    public async Task Skips_PaymentAddress_Allocator_For_FLAGGED_Transactions()
    {
        // Price deviation forces FLAGGED — payment-address allocation must
        // wait until admin approval transitions the row back to CREATED
        // (future task entry point).
        _marketPrice.Price = 50m;
        var sut = BuildSut();

        var outcome = await sut.CreateAsync(_seller.Id, ValidRequest(), CancellationToken.None);

        Assert.Equal(CreateTransactionStatus.Created, outcome.Status);
        Assert.Equal(TransactionStatus.FLAGGED, outcome.Body!.Status);
        Assert.Empty(_allocator.Allocations);
    }

    [Fact]
    public async Task Transaction_Is_Still_Created_When_Allocator_Reports_SidecarUnavailable()
    {
        // Best-effort semantics: a sidecar outage during creation must not
        // bubble up as a transaction failure — EnsurePaymentAddressJob will
        // retry the allocation on its next sweep.
        _allocator.DefaultStatus = Skinora.Transactions.Application.PaymentAddresses
            .PaymentAddressAllocationStatus.SidecarUnavailable;
        var sut = BuildSut();

        var outcome = await sut.CreateAsync(_seller.Id, ValidRequest(), CancellationToken.None);

        Assert.Equal(CreateTransactionStatus.Created, outcome.Status);
        Assert.Equal(TransactionStatus.CREATED, outcome.Body!.Status);
        var persisted = await Context.Set<Transaction>().AsNoTracking()
            .SingleAsync(t => t.Id == outcome.Body.Id);
        Assert.Equal(TransactionStatus.CREATED, persisted.Status);
        Assert.Single(_allocator.Allocations);
    }

    // ---- T128 — one open transaction per item (02 §2.3) ---------------------

    [Fact]
    public async Task Rejects_Second_Create_For_Same_Asset_With_ItemAlreadyListed()
    {
        await SeedListingAsync(_seller.Id, ItemAssetId, TransactionStatus.CREATED);

        var sut = BuildSut();
        var outcome = await sut.CreateAsync(_seller.Id, ValidRequest(), CancellationToken.None);

        Assert.Equal(CreateTransactionStatus.ItemAlreadyListed, outcome.Status);
        Assert.Equal(TransactionErrorCodes.ItemAlreadyListed, outcome.ErrorCode);

        // The rejection is the whole outcome: nothing was written and no event
        // was staged for a transaction that does not exist.
        Assert.Equal(1, await Context.Set<Transaction>()
            .CountAsync(t => t.SellerId == _seller.Id && t.ItemAssetId == ItemAssetId));
        Assert.Empty(_outbox.Published);
        Assert.Empty(_allocator.Allocations);
    }

    [Fact]
    public async Task Already_Listed_Gate_Runs_Before_The_Steam_Read()
    {
        // 02 §2.3 needs no inventory evidence, and Steam reads are the scarce,
        // rate-limited resource (T122). A duplicate listing attempt must not
        // spend one.
        await SeedListingAsync(_seller.Id, ItemAssetId, TransactionStatus.CREATED);

        var sut = BuildSut();
        var outcome = await sut.CreateAsync(_seller.Id, ValidRequest(), CancellationToken.None);

        Assert.Equal(CreateTransactionStatus.ItemAlreadyListed, outcome.Status);
        Assert.Empty(_inventory.ItemReadFreshness);
    }

    [Fact]
    public async Task Already_Listed_Gate_Does_Not_Preempt_SellerNotFound()
    {
        // A suspended seller must hear that their account is the problem, not
        // that the item is listed — the gate sits after the seller lookup on
        // purpose.
        await SeedListingAsync(_seller.Id, ItemAssetId, TransactionStatus.CREATED);
        _seller.IsSuspended = true;
        Context.Set<User>().Update(_seller);
        await Context.SaveChangesAsync();

        var sut = BuildSut();
        var outcome = await sut.CreateAsync(_seller.Id, ValidRequest(), CancellationToken.None);

        Assert.Equal(CreateTransactionStatus.SellerNotFound, outcome.Status);
    }

    [Theory]
    [InlineData(TransactionStatus.COMPLETED)]
    [InlineData(TransactionStatus.CANCELLED_TIMEOUT)]
    [InlineData(TransactionStatus.CANCELLED_SELLER)]
    [InlineData(TransactionStatus.CANCELLED_BUYER)]
    [InlineData(TransactionStatus.CANCELLED_ADMIN)]
    [InlineData(TransactionStatus.REFUNDED)]
    public async Task Terminal_Transaction_Over_Same_Asset_Does_Not_Block_A_New_Create(
        TransactionStatus terminalStatus)
    {
        // The rule is "one OPEN transaction per item". Once the previous one
        // has ended, the seller may list the asset again — and the unique index
        // agrees, because its filter excludes exactly these six statuses
        // (06 §5.1). A gate stricter than the index would strand the asset.
        await SeedListingAsync(_seller.Id, ItemAssetId, terminalStatus);

        var sut = BuildSut();
        var outcome = await sut.CreateAsync(_seller.Id, ValidRequest(), CancellationToken.None);

        Assert.Equal(CreateTransactionStatus.Created, outcome.Status);
        Assert.Equal(TransactionStatus.CREATED, outcome.Body!.Status);
    }

    [Fact]
    public async Task Another_Sellers_Open_Listing_Over_Same_Asset_Does_Not_Block()
    {
        // The key is (SellerId, ItemAssetId): asset ids are Steam-account
        // scoped, so a row belonging to someone else says nothing about this
        // seller's inventory.
        var otherSeller = new User
        {
            Id = Guid.NewGuid(),
            SteamId = "76561198000000099",
            SteamDisplayName = "Other Seller",
            DefaultPayoutAddress = ValidWallet,
            MobileAuthenticatorVerified = true,
        };
        Context.Set<User>().Add(otherSeller);
        await Context.SaveChangesAsync();
        await SeedListingAsync(otherSeller.Id, ItemAssetId, TransactionStatus.CREATED);

        var sut = BuildSut();
        var outcome = await sut.CreateAsync(_seller.Id, ValidRequest(), CancellationToken.None);

        Assert.Equal(CreateTransactionStatus.Created, outcome.Status);
    }

    [Fact]
    public async Task Create_Losing_The_Uniqueness_Race_Reports_ItemAlreadyListed()
    {
        // Two creates for the same asset can both clear the read-based gate.
        // The competing row is inserted from a separate context during the
        // inventory read — after the gate, before SaveChanges — so the losing
        // caller reaches the unique index exactly as it would in production.
        Guid winnerId = Guid.Empty;
        _inventory.OnItemRead = async () =>
        {
            _inventory.OnItemRead = null; // one interleave, not one per read
            await using var competing = CreateContext();
            winnerId = await SeedListingAsync(
                _seller.Id, ItemAssetId, TransactionStatus.CREATED, competing);
        };

        var sut = BuildSut();
        var outcome = await sut.CreateAsync(_seller.Id, ValidRequest(), CancellationToken.None);

        Assert.Equal(CreateTransactionStatus.ItemAlreadyListed, outcome.Status);
        Assert.Equal(TransactionErrorCodes.ItemAlreadyListed, outcome.ErrorCode);

        // The winner survives alone — the loser's row was never committed.
        await using var verify = CreateContext();
        var rows = await verify.Set<Transaction>().AsNoTracking()
            .Where(t => t.SellerId == _seller.Id && t.ItemAssetId == ItemAssetId)
            .Select(t => t.Id)
            .ToListAsync();
        Assert.Equal([winnerId], rows);
    }

    [Fact]
    public async Task Unique_Violation_On_A_Different_Index_Is_Not_Reported_As_ItemAlreadyListed()
    {
        // Transactions carries a second unique index (InviteToken). Reporting
        // that collision as "item already listed" would send the seller to fix
        // something that is not wrong, so the catch confirms the conflict by
        // re-reading and rethrows when there is none.
        await Context.ConfigureSettingAsync(TransactionLimitsProvider.OpenLinkEnabledKey, "true");
        const string FixedToken = "t128-fixed-invite-token";
        _inviteCodes = new FixedInvitationCodeGenerator(FixedToken);

        _inventory.OnItemRead = async () =>
        {
            _inventory.OnItemRead = null;
            await using var competing = CreateContext();
            // Same token, different asset: the item index stays clean.
            await SeedListingAsync(
                _seller.Id, "some-other-asset", TransactionStatus.CREATED, competing,
                inviteToken: FixedToken);
        };

        var sut = BuildSut();
        var request = ValidRequest() with
        {
            BuyerIdentificationMethod = BuyerIdentificationMethod.OPEN_LINK,
            BuyerSteamId = null,
        };

        await Assert.ThrowsAsync<DbUpdateException>(
            () => sut.CreateAsync(_seller.Id, request, CancellationToken.None));
    }

    /// <summary>
    /// Insert one seller-owned transaction over <paramref name="itemAssetId"/>.
    /// Written through the given context (defaults to the test's own) so a race
    /// test can commit from a second connection.
    /// </summary>
    private async Task<Guid> SeedListingAsync(
        Guid sellerId,
        string itemAssetId,
        TransactionStatus status,
        AppDbContext? context = null,
        string? inviteToken = null)
    {
        var db = context ?? Context;
        var nowUtc = _clock.GetUtcNow().UtcDateTime;

        // CK_Transactions_Cancel — the five unwound statuses carry a forensic
        // trail; COMPLETED and the live statuses leave these NULL.
        var isUnwound = status is TransactionStatus.CANCELLED_TIMEOUT
            or TransactionStatus.CANCELLED_SELLER
            or TransactionStatus.CANCELLED_BUYER
            or TransactionStatus.CANCELLED_ADMIN
            or TransactionStatus.REFUNDED;

        var tx = new Transaction
        {
            Id = Guid.NewGuid(),
            Status = status,
            SellerId = sellerId,
            BuyerIdentificationMethod = inviteToken is null
                ? BuyerIdentificationMethod.STEAM_ID
                : BuyerIdentificationMethod.OPEN_LINK,
            TargetBuyerSteamId = inviteToken is null ? BuyerSteamId : null,
            InviteToken = inviteToken,
            ItemAssetId = itemAssetId,
            ItemClassId = "abc-class",
            ItemName = "AK-47 | Redline",
            StablecoinType = StablecoinType.USDT,
            Price = 100m,
            CommissionRate = 0.02m,
            CommissionAmount = 2m,
            TotalAmount = 102m,
            SellerPayoutAddress = ValidWallet,
            PaymentTimeoutMinutes = 1440,
            AcceptDeadline = status == TransactionStatus.CREATED ? nowUtc.AddHours(1) : null,
            CancelledBy = isUnwound ? CancelledByType.ADMIN : null,
            CancelReason = isUnwound ? "seeded terminal row" : null,
            CancelledAt = isUnwound ? nowUtc.AddMinutes(-5) : null,
        };
        db.Set<Transaction>().Add(tx);
        await db.SaveChangesAsync();
        return tx.Id;
    }

    private sealed class FixedInvitationCodeGenerator : IInvitationCodeGenerator
    {
        private readonly string _token;

        public FixedInvitationCodeGenerator(string token) => _token = token;

        public string Generate() => _token;
    }

    private TransactionCreationService BuildSut()
    {
        var limits = new TransactionLimitsProvider(Context);
        var eligibility = new TransactionEligibilityService(
            Context,
            limits,
            new AlwaysClearFlagChecker(),
            _clock);
        var fraud = new FraudPreCheckService(Context, _marketPrice);
        return new TransactionCreationService(
            Context,
            eligibility,
            limits,
            _inventory,
            fraud,
            new RecordingFraudFlagWriter(),
            new Trc20AddressValidator(),
            new NoMatchWalletSanctionsCheck(),
            _inviteCodes,
            _outbox,
            new NullSteamInventoryCacheInvalidator(),
            _allocator,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<TransactionCreationService>.Instance,
            _clock);
    }

    private CreateTransactionRequest ValidRequest() => new(
        ItemAssetId: ItemAssetId,
        Stablecoin: StablecoinType.USDT,
        Price: "100.00",
        PaymentTimeoutHours: 24,
        BuyerIdentificationMethod: BuyerIdentificationMethod.STEAM_ID,
        BuyerSteamId: BuyerSteamId,
        SellerWalletAddress: ValidWallet);

    private async Task ConfigureHighVolumeAsync(int periodHours, int countThreshold, decimal amountThreshold)
    {
        await Context.ConfigureSettingAsync(FraudPreCheckService.HighVolumePeriodHoursKey, periodHours.ToString(System.Globalization.CultureInfo.InvariantCulture));
        await Context.ConfigureSettingAsync(FraudPreCheckService.HighVolumeCountThresholdKey, countThreshold.ToString(System.Globalization.CultureInfo.InvariantCulture));
        await Context.ConfigureSettingAsync(FraudPreCheckService.HighVolumeAmountThresholdKey, amountThreshold.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Insert <paramref name="count"/> seller-owned <c>Transaction</c> rows
    /// inside the high-volume window. Two-stage save (Add → Save → Update
    /// CreatedAt → Save) so the audit pipeline cannot overwrite our backdated
    /// timestamps (see MEMORY: T33 yapım notu).
    /// </summary>
    private async Task SeedRecentTransactionsAsync(int count, decimal eachAmount, DateTime? createdAtUtc = null)
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var stamp = createdAtUtc ?? nowUtc.AddHours(-1);
        var ids = new List<Guid>(capacity: count);

        for (var i = 0; i < count; i++)
        {
            // COMPLETED so the eligibility max-concurrent check (T39) does
            // not also reject the new transaction — high-volume rolling
            // window aggregates by SellerId+CreatedAt regardless of status.
            var tx = new Transaction
            {
                Id = Guid.NewGuid(),
                Status = TransactionStatus.COMPLETED,
                SellerId = _seller.Id,
                BuyerIdentificationMethod = BuyerIdentificationMethod.STEAM_ID,
                TargetBuyerSteamId = BuyerSteamId,
                ItemAssetId = $"asset-{i}",
                ItemClassId = $"class-{i}",
                ItemName = $"Backfill Item {i}",
                StablecoinType = StablecoinType.USDT,
                Price = eachAmount,
                CommissionRate = 0.02m,
                CommissionAmount = 0m,
                TotalAmount = eachAmount,
                SellerPayoutAddress = ValidWallet,
                PaymentTimeoutMinutes = 60,
                AcceptDeadline = nowUtc.AddHours(1),
            };
            Context.Set<Transaction>().Add(tx);
            ids.Add(tx.Id);
        }
        await Context.SaveChangesAsync();

        // Audit pipeline pinned CreatedAt to UtcNow on Add — bring it back.
        var seeded = await Context.Set<Transaction>().Where(t => ids.Contains(t.Id)).ToListAsync();
        foreach (var tx in seeded) tx.CreatedAt = stamp;
        await Context.SaveChangesAsync();
    }

    private async Task BackdateSellerAsync(Guid sellerId, int ageDays)
    {
        // Same two-stage trick — UpdateAuditFields only touches CreatedAt on
        // Added rows; an explicit Modified update lets us pin the timestamp.
        var seller = await Context.Set<User>().SingleAsync(u => u.Id == sellerId);
        seller.CreatedAt = _clock.GetUtcNow().UtcDateTime.AddDays(-ageDays);
        await Context.SaveChangesAsync();
    }

    private sealed class AlwaysClearFlagChecker : IAccountFlagChecker
    {
        public Task<bool> HasActiveAccountFlagAsync(Guid userId, CancellationToken cancellationToken)
            => Task.FromResult(false);
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

    /// <summary>
    /// Lightweight stub for <see cref="ITransactionFraudFlagWriter"/> — keeps
    /// the suite focused on T45 creation behaviour without dragging the
    /// Fraud module's full audit / outbox dependency graph into the test
    /// host. T54's own integration suite covers the writer's atomic
    /// FraudFlag + AuditLog + outbox semantics.
    /// </summary>
    private sealed class RecordingFraudFlagWriter : ITransactionFraudFlagWriter
    {
        public List<(Guid UserId, Guid TransactionId, FraudFlagType Type)> Calls { get; } = [];

        public Task StagePreCreateFlagAsync(
            Guid userId,
            Guid transactionId,
            FraudFlagType type,
            string details,
            CancellationToken cancellationToken)
        {
            Calls.Add((userId, transactionId, type));
            return Task.CompletedTask;
        }

        /// <summary>
        /// T129 added the account-level leg to the port. Creation never calls it
        /// (its only caller is the settlement reversal path), so recording with
        /// an empty transaction id keeps the fake honest: a creation test that
        /// ever saw a call here would be asserting against a real defect.
        /// </summary>
        public Task StageAccountFlagAsync(
            Guid userId,
            FraudFlagType type,
            string details,
            CancellationToken cancellationToken)
        {
            Calls.Add((userId, Guid.Empty, type));
            return Task.CompletedTask;
        }
    }
}
