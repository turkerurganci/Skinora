using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Skinora.Shared.Domain;
using Skinora.Shared.Enums;
using Skinora.Shared.Interfaces;
using Skinora.Shared.Persistence;
using Skinora.Shared.Persistence.Outbox;
using Skinora.Shared.Tests.Integration;
using Skinora.Transactions.Application.Lifecycle;
using Skinora.Transactions.Application.Networks;
using Skinora.Transactions.Tests.Helpers;
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

    // A real base58check TRON address (T146 — creation re-validates the
    // profile address with the checksum); tronweb 5.3.5 fromHex.
    private const string ValidWallet = "THfUtCxb8h8unh2SMVPjokNfNuVAZT16Ja";
    private const string SellerSteamId = "76561198000000060";
    private const string BuyerSteamId = "76561198000000061";
    private const string ItemAssetId = "27348562891";

    private User _seller = null!;
    private FakeTimeProvider _clock = null!;
    private FakeSteamInventoryReader _inventory = null!;
    private FakeMarketPriceProvider _marketPrice = null!;
    private RecordingOutboxService _outbox = null!;
    private IBlockchainNetworkAvailability _networks = null!;

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
            WalletAddresses = { UserWalletAddress.For(BlockchainNetwork.TRON, WalletAddressType.PAYOUT, ValidWallet) },
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
        _networks = TestNetworkAvailability.TronOnly();

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
        // T147 — the payout snapshot lives in the TRON option row until the
        // buyer picks a network; the column is filled at acceptance.
        Assert.Null(persisted.SellerPayoutAddress);
        Assert.Null(persisted.Network);
        Assert.Equal(ValidWallet, await OptionAddressAsync(persisted.Id, BlockchainNetwork.TRON));
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

    [Theory]
    // T144 — the persisted payable is whole-cent (commission truncated to the
    // cent). Before T144: 10.55 → 0.211 / 10.761, 10.25 → 0.205 / 10.455.
    [InlineData("10.55", "0.21", "10.76")]
    [InlineData("10.25", "0.20", "10.45")]
    [InlineData("99.99", "1.99", "101.98")]
    [InlineData("250.75", "5.01", "255.76")]
    public async Task Persists_Whole_Cent_Commission_And_Total(
        string price, string commission, string total)
    {
        var sut = BuildSut();
        _marketPrice.Price = decimal.Parse(price, System.Globalization.CultureInfo.InvariantCulture);

        var outcome = await sut.CreateAsync(
            _seller.Id, ValidRequest() with { Price = price }, CancellationToken.None);

        Assert.Equal(CreateTransactionStatus.Created, outcome.Status);
        var persisted = await Context.Set<Transaction>()
            .AsNoTracking()
            .SingleAsync(t => t.Id == outcome.Body!.Id);
        Assert.Equal(decimal.Parse(commission, System.Globalization.CultureInfo.InvariantCulture), persisted.CommissionAmount);
        Assert.Equal(decimal.Parse(total, System.Globalization.CultureInfo.InvariantCulture), persisted.TotalAmount);
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
    public async Task Rejects_Usdc_Stablecoin()
    {
        // 11 §F8 K2 (T166) — the enum still knows USDC (wrong-token refunds,
        // existing rows) but a new listing may not choose it.
        var sut = BuildSut();
        var outcome = await sut.CreateAsync(
            _seller.Id, ValidRequest() with { Stablecoin = StablecoinType.USDC }, CancellationToken.None);

        Assert.Equal(CreateTransactionStatus.ValidationFailed, outcome.Status);
        Assert.Equal(TransactionErrorCodes.ValidationError, outcome.ErrorCode);
        Assert.False(await Context.Set<Transaction>().AnyAsync(t => t.SellerId == _seller.Id));
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

    /// <summary>
    /// The format check survives the move to the profile because rows can reach
    /// the TRON <c>UserWalletAddress</c> PAYOUT row without passing <c>WalletAddressService</c>
    /// (migrations, seeds, the e2e harness writes it in raw SQL). The request
    /// body can no longer carry an address at all, so the malformed value is
    /// planted on the profile — which is the only way it can now occur.
    /// </summary>
    [Theory]
    [InlineData("NOT_A_TRC20_ADDRESS")]
    // T146 — right shape, broken base58check checksum (last character of
    // the USDT contract address changed; tronweb isAddress = false).
    [InlineData("TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6u")]
    public async Task Rejects_Invalid_Wallet_Format_On_Seller_Profile(string plantedAddress)
    {
        await SetTronPayoutAsync(plantedAddress);

        var sut = BuildSut();
        var outcome = await sut.CreateAsync(_seller.Id, ValidRequest(), CancellationToken.None);

        Assert.Equal(CreateTransactionStatus.InvalidWallet, outcome.Status);
        Assert.Equal(TransactionErrorCodes.InvalidWalletAddress, outcome.ErrorCode);
    }

    /// <summary>
    /// The payout address is a snapshot of the PROFILE, never of anything the
    /// caller supplies (02 §12.3). Pinned because the request body used to carry
    /// the address and the payout job pays whatever lands in this column
    /// (<c>SellerPayoutQueueJob</c>), so the two controls on the profile write
    /// path — Steam re-auth and the cooldown — guarded a value that was never
    /// the one paid.
    /// </summary>
    [Fact]
    public async Task Snapshots_Payout_Address_From_Seller_Profile()
    {
        // A valid TRON address — and deliberately NOT ValidWallet, so the
        // assertion fails if the column is fed from anywhere but this profile.
        const string profileAddress = "TGepRwJFoonb8B1Cfq66dUo6ZQFiBZES5L";
        Assert.NotEqual(ValidWallet, profileAddress);
        await SetTronPayoutAsync(profileAddress);
        // T146/T147 — another network's payout address is not snapshotted when
        // the listing accepts only TRON (the default when the body names none).
        var ethereumPayout = UserWalletAddress.For(
            BlockchainNetwork.ETHEREUM, WalletAddressType.PAYOUT, "0x5aaeb6053f3e94c9b9a09f33669435e7ef1beaed");
        ethereumPayout.UserId = _seller.Id;
        Context.Set<UserWalletAddress>().Add(ethereumPayout);
        await Context.SaveChangesAsync();

        var sut = BuildSut();
        var outcome = await sut.CreateAsync(_seller.Id, ValidRequest(), CancellationToken.None);

        Assert.Equal(CreateTransactionStatus.Created, outcome.Status);
        Assert.NotNull(outcome.Body);
        var persisted = await Context.Set<Transaction>().AsNoTracking()
            .SingleAsync(t => t.Id == outcome.Body!.Id);
        Assert.Equal(profileAddress, await OptionAddressAsync(persisted.Id, BlockchainNetwork.TRON));
        Assert.Null(await OptionAddressAsync(persisted.Id, BlockchainNetwork.ETHEREUM));
    }

    private const string EthereumPayoutAddress = "0x5aaeb6053f3e94c9b9a09f33669435e7ef1beaed";

    /// <summary>
    /// T147 — a body without <c>acceptedNetworks</c> lists on TRON (⚠ default),
    /// so a seller who only has an ETHEREUM payout address has no payout
    /// address for it; the rejection names the network (07 §1.3).
    /// </summary>
    [Fact]
    public async Task EthereumOnlySeller_Gets_SellerWalletAddressMissing()
    {
        var tronRow = await Context.Set<UserWalletAddress>()
            .SingleAsync(w => w.UserId == _seller.Id && w.Network == BlockchainNetwork.TRON);
        Context.Set<UserWalletAddress>().Remove(tronRow);
        var ethereumPayout = UserWalletAddress.For(
            BlockchainNetwork.ETHEREUM, WalletAddressType.PAYOUT, EthereumPayoutAddress);
        ethereumPayout.UserId = _seller.Id;
        Context.Set<UserWalletAddress>().Add(ethereumPayout);
        await Context.SaveChangesAsync();

        var outcome = await BuildSut().CreateAsync(_seller.Id, ValidRequest(), CancellationToken.None);

        Assert.Equal(CreateTransactionStatus.SellerWalletAddressMissing, outcome.Status);
        Assert.Equal(TransactionErrorCodes.SellerWalletAddressMissing, outcome.ErrorCode);
        Assert.Equal("TRON", NetworkOf(outcome.ErrorDetails));
    }

    [Fact]
    public async Task SellerWithTronAndEthereumPayout_Snapshots_The_Tron_Address()
    {
        var ethereumPayout = UserWalletAddress.For(
            BlockchainNetwork.ETHEREUM, WalletAddressType.PAYOUT, EthereumPayoutAddress);
        ethereumPayout.UserId = _seller.Id;
        Context.Set<UserWalletAddress>().Add(ethereumPayout);
        await Context.SaveChangesAsync();

        var outcome = await BuildSut().CreateAsync(_seller.Id, ValidRequest(), CancellationToken.None);

        Assert.Equal(CreateTransactionStatus.Created, outcome.Status);
        var persisted = await Context.Set<Transaction>().AsNoTracking()
            .SingleAsync(t => t.Id == outcome.Body!.Id);
        Assert.Equal(ValidWallet, await OptionAddressAsync(persisted.Id, BlockchainNetwork.TRON));
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
    public async Task Rejects_With_InventoryNotFound_When_The_Seller_Has_No_Cs2_Inventory()
    {
        // P2P-InventoryUnauthorizedMapping — Steam's 401 is Unavailable to the
        // evidence paths but permanent: the seller is told the account has no
        // CS2 inventory instead of being sent to retry (STEAM_UNAVAILABLE) or
        // to look for the item (ITEM_NOT_IN_INVENTORY).
        _inventory.ForcedNoInventory = true;

        var sut = BuildSut();
        var outcome = await sut.CreateAsync(_seller.Id, ValidRequest(), CancellationToken.None);

        Assert.Equal(CreateTransactionStatus.InventoryNotFound, outcome.Status);
        Assert.Equal(TransactionErrorCodes.InventoryNotFound, outcome.ErrorCode);
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

    // ---- 08 §2.2a — Steam trade eligibility on the create path ----------------
    //
    // Each of the three Steam reasons leaves the eligibility envelope with its
    // OWN status, because the controller answers them differently: the two
    // refusals are 403 (an account-level permission on Steam's side) while
    // "could not ask" is a retryable 503. Falling into the shared
    // EligibilityFailed status would answer all three with 422 and tell a
    // seller to fix something Steam never said was wrong.

    [Fact]
    public async Task Rejects_Limited_Steam_Account_With_Its_Own_Status()
    {
        var sut = BuildSut(FakeSteamTradeEligibilityChecker.Limited());

        var outcome = await sut.CreateAsync(_seller.Id, ValidRequest(), CancellationToken.None);

        Assert.Equal(CreateTransactionStatus.SteamAccountLimited, outcome.Status);
        Assert.Equal(TransactionErrorCodes.SteamAccountLimited, outcome.ErrorCode);
        Assert.Null(outcome.Body);
    }

    [Fact]
    public async Task Rejects_Too_New_Steam_Account_With_Its_Own_Status()
    {
        var sut = BuildSut(FakeSteamTradeEligibilityChecker.TooNew(remainingDays: 4));

        var outcome = await sut.CreateAsync(_seller.Id, ValidRequest(), CancellationToken.None);

        Assert.Equal(CreateTransactionStatus.SteamAccountTooNew, outcome.Status);
        Assert.Equal(TransactionErrorCodes.SteamAccountTooNew, outcome.ErrorCode);
        Assert.Null(outcome.Body);
    }

    [Fact]
    public async Task Rejects_Unreadable_Steam_Answer_As_Retryable_Not_As_Refusal()
    {
        var sut = BuildSut(FakeSteamTradeEligibilityChecker.Unknown());

        var outcome = await sut.CreateAsync(_seller.Id, ValidRequest(), CancellationToken.None);

        Assert.Equal(CreateTransactionStatus.SteamUnavailable, outcome.Status);
        Assert.Equal(TransactionErrorCodes.SteamUnavailable, outcome.ErrorCode);
        Assert.Null(outcome.Body);
    }

    // ---- T147 — no deposit address at creation --------------------------------

    [Fact]
    public async Task Derives_No_Deposit_Address_At_Creation_The_Network_Is_Chosen_At_Acceptance()
    {
        // Until T147 the TRON deposit was derived here (T70). The buyer now
        // picks the network at acceptance and the deposit lives on it
        // (06 §1.4), so a fresh listing has option rows and no address.
        var sut = BuildSut();
        var outcome = await sut.CreateAsync(_seller.Id, ValidRequest(), CancellationToken.None);

        Assert.Equal(CreateTransactionStatus.Created, outcome.Status);
        Assert.Equal(TransactionStatus.CREATED, outcome.Body!.Status);
        Assert.False(await Context.Set<PaymentAddress>().IgnoreQueryFilters()
            .AnyAsync(p => p.TransactionId == outcome.Body.Id));
        var persisted = await Context.Set<Transaction>().AsNoTracking()
            .SingleAsync(t => t.Id == outcome.Body.Id);
        Assert.Null(persisted.Network);
        Assert.Null(persisted.SellerPayoutAddress);
        var option = Assert.Single(await Context.Set<TransactionNetworkOption>().AsNoTracking()
            .Where(o => o.TransactionId == outcome.Body.Id).ToListAsync());
        Assert.Equal(BlockchainNetwork.TRON, option.Network);
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
            WalletAddresses = { UserWalletAddress.For(BlockchainNetwork.TRON, WalletAddressType.PAYOUT, ValidWallet) },
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

    // ---- T147 — accepted networks (07 §1.3, 11 §F8 K3) -----------------------

    private async Task AddEthereumPayoutAsync(DateTime? changedAt = null)
    {
        var row = UserWalletAddress.For(
            BlockchainNetwork.ETHEREUM, WalletAddressType.PAYOUT, EthereumPayoutAddress, changedAt);
        row.UserId = _seller.Id;
        Context.Set<UserWalletAddress>().Add(row);
        await Context.SaveChangesAsync();
    }

    private CreateTransactionRequest RequestFor(params string[] networks) =>
        ValidRequest() with { AcceptedNetworks = networks };

    [Fact]
    public async Task Listing_On_Both_Networks_Snapshots_Each_Networks_Payout_Address()
    {
        _networks = TestNetworkAvailability.All();
        await AddEthereumPayoutAsync();

        // Client order does not matter; rows are per network.
        var outcome = await BuildSut().CreateAsync(_seller.Id, RequestFor("ETHEREUM", "TRON"), CancellationToken.None);

        Assert.Equal(CreateTransactionStatus.Created, outcome.Status);
        var id = outcome.Body!.Id;
        Assert.Equal(ValidWallet, await OptionAddressAsync(id, BlockchainNetwork.TRON));
        Assert.Equal(EthereumPayoutAddress, await OptionAddressAsync(id, BlockchainNetwork.ETHEREUM));
        var persisted = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == id);
        Assert.Null(persisted.Network);
        Assert.Null(persisted.SellerPayoutAddress);
    }

    [Fact]
    public async Task Listing_On_Ethereum_Only_Writes_Just_That_Option()
    {
        _networks = TestNetworkAvailability.All();
        await AddEthereumPayoutAsync();

        var outcome = await BuildSut().CreateAsync(_seller.Id, RequestFor("ETHEREUM"), CancellationToken.None);

        Assert.Equal(CreateTransactionStatus.Created, outcome.Status);
        var networks = await Context.Set<TransactionNetworkOption>().AsNoTracking()
            .Where(o => o.TransactionId == outcome.Body!.Id).Select(o => o.Network).ToListAsync();
        Assert.Equal([BlockchainNetwork.ETHEREUM], networks);
    }

    [Fact]
    public async Task Listing_On_A_Network_Without_A_Payout_Address_Is_Refused_Naming_It()
    {
        // 11 §F8 K3: only networks with a registered payout address.
        _networks = TestNetworkAvailability.All();

        var outcome = await BuildSut().CreateAsync(_seller.Id, RequestFor("TRON", "ETHEREUM"), CancellationToken.None);

        Assert.Equal(CreateTransactionStatus.SellerWalletAddressMissing, outcome.Status);
        Assert.Equal(TransactionErrorCodes.SellerWalletAddressMissing, outcome.ErrorCode);
        Assert.Equal("ETHEREUM", NetworkOf(outcome.ErrorDetails));
        Assert.False(await Context.Set<Transaction>().AnyAsync(t => t.SellerId == _seller.Id));
    }

    [Fact]
    public async Task Listing_On_A_Network_Without_A_Service_Fails_Closed_With_Network_Unavailable()
    {
        // Ethereum has no service until T150 — production answer today.
        await AddEthereumPayoutAsync();

        var outcome = await BuildSut().CreateAsync(_seller.Id, RequestFor("TRON", "ETHEREUM"), CancellationToken.None);

        Assert.Equal(CreateTransactionStatus.NetworkUnavailable, outcome.Status);
        Assert.Equal(TransactionErrorCodes.NetworkUnavailable, outcome.ErrorCode);
        Assert.Equal("ETHEREUM", NetworkOf(outcome.ErrorDetails));
        Assert.False(await Context.Set<Transaction>().AnyAsync(t => t.SellerId == _seller.Id));
        Assert.Empty(_outbox.Published);
    }

    [Fact]
    public async Task Listing_On_A_Frozen_Network_Fails_Closed_With_Network_Unavailable()
    {
        // T148 health freeze (11 §6.2 FK8-6): a configured network in a
        // detected outage is closed for new listings like one without a service.
        _networks = TestNetworkAvailability.AllWithFrozen(BlockchainNetwork.ETHEREUM);
        await AddEthereumPayoutAsync();

        var outcome = await BuildSut().CreateAsync(_seller.Id, RequestFor("TRON", "ETHEREUM"), CancellationToken.None);

        Assert.Equal(CreateTransactionStatus.NetworkUnavailable, outcome.Status);
        Assert.Equal(TransactionErrorCodes.NetworkUnavailable, outcome.ErrorCode);
        Assert.Equal("ETHEREUM", NetworkOf(outcome.ErrorDetails));
        Assert.False(await Context.Set<Transaction>().AnyAsync(t => t.SellerId == _seller.Id));
        Assert.Empty(_outbox.Published);
    }

    public static TheoryData<string[]> MalformedAcceptedNetworks => new()
    {
        Array.Empty<string>(),
        new[] { "TRON", "TRON" },
        new[] { "BSC" },
        new[] { "tron" },
        new[] { "" },
    };

    [Theory]
    [MemberData(nameof(MalformedAcceptedNetworks))]
    public async Task Malformed_AcceptedNetworks_Is_A_Validation_Error(string[] networks)
    {
        var outcome = await BuildSut().CreateAsync(_seller.Id, RequestFor(networks), CancellationToken.None);

        Assert.Equal(CreateTransactionStatus.ValidationFailed, outcome.Status);
        Assert.Equal(TransactionErrorCodes.ValidationError, outcome.ErrorCode);
    }

    [Fact]
    public async Task Payout_Cooldown_Is_Checked_Per_Accepted_Network()
    {
        // 02 §12.3, per network (T146): a fresh Ethereum payout address blocks
        // a listing that accepts Ethereum and names it; a TRON-only listing of
        // the same seller still goes through.
        await Context.ConfigureSettingAsync(TransactionLimitsProvider.PayoutCooldownKey, "24");
        _networks = TestNetworkAvailability.All();
        await AddEthereumPayoutAsync(changedAt: _clock.GetUtcNow().UtcDateTime.AddHours(-1));

        var both = await BuildSut().CreateAsync(_seller.Id, RequestFor("TRON", "ETHEREUM"), CancellationToken.None);
        Assert.Equal(CreateTransactionStatus.PayoutAddressCooldownActive, both.Status);
        Assert.Equal(TransactionErrorCodes.PayoutAddressCooldownActive, both.ErrorCode);
        Assert.Equal("ETHEREUM", NetworkOf(both.ErrorDetails));

        var tronOnly = await BuildSut().CreateAsync(_seller.Id, RequestFor("TRON"), CancellationToken.None);
        Assert.Equal(CreateTransactionStatus.Created, tronOnly.Status);
    }

    [Fact]
    public async Task Sanctions_Are_Screened_On_Each_Accepted_Networks_Payout_Address()
    {
        _networks = TestNetworkAvailability.All();
        await AddEthereumPayoutAsync();
        var sanctions = new NetworkMatchingSanctionsCheck(BlockchainNetwork.ETHEREUM);

        var outcome = await BuildSut(sanctions: sanctions)
            .CreateAsync(_seller.Id, RequestFor("TRON", "ETHEREUM"), CancellationToken.None);

        Assert.Equal(CreateTransactionStatus.SanctionsMatch, outcome.Status);
        Assert.Equal("ETHEREUM", NetworkOf(outcome.ErrorDetails));
        Assert.Contains((BlockchainNetwork.ETHEREUM, EthereumPayoutAddress), sanctions.Screened);
        Assert.Contains((BlockchainNetwork.TRON, ValidWallet), sanctions.Screened);
    }

    private sealed class NetworkMatchingSanctionsCheck : IWalletSanctionsCheck
    {
        private readonly BlockchainNetwork _matching;
        public NetworkMatchingSanctionsCheck(BlockchainNetwork matching) => _matching = matching;
        public List<(BlockchainNetwork, string)> Screened { get; } = [];

        public Task<WalletSanctionsDecision> EvaluateAsync(
            BlockchainNetwork network, string walletAddress, CancellationToken cancellationToken)
        {
            Screened.Add((network, walletAddress));
            return Task.FromResult(network == _matching
                ? WalletSanctionsDecision.Match("TEST_LIST")
                : WalletSanctionsDecision.NoMatch());
        }
    }

    private TransactionCreationService BuildSut(
        FakeSteamTradeEligibilityChecker? steamEligibility = null,
        IWalletSanctionsCheck? sanctions = null)
    {
        var limits = new TransactionLimitsProvider(Context);
        var eligibility = new TransactionEligibilityService(
            Context,
            limits,
            new AlwaysClearFlagChecker(),
            steamEligibility ?? new FakeSteamTradeEligibilityChecker(),
            _clock);
        var fraud = new FraudPreCheckService(Context, _marketPrice);
        return new TransactionCreationService(
            Context,
            eligibility,
            limits,
            _inventory,
            fraud,
            new RecordingFraudFlagWriter(),
            new WalletAddressValidator(),
            sanctions ?? new NoMatchWalletSanctionsCheck(),
            _inviteCodes,
            _outbox,
            new NullSteamInventoryCacheInvalidator(),
            _networks,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<TransactionCreationService>.Instance,
            _clock);
    }

    private Task<string?> OptionAddressAsync(Guid transactionId, BlockchainNetwork network) =>
        Context.Set<TransactionNetworkOption>().AsNoTracking()
            .Where(o => o.TransactionId == transactionId && o.Network == network)
            .Select(o => (string?)o.SellerPayoutAddress)
            .SingleOrDefaultAsync();

    private static string? NetworkOf(object? details) =>
        details?.GetType().GetProperty("network")?.GetValue(details) as string;

    private async Task SetTronPayoutAsync(string address)
    {
        var row = await Context.Set<UserWalletAddress>()
            .SingleAsync(w => w.UserId == _seller.Id
                              && w.Network == BlockchainNetwork.TRON
                              && w.Type == WalletAddressType.PAYOUT);
        row.Address = address;
        await Context.SaveChangesAsync();
    }

    private CreateTransactionRequest ValidRequest() => new(
        ItemAssetId: ItemAssetId,
        Stablecoin: StablecoinType.USDT,
        Price: "100.00",
        PaymentTimeoutHours: 24,
        BuyerIdentificationMethod: BuyerIdentificationMethod.STEAM_ID,
        BuyerSteamId: BuyerSteamId);

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
        public Task<bool> HasPendingAccountFlagAsync(Guid userId, FraudFlagType type, CancellationToken cancellationToken)
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
