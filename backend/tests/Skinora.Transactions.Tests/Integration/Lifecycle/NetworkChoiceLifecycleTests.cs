using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Skinora.Shared.Domain;
using Skinora.Shared.Enums;
using Skinora.Shared.Interfaces;
using Skinora.Shared.Persistence;
using Skinora.Shared.Tests.Integration;
using Skinora.Transactions.Application.GasFee;
using Skinora.Transactions.Application.Lifecycle;
using Skinora.Transactions.Application.PaymentAddresses;
using Skinora.Transactions.Application.Steam;
using Skinora.Transactions.Application.Timeouts;
using Skinora.Transactions.Application.Transfers;
using Skinora.Transactions.Application.Webhooks;
using Skinora.Transactions.Domain.Entities;
using Skinora.Transactions.Domain.StateMachine;
using Skinora.Transactions.Infrastructure.Persistence;
using Skinora.Transactions.Tests.Helpers;
using Skinora.Transactions.Tests.Integration.PaymentAddresses;
using Skinora.Users.Application.Settings;
using Skinora.Users.Application.Wallet;
using Skinora.Users.Domain.Entities;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.Transactions.Tests.Integration.Lifecycle;

/// <summary>
/// T147 — the state machine end to end on each network (11 §F8 K3, K9), on
/// real SQL Server: a listing that accepts both networks is created, the buyer
/// picks one, and every leg after that — deposit, payment, a rejected extra
/// transfer, payout — runs on the network the buyer chose. The Ethereum
/// service is a stub (T150 builds the real one): availability reports it open
/// and the sidecar stub derives an Ethereum-shaped deposit.
/// </summary>
/// <remarks>
/// The steps that do not depend on the network (seller readiness, delivery,
/// settlement) are driven through the state machine with the fields their
/// guards require; their own services are covered by their own suites.
/// </remarks>
public class NetworkChoiceLifecycleTests : IntegrationTestBase
{
    static NetworkChoiceLifecycleTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        TransactionsModuleDbRegistration.RegisterTransactionsModule();
        Skinora.Platform.Infrastructure.Persistence.PlatformModuleDbRegistration.RegisterPlatformModule();
    }

    private const string SellerSteamId = "76561198000000460";
    private const string BuyerSteamId = "76561198000000461";
    private const string ItemAssetId = "27348562899";
    private const ulong SteamId64ToId32Offset = 76561197960265728UL;

    // TRON: real base58check addresses (tronweb 5.3.5). Ethereum: EIP-55
    // specification vectors — the buyer's refund address is entered mixed
    // case and must be stored canonical (06 §1.4).
    private const string SellerTronPayout = "THfUtCxb8h8unh2SMVPjokNfNuVAZT16Ja";
    private const string SellerEthereumPayout = "0x5aaeb6053f3e94c9b9a09f33669435e7ef1beaed";
    private const string BuyerTronRefund = "TMwDwmmHe5vTFDEiGNWF8RGxic9b8a2sdY";
    private const string BuyerEthereumRefund = "0xfB6916095ca1df60bB79Ce92cE3Ea74c37c5d359";
    private const string BuyerEthereumRefundCanonical = "0xfb6916095ca1df60bb79ce92ce3ea74c37c5d359";

    // Where the buyer's money comes from — an exchange hot wallet (K9's reason).
    private const string ExchangeTronSender = "TSUj7JoASMqs36TxFnpK9zsHs7wi1ji2vE";
    private const string ExchangeEthereumSender = "0xdbf03b407c01e7cd3cbea99509d93f8dddc8c6fb";

    private const string TronDeposit = "TDepositT147Tron00000000000000000";
    private const string EthereumDeposit = "0xd1220a0cf47c7b9be7a2e6ba89f429762e7b9adb";

    private User _seller = null!;
    private User _buyer = null!;
    private FakeTimeProvider _clock = null!;
    private FakeSteamInventoryReader _inventory = null!;
    private NullOutbox _outbox = null!;
    private StubBlockchainSidecarClient _sidecar = null!;

    protected override async Task SeedAsync(AppDbContext context)
    {
        _seller = new User
        {
            Id = Guid.NewGuid(),
            SteamId = SellerSteamId,
            SteamDisplayName = "Seller",
            MobileAuthenticatorVerified = true,
            WalletAddresses =
            {
                UserWalletAddress.For(BlockchainNetwork.TRON, WalletAddressType.PAYOUT, SellerTronPayout),
                UserWalletAddress.For(BlockchainNetwork.ETHEREUM, WalletAddressType.PAYOUT, SellerEthereumPayout),
            },
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

        await context.ConfigureSettingAsync(TransactionLimitsProvider.MaxConcurrentKey, "5");
        await context.ConfigureSettingAsync(TransactionLimitsProvider.PaymentTimeoutMinKey, "360");
        await context.ConfigureSettingAsync(TransactionLimitsProvider.PaymentTimeoutMaxKey, "4320");
        await context.ConfigureSettingAsync(TransactionLimitsProvider.AcceptTimeoutKey, "60");
        await context.ConfigureSettingAsync(TransactionLimitsProvider.MinAmountKey, "10");
        await context.ConfigureSettingAsync(TransactionLimitsProvider.MaxAmountKey, "50000");
        await context.ConfigureSettingAsync(TransactionLimitsProvider.CommissionRateKey, "0.02");
        await context.ConfigureSettingAsync(TransactionLimitsProvider.OpenLinkEnabledKey, "false");

        _clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero));
        _inventory = new FakeSteamInventoryReader();
        _inventory.Register(SellerSteamId, new InventoryItemSnapshot(
            AssetId: ItemAssetId,
            ClassId: "abc-class",
            InstanceId: "abc-instance",
            Name: "AK-47 | Redline",
            MarketHashName: "AK-47 | Redline (Field-Tested)",
            IconUrl: null,
            Exterior: "Field-Tested",
            Type: "Rifle",
            InspectLink: null,
            IsTradeable: true));
        _outbox = new NullOutbox();
        _sidecar = new StubBlockchainSidecarClient();
    }

    public static TheoryData<BlockchainNetwork> Networks => new()
    {
        BlockchainNetwork.TRON,
        BlockchainNetwork.ETHEREUM,
    };

    [Theory]
    [MemberData(nameof(Networks))]
    public async Task Two_Network_Listing_Runs_From_Creation_To_Payout_On_The_Network_The_Buyer_Chose(
        BlockchainNetwork chosen)
    {
        var networks = FakeNetworkAvailability.All();
        var isEthereum = chosen == BlockchainNetwork.ETHEREUM;
        var sellerPayout = isEthereum ? SellerEthereumPayout : SellerTronPayout;
        var refundInput = isEthereum ? BuyerEthereumRefund : BuyerTronRefund;
        var refundStored = isEthereum ? BuyerEthereumRefundCanonical : BuyerTronRefund;
        var deposit = isEthereum ? EthereumDeposit : TronDeposit;
        var sender = isEthereum ? ExchangeEthereumSender : ExchangeTronSender;

        // ---- CREATED: the listing accepts both networks ----
        var created = await BuildCreation(networks).CreateAsync(_seller.Id, new CreateTransactionRequest(
            ItemAssetId: ItemAssetId,
            Stablecoin: StablecoinType.USDT,
            Price: "100.00",
            PaymentTimeoutHours: 24,
            BuyerIdentificationMethod: BuyerIdentificationMethod.STEAM_ID,
            BuyerSteamId: BuyerSteamId,
            AcceptedNetworks: ["TRON", "ETHEREUM"]), CancellationToken.None);
        Assert.Equal(CreateTransactionStatus.Created, created.Status);
        var id = created.Body!.Id;
        Assert.Equal(2, await Context.Set<TransactionNetworkOption>().CountAsync(o => o.TransactionId == id));
        Assert.False(await Context.Set<PaymentAddress>().AnyAsync(p => p.TransactionId == id));

        // ---- ACCEPTED on the chosen network; deposit derived on it ----
        _sidecar.Responses.Enqueue(new BlockchainSidecarDeriveResult(
            BlockchainSidecarStatus.Success, deposit, "m/44'/x'/0'/0/0"));
        var accepted = await BuildAcceptance(networks).AcceptAsync(_buyer.Id, id,
            new AcceptTransactionRequest(refundInput, TradeUrl(BuyerSteamId), Network: chosen.ToString()),
            CancellationToken.None);
        Assert.Equal(AcceptTransactionStatus.Accepted, accepted.Status);

        var tx = await Reload(id);
        Assert.Equal(TransactionStatus.ACCEPTED, tx.Status);
        Assert.Equal(chosen, tx.Network);
        Assert.Equal(sellerPayout, tx.SellerPayoutAddress);
        Assert.Equal(refundStored, tx.BuyerRefundAddress);
        var depositRow = await Context.Set<PaymentAddress>().AsNoTracking().SingleAsync(p => p.TransactionId == id);
        Assert.Equal(chosen, depositRow.Network);
        Assert.Equal(deposit, depositRow.Address);
        Assert.Equal(0, depositRow.HdWalletIndex);   // the chosen network's own counter
        Assert.Equal(102m, depositRow.ExpectedAmount);

        // ---- SELLER_CONFIRMED (readiness service covered by its own suite) ----
        tx = await TrackedAsync(id);
        tx.SellerReadyConfirmedAt = _clock.GetUtcNow().UtcDateTime;
        tx.PaymentDeadline = _clock.GetUtcNow().UtcDateTime.AddHours(24);
        new TransactionStateMachine(tx).Fire(TransactionTrigger.SellerConfirmReady);
        await Context.SaveChangesAsync();

        // ---- PAYMENT_RECEIVED: the exact amount arrives on the chosen network ----
        var amountValidation = BuildAmountValidation();
        var payment = await AddIncomingAsync(id, depositRow, sender, 102m);
        Assert.Equal(AmountValidationOutcome.AcceptedExact,
            await amountValidation.ValidateConfirmedBuyerPaymentAsync(payment, "corr-pay", CancellationToken.None));
        await Context.SaveChangesAsync();
        Assert.Equal(TransactionStatus.PAYMENT_RECEIVED, (await Reload(id)).Status);

        // ---- A stray extra transfer is refunded to the buyer's refund address (K9) ----
        var extra = await AddIncomingAsync(id, depositRow, sender, 5m);
        Assert.Equal(AmountValidationOutcome.MultiPaymentRefunded,
            await amountValidation.ValidateConfirmedBuyerPaymentAsync(extra, "corr-extra", CancellationToken.None));
        await Context.SaveChangesAsync();
        var refund = await Context.Set<BlockchainTransaction>().AsNoTracking()
            .SingleAsync(b => b.TransactionId == id && b.Type == BlockchainTransactionType.EXCESS_REFUND);
        Assert.Equal(refundStored, refund.ToAddress);
        Assert.NotEqual(sender, refund.ToAddress);

        // ---- ITEM_DELIVERED + settlement (their services have their own suites) ----
        tx = await TrackedAsync(id);
        var now = _clock.GetUtcNow().UtcDateTime;
        tx.BuyerConfirmedReceiptAt = now;
        tx.DeliveryEvidence = DeliveryEvidence.BUYER_CONFIRMED;
        tx.DeliveryVerifiedAt = now;
        tx.PayoutEligibleAt = now.AddMinutes(-1);
        new TransactionStateMachine(tx).Fire(TransactionTrigger.DeliverItem);
        tx.SettlementVerifiedAt = now;
        await Context.SaveChangesAsync();

        // ---- The deposit is swept on its network; then the seller is paid there ----
        await AddConfirmedSweepAsync(id, depositRow);
        await BuildPayoutQueue().ExecuteAsync();
        var payout = await Context.Set<BlockchainTransaction>().AsNoTracking()
            .SingleAsync(b => b.TransactionId == id && b.Type == BlockchainTransactionType.SELLER_PAYOUT);
        Assert.Equal(sellerPayout, payout.ToAddress);
        Assert.Equal(BlockchainTransactionStatus.PENDING, payout.Status);
    }

    // ---------------- helpers ----------------

    private static string TradeUrl(string steamId64) =>
        $"https://steamcommunity.com/tradeoffer/new/?partner={ulong.Parse(steamId64) - SteamId64ToId32Offset}&token=AbCdEfGh";

    private Task<Transaction> Reload(Guid id) =>
        Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == id);

    private Task<Transaction> TrackedAsync(Guid id) =>
        Context.Set<Transaction>().SingleAsync(t => t.Id == id);

    private async Task<BlockchainTransaction> AddIncomingAsync(
        Guid transactionId, PaymentAddress deposit, string from, decimal amount)
    {
        var row = new BlockchainTransaction
        {
            Id = Guid.NewGuid(),
            TransactionId = transactionId,
            PaymentAddressId = deposit.Id,
            Network = deposit.Network,
            Type = BlockchainTransactionType.BUYER_PAYMENT,
            TxHash = deposit.Network == BlockchainNetwork.ETHEREUM
                ? "0x" + Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N")
                : Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
            EventIndex = 0,
            FromAddress = from,
            ToAddress = deposit.Address,
            Amount = amount,
            Token = StablecoinType.USDT,
            Status = BlockchainTransactionStatus.CONFIRMED,
            BlockNumber = 1_000_000L,
            ConfirmationCount = 20,
            ConfirmedAt = _clock.GetUtcNow().UtcDateTime,
            CreatedAt = _clock.GetUtcNow().UtcDateTime,
        };
        Context.Set<BlockchainTransaction>().Add(row);
        await Context.SaveChangesAsync();
        return row;
    }

    private async Task AddConfirmedSweepAsync(Guid transactionId, PaymentAddress deposit)
    {
        var isEthereum = deposit.Network == BlockchainNetwork.ETHEREUM;
        Context.Set<BlockchainTransaction>().Add(new BlockchainTransaction
        {
            Id = Guid.NewGuid(),
            TransactionId = transactionId,
            PaymentAddressId = deposit.Id,
            Network = deposit.Network,
            Type = BlockchainTransactionType.SWEEP,
            TxHash = isEthereum
                ? "0x" + Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N")
                : Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
            FromAddress = deposit.Address,
            ToAddress = isEthereum ? "0x52908400098527886e0f7030069857d2e4169ee7" : "TXqH2JBkDgGWyCFg4GZzg8eUjG5JMZ7hPL",
            Amount = 102m,
            Token = StablecoinType.USDT,
            Status = BlockchainTransactionStatus.CONFIRMED,
            BlockNumber = 1_000_100L,
            ConfirmationCount = 20,
            ConfirmedAt = _clock.GetUtcNow().UtcDateTime,
            CreatedAt = _clock.GetUtcNow().UtcDateTime,
        });
        await Context.SaveChangesAsync();
    }

    private TransactionCreationService BuildCreation(FakeNetworkAvailability networks)
    {
        var limits = new TransactionLimitsProvider(Context);
        var eligibility = new TransactionEligibilityService(
            Context, limits, new ClearFlags(), new FakeSteamTradeEligibilityChecker(), _clock);
        return new TransactionCreationService(
            Context,
            eligibility,
            limits,
            _inventory,
            new FraudPreCheckService(Context, new FakeMarketPriceProvider()),
            new NoFraudFlags(),
            new WalletAddressValidator(),
            new NoMatchWalletSanctionsCheck(),
            new InvitationCodeGenerator(),
            _outbox,
            new NullSteamInventoryCacheInvalidator(),
            networks,
            NullLogger<TransactionCreationService>.Instance,
            _clock);
    }

    private TransactionAcceptanceService BuildAcceptance(FakeNetworkAvailability networks) => new(
        Context,
        new WalletAddressValidator(),
        new NoMatchWalletSanctionsCheck(),
        new ClearFlags(),
        new TradeUrlParser(),
        new ActiveTradeHold(),
        new FakeSteamTradeEligibilityChecker(),
        _outbox,
        networks,
        new PaymentAddressAllocator(Context, _sidecar, NullLogger<PaymentAddressAllocator>.Instance),
        _clock);

    private AmountValidationService BuildAmountValidation() => new(
        Context,
        new FixedGasFee(),
        new RefundDecisionService(new FixedGasFeeSettings()),
        new NoRefundBlockedAlerts(),
        _outbox,
        new NoTimeoutScheduling(),
        _clock,
        Options.Create(new StablecoinContractOptions()),
        NullLogger<AmountValidationService>.Instance);

    private SellerPayoutQueueJob BuildPayoutQueue() => new(
        Context,
        new RefundDecisionService(new FixedGasFeeSettings()),
        new FixedGasFee(),
        _outbox,
        _clock,
        NullLogger<SellerPayoutQueueJob>.Instance);

    private sealed class NullOutbox : IOutboxService
    {
        public Task PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class ClearFlags : IAccountFlagChecker
    {
        public Task<bool> HasActiveAccountFlagAsync(Guid userId, CancellationToken cancellationToken)
            => Task.FromResult(false);
        public Task<bool> HasPendingAccountFlagAsync(Guid userId, FraudFlagType type, CancellationToken cancellationToken)
            => Task.FromResult(false);
    }

    private sealed class NoFraudFlags : ITransactionFraudFlagWriter
    {
        public Task StagePreCreateFlagAsync(Guid userId, Guid transactionId, FraudFlagType type, string details, CancellationToken cancellationToken)
            => Task.CompletedTask;
        public Task StageAccountFlagAsync(Guid userId, FraudFlagType type, string details, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class ActiveTradeHold : ITradeHoldChecker
    {
        public Task<TradeHoldResult> CheckAsync(string steamId64, string tradeOfferAccessToken, CancellationToken cancellationToken)
            => Task.FromResult(new TradeHoldResult(true, true, null));
    }

    private sealed class FixedGasFee : IChargedGasFeeResolver
    {
        public Task<ResolvedGasFee> ResolveRefundFeeAsync(
            string? fromDepositAddress, string toAddress, decimal amount, StablecoinType token, CancellationToken cancellationToken)
            => Task.FromResult(new ResolvedGasFee(0.50m, GasFeeSource.RuntimeEstimate));
        public Task<ResolvedGasFee> ResolvePayoutFeeAsync(
            string toAddress, decimal amount, StablecoinType token, CancellationToken cancellationToken)
            => Task.FromResult(new ResolvedGasFee(0.50m, GasFeeSource.RuntimeEstimate));
    }

    private sealed class FixedGasFeeSettings : IGasFeeSettingsProvider
    {
        public Task<GasFeeSettings> GetAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new GasFeeSettings(
                ProtectionRatio: 0.10m,
                MinRefundThresholdRatio: 2m,
                RefundGasFeeEstimateUsdt: 0.50m,
                PayoutGasFeeEstimateUsdt: 0.50m,
                MaxChargedGasFeeUsdt: 10m));
    }

    private sealed class NoRefundBlockedAlerts : IRefundBlockedAlertService
    {
        public Task RaiseAsync(Guid transactionId, RefundDecision decision, CancellationToken cancellationToken)
            => throw new InvalidOperationException($"Unexpected refund block: {decision.Reason}.");
    }

    private sealed class NoTimeoutScheduling : ITimeoutSchedulingService
    {
        public Task<TimeoutJobIds> SchedulePaymentTimeoutAsync(Guid transactionId, CancellationToken cancellationToken)
            => Task.FromResult(new TimeoutJobIds("p", "w"));
        public Task CancelTimeoutJobsAsync(Guid transactionId, CancellationToken cancellationToken)
            => Task.CompletedTask;
        public Task<TimeoutJobIds> ReschedulePaymentTimeoutAsync(
            Guid transactionId, TimeSpan remaining, DateTime newPaymentDeadlineUtc, CancellationToken cancellationToken)
            => Task.FromResult(new TimeoutJobIds("p", "w"));
        public Task<DateTime> ArmDeliveryDeadlineAsync(Guid transactionId, CancellationToken cancellationToken)
            => Task.FromResult(new DateTime(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc));
    }
}
