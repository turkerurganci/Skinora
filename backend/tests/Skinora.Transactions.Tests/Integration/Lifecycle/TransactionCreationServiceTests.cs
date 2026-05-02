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

        _inventory.Register(SellerSteamId, new InventoryItemSnapshot(
            AssetId: ItemAssetId,
            ClassId: "abc-class",
            InstanceId: "abc-instance",
            Name: "AK-47 | Redline",
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
    public async Task Rejects_When_Item_Has_Trade_Lock()
    {
        _inventory.Register(SellerSteamId, new InventoryItemSnapshot(
            "locked", "c", null, "Locked Item", null, null, null, null, IsTradeable: false));

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
            new Trc20AddressValidator(),
            new NoMatchWalletSanctionsCheck(),
            new InvitationCodeGenerator(),
            _outbox,
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
}
