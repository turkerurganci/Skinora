using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Skinora.Disputes.Application.AutoCheckers;
using Skinora.Disputes.Application.Disputes;
using Skinora.Disputes.Domain.Entities;
using Skinora.Disputes.Infrastructure.Persistence;
using Skinora.Shared.Domain;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Interfaces;
using Skinora.Shared.Persistence;
using Skinora.Shared.Tests.Integration;
using Skinora.Steam.Domain.Entities;
using Skinora.Steam.Infrastructure.Persistence;
using Skinora.Transactions.Application.Steam;
using Skinora.Transactions.Domain.Entities;
using Skinora.Transactions.Infrastructure.Persistence;
using Skinora.Users.Domain.Entities;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.Disputes.Tests.Integration;

/// <summary>
/// End-to-end coverage for <see cref="DisputeService"/> (T58 — 02 §10,
/// 03 §6, 07 §7.8–§7.10). Exercises every type-specific auto-checker and
/// every error code listed under 07 §7.8–§7.10 "Hatalar" against a real
/// SQL Server instance.
/// </summary>
public class DisputeServiceTests : IntegrationTestBase
{
    static DisputeServiceTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        TransactionsModuleDbRegistration.RegisterTransactionsModule();
        SteamModuleDbRegistration.RegisterSteamModule();
        DisputesModuleDbRegistration.RegisterDisputesModule();
    }

    private const string SellerSteam = "76561198000000301";
    private const string BuyerSteam = "76561198000000302";
    private const string SellerWallet = "TXqH2JBkDgGWyCFg4GZzg8eUjG5JMZ7hPL";

    private FakeTimeProvider _clock = null!;
    private RecordingOutboxService _outbox = null!;
    private FakeInventoryReader _inventory = null!;
    private User _seller = null!;
    private User _buyer = null!;
    private User _otherUser = null!;
    private PlatformSteamBot _bot = null!;

    protected override async Task SeedAsync(AppDbContext context)
    {
        _clock = new FakeTimeProvider(new DateTimeOffset(2026, 5, 5, 12, 0, 0, TimeSpan.Zero));
        _outbox = new RecordingOutboxService();
        _inventory = new FakeInventoryReader();

        _seller = new User
        {
            Id = Guid.NewGuid(),
            SteamId = SellerSteam,
            SteamDisplayName = "Seller",
            DefaultPayoutAddress = SellerWallet,
            MobileAuthenticatorVerified = true,
        };
        _buyer = new User
        {
            Id = Guid.NewGuid(),
            SteamId = BuyerSteam,
            SteamDisplayName = "Buyer",
            MobileAuthenticatorVerified = true,
        };
        _otherUser = new User
        {
            Id = Guid.NewGuid(),
            SteamId = "76561198000000999",
            SteamDisplayName = "Stranger",
        };
        context.Set<User>().AddRange(_seller, _buyer, _otherUser);

        _bot = new PlatformSteamBot
        {
            Id = Guid.NewGuid(),
            SteamId = "76561198000000400",
            DisplayName = "Bot",
            Status = PlatformSteamBotStatus.ACTIVE,
        };
        context.Set<PlatformSteamBot>().Add(_bot);

        await context.SaveChangesAsync();
    }

    // ---------- Open ▸ PAYMENT ----------

    [Fact]
    public async Task Open_Payment_NoConfirmedPayment_StaysOpen_AndFlagsTransaction()
    {
        var tx = await CreateTransactionAsync(TransactionStatus.ITEM_ESCROWED);
        var sut = BuildSut();

        var outcome = await sut.OpenAsync(_buyer.Id, tx.Id,
            new OpenDisputeRequest(DisputeType.PAYMENT), CancellationToken.None);

        Assert.Equal(OpenDisputeStatus.Opened, outcome.Status);
        Assert.NotNull(outcome.Body);
        Assert.Equal(DisputeStatus.OPEN, outcome.Body.Status);
        Assert.False(outcome.Body.AutoCheckResult.Resolved);
        Assert.True(outcome.Body.AutoCheckResult.CanSubmitTxHash);
        Assert.True(outcome.Body.AutoCheckResult.CanEscalate);

        var persisted = await Context.Set<Dispute>().AsNoTracking()
            .FirstAsync(d => d.Id == outcome.Body.Id);
        Assert.Equal(DisputeStatus.OPEN, persisted.Status);
        Assert.Null(persisted.ResolvedAt);
        Assert.Equal(DisputeType.PAYMENT, persisted.Type);

        var refreshedTx = await Context.Set<Transaction>().AsNoTracking()
            .FirstAsync(t => t.Id == tx.Id);
        Assert.True(refreshedTx.HasActiveDispute);

        Assert.Empty(_outbox.Published);
    }

    // WP17 — a non-default buyer locale flows through DisputeService end-to-end
    // (proves the ResolveLocaleAsync -> DisputeAutoCheckMessages.Localize wiring,
    // not just the "en" default the other tests exercise).
    [Fact]
    public async Task Open_Then_Escalate_BuyerLocaleTr_LocalizesStoredResponseAndEvent()
    {
        var buyer = await Context.Set<User>().FirstAsync(u => u.Id == _buyer.Id);
        buyer.PreferredLanguage = "tr";
        await Context.SaveChangesAsync();

        var tx = await CreateTransactionAsync(TransactionStatus.ITEM_ESCROWED);
        var sut = BuildSut();

        // Open PAYMENT (no confirmed payment) → unresolved, Turkish message stored + returned.
        var open = await sut.OpenAsync(_buyer.Id, tx.Id,
            new OpenDisputeRequest(DisputeType.PAYMENT), CancellationToken.None);
        Assert.Equal("Blockchain üzerinde ödeme bulunamadı", open.Body!.AutoCheckResult.Message);

        var persisted = await Context.Set<Dispute>().AsNoTracking()
            .FirstAsync(d => d.Id == open.Body.Id);
        Assert.Equal("Blockchain üzerinde ödeme bulunamadı", persisted.SystemCheckResult);

        // Escalate → response message AND the emitted event OutcomeText both Turkish.
        var esc = await sut.EscalateAsync(_buyer.Id, tx.Id, open.Body.Id,
            new EscalateDisputeRequest("Ödemeyi gönderdim ama sistem görmüyor"),
            CancellationToken.None);
        Assert.Equal("İtirazınız admin ekibine iletildi", esc.Body!.Message);

        var escEvent = Assert.Single(_outbox.Published.OfType<DisputeEscalatedEvent>());
        Assert.Equal("İtirazınız admin ekibine iletildi", escEvent.OutcomeText);
    }

    [Fact]
    public async Task Open_Payment_ConfirmedPaymentExists_AutoResolves_AndEmitsEvent()
    {
        var tx = await CreateTransactionAsync(TransactionStatus.PAYMENT_RECEIVED);
        await SeedConfirmedBuyerPaymentAsync(tx, "abc123def456789012345678");

        var sut = BuildSut();
        var outcome = await sut.OpenAsync(_buyer.Id, tx.Id,
            new OpenDisputeRequest(DisputeType.PAYMENT), CancellationToken.None);

        Assert.Equal(OpenDisputeStatus.Opened, outcome.Status);
        Assert.NotNull(outcome.Body);
        Assert.Equal(DisputeStatus.CLOSED, outcome.Body.Status);
        Assert.True(outcome.Body.AutoCheckResult.Resolved);
        Assert.False(outcome.Body.AutoCheckResult.CanSubmitTxHash);
        Assert.False(outcome.Body.AutoCheckResult.CanEscalate);

        var refreshedTx = await Context.Set<Transaction>().AsNoTracking()
            .FirstAsync(t => t.Id == tx.Id);
        Assert.False(refreshedTx.HasActiveDispute);

        var resolvedEvent = Assert.Single(_outbox.Published.OfType<DisputeAutoResolvedEvent>());
        Assert.Equal(outcome.Body.Id, resolvedEvent.DisputeId);
        Assert.Equal(_buyer.Id, resolvedEvent.BuyerId);
    }

    // ---------- Open ▸ DELIVERY ----------

    [Fact]
    public async Task Open_Delivery_TradeOfferAccepted_AutoResolves()
    {
        var tx = await CreateTransactionAsync(TransactionStatus.ITEM_DELIVERED);
        Context.Set<TradeOffer>().Add(new TradeOffer
        {
            Id = Guid.NewGuid(),
            TransactionId = tx.Id,
            PlatformSteamBotId = _bot.Id,
            Direction = TradeOfferDirection.TO_BUYER,
            Status = TradeOfferStatus.ACCEPTED,
            SteamTradeOfferId = "10000",
            SentAt = _clock.GetUtcNow().UtcDateTime,
            RespondedAt = _clock.GetUtcNow().UtcDateTime,
            CreatedAt = _clock.GetUtcNow().UtcDateTime,
            UpdatedAt = _clock.GetUtcNow().UtcDateTime,
        });
        await Context.SaveChangesAsync();

        var sut = BuildSut();
        var outcome = await sut.OpenAsync(_buyer.Id, tx.Id,
            new OpenDisputeRequest(DisputeType.DELIVERY), CancellationToken.None);

        Assert.Equal(OpenDisputeStatus.Opened, outcome.Status);
        Assert.Equal(DisputeStatus.CLOSED, outcome.Body!.Status);
        Assert.True(outcome.Body.AutoCheckResult.Resolved);

        Assert.Single(_outbox.Published.OfType<DisputeAutoResolvedEvent>());
    }

    [Fact]
    public async Task Open_Delivery_TradeOfferPendingNoInventory_StaysOpen()
    {
        var tx = await CreateTransactionAsync(TransactionStatus.TRADE_OFFER_SENT_TO_BUYER);
        Context.Set<TradeOffer>().Add(new TradeOffer
        {
            Id = Guid.NewGuid(),
            TransactionId = tx.Id,
            PlatformSteamBotId = _bot.Id,
            Direction = TradeOfferDirection.TO_BUYER,
            Status = TradeOfferStatus.SENT,
            SteamTradeOfferId = "10001",
            SentAt = _clock.GetUtcNow().UtcDateTime,
            CreatedAt = _clock.GetUtcNow().UtcDateTime,
            UpdatedAt = _clock.GetUtcNow().UtcDateTime,
        });
        await Context.SaveChangesAsync();

        // Inventory probe returns null (default fake) — fails closed: stays open.
        var sut = BuildSut();
        var outcome = await sut.OpenAsync(_buyer.Id, tx.Id,
            new OpenDisputeRequest(DisputeType.DELIVERY), CancellationToken.None);

        Assert.Equal(DisputeStatus.OPEN, outcome.Body!.Status);
        Assert.False(outcome.Body.AutoCheckResult.Resolved);
        Assert.True(outcome.Body.AutoCheckResult.CanEscalate);
    }

    [Fact]
    public async Task Open_Delivery_PendingButInventoryHasItem_AutoResolves()
    {
        var tx = await CreateTransactionAsync(TransactionStatus.TRADE_OFFER_SENT_TO_BUYER);
        Context.Set<TradeOffer>().Add(new TradeOffer
        {
            Id = Guid.NewGuid(),
            TransactionId = tx.Id,
            PlatformSteamBotId = _bot.Id,
            Direction = TradeOfferDirection.TO_BUYER,
            Status = TradeOfferStatus.SENT,
            SteamTradeOfferId = "10002",
            SentAt = _clock.GetUtcNow().UtcDateTime,
            CreatedAt = _clock.GetUtcNow().UtcDateTime,
            UpdatedAt = _clock.GetUtcNow().UtcDateTime,
        });
        await Context.SaveChangesAsync();

        _inventory.Snapshot = new InventoryItemSnapshot(
            AssetId: tx.ItemAssetId,
            ClassId: tx.ItemClassId,
            InstanceId: null,
            Name: tx.ItemName,
            MarketHashName: tx.ItemName,
            IconUrl: null,
            Exterior: null,
            Type: null,
            InspectLink: null,
            IsTradeable: true);

        var sut = BuildSut();
        var outcome = await sut.OpenAsync(_buyer.Id, tx.Id,
            new OpenDisputeRequest(DisputeType.DELIVERY), CancellationToken.None);

        Assert.Equal(DisputeStatus.CLOSED, outcome.Body!.Status);
        Assert.True(outcome.Body.AutoCheckResult.Resolved);
    }

    // ---------- Open ▸ WRONG_ITEM ----------

    [Fact]
    public async Task Open_WrongItem_NoDeliveredAsset_StaysOpen()
    {
        var tx = await CreateTransactionAsync(TransactionStatus.ITEM_DELIVERED);
        var sut = BuildSut();

        var outcome = await sut.OpenAsync(_buyer.Id, tx.Id,
            new OpenDisputeRequest(DisputeType.WRONG_ITEM), CancellationToken.None);

        Assert.Equal(DisputeStatus.OPEN, outcome.Body!.Status);
        Assert.False(outcome.Body.AutoCheckResult.Resolved);
    }

    [Fact]
    public async Task Open_WrongItem_ClassMatch_AutoResolves()
    {
        var tx = await CreateTransactionAsync(TransactionStatus.ITEM_DELIVERED,
            deliveredAssetId: "delivered-asset-1");
        _inventory.Snapshot = BuildSnapshot("delivered-asset-1", classId: tx.ItemClassId);

        var sut = BuildSut();
        var outcome = await sut.OpenAsync(_buyer.Id, tx.Id,
            new OpenDisputeRequest(DisputeType.WRONG_ITEM), CancellationToken.None);

        Assert.Equal(DisputeStatus.CLOSED, outcome.Body!.Status);
        Assert.Single(_outbox.Published.OfType<DisputeAutoResolvedEvent>());
    }

    [Fact]
    public async Task Open_WrongItem_ClassMismatch_AutoEscalates_AndNotifiesBothParties()
    {
        var tx = await CreateTransactionAsync(TransactionStatus.ITEM_DELIVERED,
            deliveredAssetId: "delivered-asset-2");
        _inventory.Snapshot = BuildSnapshot("delivered-asset-2", classId: "DIFFERENT-CLASS");

        var sut = BuildSut();
        var outcome = await sut.OpenAsync(_buyer.Id, tx.Id,
            new OpenDisputeRequest(DisputeType.WRONG_ITEM), CancellationToken.None);

        Assert.Equal(DisputeStatus.ESCALATED, outcome.Body!.Status);

        var evt = Assert.Single(_outbox.Published.OfType<DisputeEscalatedEvent>());
        Assert.True(evt.AutoEscalated);
        Assert.Equal(_seller.Id, evt.SellerId);
        Assert.Equal(_buyer.Id, evt.BuyerId);

        var refreshedTx = await Context.Set<Transaction>().AsNoTracking()
            .FirstAsync(t => t.Id == tx.Id);
        Assert.True(refreshedTx.HasActiveDispute);
    }

    [Fact]
    public async Task Open_WrongItem_DeliveredAssetSet_ButSidecarProbeNull_StaysOpen()
    {
        // WP6 harden — when the real SidecarSteamInventoryReader cannot resolve
        // the delivered asset (sidecar 503 / private inventory both map to null),
        // a class-id mismatch CANNOT be concluded. The checker must fail closed:
        // leave the dispute OPEN for manual escalation rather than auto-escalate
        // (or auto-resolve) off missing data.
        var tx = await CreateTransactionAsync(TransactionStatus.ITEM_DELIVERED,
            deliveredAssetId: "delivered-asset-9");
        _inventory.Snapshot = null; // simulates sidecar Unavailable / InventoryPrivate

        var sut = BuildSut();
        var outcome = await sut.OpenAsync(_buyer.Id, tx.Id,
            new OpenDisputeRequest(DisputeType.WRONG_ITEM), CancellationToken.None);

        // OPEN status + no escalation event proves the checker did NOT auto-
        // escalate off missing data (AutoEscalated is internal-only; the DTO
        // surfaces it as "stays OPEN, can escalate manually").
        Assert.Equal(DisputeStatus.OPEN, outcome.Body!.Status);
        Assert.False(outcome.Body.AutoCheckResult.Resolved);
        Assert.True(outcome.Body.AutoCheckResult.CanEscalate);
        Assert.Empty(_outbox.Published.OfType<DisputeEscalatedEvent>());
        Assert.Empty(_outbox.Published.OfType<DisputeAutoResolvedEvent>());

        var refreshedTx = await Context.Set<Transaction>().AsNoTracking()
            .FirstAsync(t => t.Id == tx.Id);
        Assert.True(refreshedTx.HasActiveDispute);
    }

    // ---------- Open ▸ guards ----------

    [Fact]
    public async Task Open_NotBuyer_Returns_NotBuyer()
    {
        var tx = await CreateTransactionAsync(TransactionStatus.ITEM_ESCROWED);
        var sut = BuildSut();

        var outcome = await sut.OpenAsync(_otherUser.Id, tx.Id,
            new OpenDisputeRequest(DisputeType.PAYMENT), CancellationToken.None);

        Assert.Equal(OpenDisputeStatus.NotBuyer, outcome.Status);
        Assert.Equal(DisputeErrorCodes.NotBuyer, outcome.ErrorCode);
    }

    [Fact]
    public async Task Open_TransactionNotFound_Returns_NotFound()
    {
        var sut = BuildSut();

        var outcome = await sut.OpenAsync(_buyer.Id, Guid.NewGuid(),
            new OpenDisputeRequest(DisputeType.PAYMENT), CancellationToken.None);

        Assert.Equal(OpenDisputeStatus.NotFound, outcome.Status);
        Assert.Equal(DisputeErrorCodes.TransactionNotFound, outcome.ErrorCode);
    }

    [Fact]
    public async Task Open_StateNotAllowedForType_Returns_InvalidStateTransition()
    {
        // PAYMENT not allowed when state is TRADE_OFFER_SENT_TO_BUYER per
        // T58 per-type table.
        var tx = await CreateTransactionAsync(TransactionStatus.TRADE_OFFER_SENT_TO_BUYER);
        var sut = BuildSut();

        var outcome = await sut.OpenAsync(_buyer.Id, tx.Id,
            new OpenDisputeRequest(DisputeType.PAYMENT), CancellationToken.None);

        Assert.Equal(OpenDisputeStatus.InvalidStateTransition, outcome.Status);
        Assert.Equal(DisputeErrorCodes.InvalidStateTransition, outcome.ErrorCode);
    }

    [Fact]
    public async Task Open_DuplicateType_AfterClose_Returns_DuplicateDispute()
    {
        var tx = await CreateTransactionAsync(TransactionStatus.PAYMENT_RECEIVED);
        // Force a confirmed payment so the first open auto-resolves to CLOSED.
        await SeedConfirmedBuyerPaymentAsync(tx, "abc123def456789012345678");

        var sut = BuildSut();
        var first = await sut.OpenAsync(_buyer.Id, tx.Id,
            new OpenDisputeRequest(DisputeType.PAYMENT), CancellationToken.None);
        Assert.Equal(OpenDisputeStatus.Opened, first.Status);

        var second = await sut.OpenAsync(_buyer.Id, tx.Id,
            new OpenDisputeRequest(DisputeType.PAYMENT), CancellationToken.None);

        Assert.Equal(OpenDisputeStatus.DuplicateDispute, second.Status);
        Assert.Equal(DisputeErrorCodes.DuplicateDispute, second.ErrorCode);
    }

    [Fact]
    public async Task Open_DifferentTypes_Concurrently_Allowed()
    {
        var tx = await CreateTransactionAsync(TransactionStatus.ITEM_DELIVERED);
        Context.Set<TradeOffer>().Add(new TradeOffer
        {
            Id = Guid.NewGuid(),
            TransactionId = tx.Id,
            PlatformSteamBotId = _bot.Id,
            Direction = TradeOfferDirection.TO_BUYER,
            Status = TradeOfferStatus.SENT,
            SteamTradeOfferId = "10003",
            SentAt = _clock.GetUtcNow().UtcDateTime,
            CreatedAt = _clock.GetUtcNow().UtcDateTime,
            UpdatedAt = _clock.GetUtcNow().UtcDateTime,
        });
        await Context.SaveChangesAsync();

        var sut = BuildSut();
        var delivery = await sut.OpenAsync(_buyer.Id, tx.Id,
            new OpenDisputeRequest(DisputeType.DELIVERY), CancellationToken.None);
        Assert.Equal(OpenDisputeStatus.Opened, delivery.Status);

        var wrongItem = await sut.OpenAsync(_buyer.Id, tx.Id,
            new OpenDisputeRequest(DisputeType.WRONG_ITEM), CancellationToken.None);
        Assert.Equal(OpenDisputeStatus.Opened, wrongItem.Status);

        var count = await Context.Set<Dispute>().CountAsync(d => d.TransactionId == tx.Id);
        Assert.Equal(2, count);
    }

    // ---------- SubmitTxHash ----------

    [Fact]
    public async Task SubmitTxHash_MatchingHash_Resolves_AndClearsActiveDisputeFlag()
    {
        var tx = await CreateTransactionAsync(TransactionStatus.PAYMENT_RECEIVED);
        var sut = BuildSut();

        var open = await sut.OpenAsync(_buyer.Id, tx.Id,
            new OpenDisputeRequest(DisputeType.PAYMENT), CancellationToken.None);
        Assert.Equal(DisputeStatus.OPEN, open.Body!.Status);

        // Sidecar (T71) writes a CONFIRMED row with the hash the buyer is
        // about to submit (canonical lowercase per T71 normalization contract).
        const string hash = "ABC123DEF456789012345678";
        await SeedConfirmedBuyerPaymentAsync(tx, hash.ToLowerInvariant());

        var outcome = await sut.SubmitTxHashAsync(_buyer.Id, tx.Id, open.Body.Id,
            new SubmitTxHashRequest(hash), CancellationToken.None);

        Assert.Equal(SubmitTxHashStatus.Processed, outcome.Status);
        Assert.True(outcome.Body!.CheckResult.Resolved);

        var refreshedDispute = await Context.Set<Dispute>().AsNoTracking()
            .FirstAsync(d => d.Id == open.Body.Id);
        Assert.Equal(DisputeStatus.CLOSED, refreshedDispute.Status);
        Assert.NotNull(refreshedDispute.ResolvedAt);

        var refreshedTx = await Context.Set<Transaction>().AsNoTracking()
            .FirstAsync(t => t.Id == tx.Id);
        Assert.False(refreshedTx.HasActiveDispute);

        Assert.Single(_outbox.Published.OfType<DisputeAutoResolvedEvent>());
    }

    [Fact]
    public async Task SubmitTxHash_NoMatch_StaysOpen()
    {
        var tx = await CreateTransactionAsync(TransactionStatus.ITEM_ESCROWED);
        var sut = BuildSut();

        var open = await sut.OpenAsync(_buyer.Id, tx.Id,
            new OpenDisputeRequest(DisputeType.PAYMENT), CancellationToken.None);
        Assert.Equal(DisputeStatus.OPEN, open.Body!.Status);

        var outcome = await sut.SubmitTxHashAsync(_buyer.Id, tx.Id, open.Body.Id,
            new SubmitTxHashRequest("0123456789abcdef0123"), CancellationToken.None);

        Assert.Equal(SubmitTxHashStatus.Processed, outcome.Status);
        Assert.False(outcome.Body!.CheckResult.Resolved);

        var refreshedDispute = await Context.Set<Dispute>().AsNoTracking()
            .FirstAsync(d => d.Id == open.Body.Id);
        Assert.Equal(DisputeStatus.OPEN, refreshedDispute.Status);
    }

    [Fact]
    public async Task SubmitTxHash_NonPaymentDispute_Returns_NotPaymentDispute()
    {
        var tx = await CreateTransactionAsync(TransactionStatus.ITEM_DELIVERED);
        Context.Set<TradeOffer>().Add(new TradeOffer
        {
            Id = Guid.NewGuid(),
            TransactionId = tx.Id,
            PlatformSteamBotId = _bot.Id,
            Direction = TradeOfferDirection.TO_BUYER,
            Status = TradeOfferStatus.SENT,
            SteamTradeOfferId = "10004",
            SentAt = _clock.GetUtcNow().UtcDateTime,
            CreatedAt = _clock.GetUtcNow().UtcDateTime,
            UpdatedAt = _clock.GetUtcNow().UtcDateTime,
        });
        await Context.SaveChangesAsync();

        var sut = BuildSut();
        var open = await sut.OpenAsync(_buyer.Id, tx.Id,
            new OpenDisputeRequest(DisputeType.DELIVERY), CancellationToken.None);
        Assert.Equal(DisputeStatus.OPEN, open.Body!.Status);

        var outcome = await sut.SubmitTxHashAsync(_buyer.Id, tx.Id, open.Body.Id,
            new SubmitTxHashRequest("0123456789abcdef0123"), CancellationToken.None);

        Assert.Equal(SubmitTxHashStatus.NotPaymentDispute, outcome.Status);
        Assert.Equal(DisputeErrorCodes.NotPaymentDispute, outcome.ErrorCode);
    }

    [Fact]
    public async Task SubmitTxHash_DisputeClosed_Returns_DisputeClosed()
    {
        var tx = await CreateTransactionAsync(TransactionStatus.PAYMENT_RECEIVED);
        await SeedConfirmedBuyerPaymentAsync(tx, "abc123def456789012345678");

        var sut = BuildSut();
        var open = await sut.OpenAsync(_buyer.Id, tx.Id,
            new OpenDisputeRequest(DisputeType.PAYMENT), CancellationToken.None);
        Assert.Equal(DisputeStatus.CLOSED, open.Body!.Status);

        var outcome = await sut.SubmitTxHashAsync(_buyer.Id, tx.Id, open.Body.Id,
            new SubmitTxHashRequest("0123456789abcdef0123"), CancellationToken.None);

        Assert.Equal(SubmitTxHashStatus.DisputeClosed, outcome.Status);
        Assert.Equal(DisputeErrorCodes.DisputeClosed, outcome.ErrorCode);
    }

    [Fact]
    public async Task SubmitTxHash_BadHashLength_Returns_ValidationError()
    {
        var tx = await CreateTransactionAsync(TransactionStatus.ITEM_ESCROWED);
        var sut = BuildSut();

        var open = await sut.OpenAsync(_buyer.Id, tx.Id,
            new OpenDisputeRequest(DisputeType.PAYMENT), CancellationToken.None);
        Assert.Equal(DisputeStatus.OPEN, open.Body!.Status);

        var outcome = await sut.SubmitTxHashAsync(_buyer.Id, tx.Id, open.Body.Id,
            new SubmitTxHashRequest("short"), CancellationToken.None);

        Assert.Equal(SubmitTxHashStatus.ValidationFailed, outcome.Status);
        Assert.Equal(DisputeErrorCodes.ValidationError, outcome.ErrorCode);
    }

    [Fact]
    public async Task SubmitTxHash_NonBuyer_Returns_NotBuyer()
    {
        var tx = await CreateTransactionAsync(TransactionStatus.ITEM_ESCROWED);
        var sut = BuildSut();

        var open = await sut.OpenAsync(_buyer.Id, tx.Id,
            new OpenDisputeRequest(DisputeType.PAYMENT), CancellationToken.None);
        Assert.Equal(DisputeStatus.OPEN, open.Body!.Status);

        var outcome = await sut.SubmitTxHashAsync(_otherUser.Id, tx.Id, open.Body.Id,
            new SubmitTxHashRequest("0123456789abcdef0123"), CancellationToken.None);

        Assert.Equal(SubmitTxHashStatus.NotBuyer, outcome.Status);
    }

    // ---------- Escalate ----------

    [Fact]
    public async Task Escalate_OpenDispute_PromotesToEscalated_AndEmitsEvent()
    {
        var tx = await CreateTransactionAsync(TransactionStatus.ITEM_ESCROWED);
        var sut = BuildSut();

        var open = await sut.OpenAsync(_buyer.Id, tx.Id,
            new OpenDisputeRequest(DisputeType.PAYMENT), CancellationToken.None);
        Assert.Equal(DisputeStatus.OPEN, open.Body!.Status);

        var outcome = await sut.EscalateAsync(_buyer.Id, tx.Id, open.Body.Id,
            new EscalateDisputeRequest("Ödemeyi gönderdim ama sistem hala görmüyor"),
            CancellationToken.None);

        Assert.Equal(EscalateDisputeStatus.Escalated, outcome.Status);
        Assert.Equal(DisputeStatus.ESCALATED, outcome.Body!.Status);
        // WP17 — escalate response is localized to the buyer's locale ("en" here).
        Assert.Equal("Your dispute has been forwarded to the admin team", outcome.Body.Message);

        var refreshedDispute = await Context.Set<Dispute>().AsNoTracking()
            .FirstAsync(d => d.Id == open.Body.Id);
        Assert.Equal(DisputeStatus.ESCALATED, refreshedDispute.Status);
        Assert.Equal("Ödemeyi gönderdim ama sistem hala görmüyor", refreshedDispute.UserDescription);

        var refreshedTx = await Context.Set<Transaction>().AsNoTracking()
            .FirstAsync(t => t.Id == tx.Id);
        Assert.True(refreshedTx.HasActiveDispute);

        var evt = Assert.Single(_outbox.Published.OfType<DisputeEscalatedEvent>());
        Assert.False(evt.AutoEscalated);
        Assert.Equal(_buyer.Id, evt.BuyerId);
    }

    [Fact]
    public async Task Escalate_AlreadyEscalated_Returns_AlreadyEscalated()
    {
        var tx = await CreateTransactionAsync(TransactionStatus.ITEM_DELIVERED,
            deliveredAssetId: "delivered-asset-3");
        _inventory.Snapshot = BuildSnapshot("delivered-asset-3", classId: "OTHER-CLASS");

        var sut = BuildSut();
        var open = await sut.OpenAsync(_buyer.Id, tx.Id,
            new OpenDisputeRequest(DisputeType.WRONG_ITEM), CancellationToken.None);
        Assert.Equal(DisputeStatus.ESCALATED, open.Body!.Status);

        var outcome = await sut.EscalateAsync(_buyer.Id, tx.Id, open.Body.Id,
            new EscalateDisputeRequest("Bekliyorum çözüm bekliyorum"),
            CancellationToken.None);

        Assert.Equal(EscalateDisputeStatus.AlreadyEscalated, outcome.Status);
        Assert.Equal(DisputeErrorCodes.AlreadyEscalated, outcome.ErrorCode);
    }

    [Fact]
    public async Task Escalate_ClosedDispute_Returns_DisputeClosed()
    {
        var tx = await CreateTransactionAsync(TransactionStatus.PAYMENT_RECEIVED);
        await SeedConfirmedBuyerPaymentAsync(tx, "abc123def456789012345678");

        var sut = BuildSut();
        var open = await sut.OpenAsync(_buyer.Id, tx.Id,
            new OpenDisputeRequest(DisputeType.PAYMENT), CancellationToken.None);
        Assert.Equal(DisputeStatus.CLOSED, open.Body!.Status);

        var outcome = await sut.EscalateAsync(_buyer.Id, tx.Id, open.Body.Id,
            new EscalateDisputeRequest("Yine de admin baksın lütfen"),
            CancellationToken.None);

        Assert.Equal(EscalateDisputeStatus.DisputeClosed, outcome.Status);
    }

    [Fact]
    public async Task Escalate_DetailTooShort_Returns_ValidationFailed()
    {
        var tx = await CreateTransactionAsync(TransactionStatus.ITEM_ESCROWED);
        var sut = BuildSut();

        var open = await sut.OpenAsync(_buyer.Id, tx.Id,
            new OpenDisputeRequest(DisputeType.PAYMENT), CancellationToken.None);
        Assert.Equal(DisputeStatus.OPEN, open.Body!.Status);

        var outcome = await sut.EscalateAsync(_buyer.Id, tx.Id, open.Body.Id,
            new EscalateDisputeRequest("kısa"),
            CancellationToken.None);

        Assert.Equal(EscalateDisputeStatus.ValidationFailed, outcome.Status);
    }

    [Fact]
    public async Task Escalate_NotBuyer_Returns_NotBuyer()
    {
        var tx = await CreateTransactionAsync(TransactionStatus.ITEM_ESCROWED);
        var sut = BuildSut();

        var open = await sut.OpenAsync(_buyer.Id, tx.Id,
            new OpenDisputeRequest(DisputeType.PAYMENT), CancellationToken.None);

        var outcome = await sut.EscalateAsync(_otherUser.Id, tx.Id, open.Body!.Id,
            new EscalateDisputeRequest("Ben başka biriyim ama itiraz ediyorum"),
            CancellationToken.None);

        Assert.Equal(EscalateDisputeStatus.NotBuyer, outcome.Status);
    }

    [Fact]
    public async Task Escalate_DisputeNotFound_Returns_NotFound()
    {
        var tx = await CreateTransactionAsync(TransactionStatus.ITEM_ESCROWED);
        var sut = BuildSut();

        var outcome = await sut.EscalateAsync(_buyer.Id, tx.Id, Guid.NewGuid(),
            new EscalateDisputeRequest("Var olmayan bir dispute"),
            CancellationToken.None);

        Assert.Equal(EscalateDisputeStatus.NotFound, outcome.Status);
        Assert.Equal(DisputeErrorCodes.DisputeNotFound, outcome.ErrorCode);
    }

    // ---------- Test helpers ----------

    private DisputeService BuildSut()
    {
        var paymentChecker = new PaymentDisputeAutoChecker(Context);
        var deliveryChecker = new DeliveryDisputeAutoChecker(Context, _inventory);
        var wrongItemChecker = new WrongItemDisputeAutoChecker(Context, _inventory);

        return new DisputeService(
            db: Context,
            outbox: _outbox,
            paymentChecker: paymentChecker,
            deliveryChecker: deliveryChecker,
            wrongItemChecker: wrongItemChecker,
            clock: _clock);
    }

    /// <summary>
    /// Inserts a CONFIRMED BUYER_PAYMENT row + the prerequisite PaymentAddress
    /// row that <c>CK_BlockchainTransactions_Type_BuyerPayment</c> requires
    /// (PaymentAddressId NOT NULL) and that <c>CK_BlockchainTransactions_Status_Confirmed</c>
    /// requires (ConfirmationCount >= 20, ConfirmedAt NOT NULL).
    /// Mirrors the T56 lesson learned in <c>FraudFlagServiceTests.InsertBuyerPaymentAsync</c>.
    /// </summary>
    private async Task SeedConfirmedBuyerPaymentAsync(Transaction tx, string txHash)
    {
        var now = _clock.GetUtcNow().UtcDateTime;

        var paymentAddress = new PaymentAddress
        {
            Id = Guid.NewGuid(),
            TransactionId = tx.Id,
            Address = "TBuyerDepositAddress0000000000000",
            HdWalletIndex = 1,
            ExpectedAmount = tx.TotalAmount,
            ExpectedToken = StablecoinType.USDT,
            MonitoringStatus = MonitoringStatus.ACTIVE,
            CreatedAt = now,
            UpdatedAt = now,
        };
        Context.Set<PaymentAddress>().Add(paymentAddress);

        Context.Set<BlockchainTransaction>().Add(new BlockchainTransaction
        {
            Id = Guid.NewGuid(),
            TransactionId = tx.Id,
            PaymentAddressId = paymentAddress.Id,
            Type = BlockchainTransactionType.BUYER_PAYMENT,
            FromAddress = "TBuyer",
            ToAddress = paymentAddress.Address,
            Amount = tx.TotalAmount,
            Token = StablecoinType.USDT,
            Status = BlockchainTransactionStatus.CONFIRMED,
            ConfirmationCount = 20,
            CreatedAt = now,
            ConfirmedAt = now,
            TxHash = txHash,
        });

        await Context.SaveChangesAsync();
    }

    private async Task<Transaction> CreateTransactionAsync(
        TransactionStatus status,
        string? deliveredAssetId = null)
    {
        var tx = new Transaction
        {
            Id = Guid.NewGuid(),
            Status = status,
            SellerId = _seller.Id,
            BuyerId = _buyer.Id,
            BuyerIdentificationMethod = BuyerIdentificationMethod.STEAM_ID,
            TargetBuyerSteamId = BuyerSteam,
            ItemAssetId = "asset-1",
            ItemClassId = "CLASS-AK47-REDLINE",
            ItemName = "AK-47 | Redline",
            StablecoinType = StablecoinType.USDT,
            Price = 50.00m,
            CommissionRate = 0.03m,
            CommissionAmount = 1.50m,
            TotalAmount = 51.50m,
            SellerPayoutAddress = SellerWallet,
            PaymentTimeoutMinutes = 60,
            DeliveredBuyerAssetId = deliveredAssetId,
            CreatedAt = _clock.GetUtcNow().UtcDateTime,
            UpdatedAt = _clock.GetUtcNow().UtcDateTime,
            AcceptedAt = _clock.GetUtcNow().UtcDateTime.AddMinutes(-10),
        };
        Context.Set<Transaction>().Add(tx);
        await Context.SaveChangesAsync();
        return tx;
    }

    private static InventoryItemSnapshot BuildSnapshot(string assetId, string classId) =>
        new(
            AssetId: assetId,
            ClassId: classId,
            InstanceId: null,
            Name: "Snapshot",
            MarketHashName: "Snapshot",
            IconUrl: null,
            Exterior: null,
            Type: null,
            InspectLink: null,
            IsTradeable: true);

    private sealed class RecordingOutboxService : IOutboxService
    {
        public List<IDomainEvent> Published { get; } = [];

        public Task PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default)
        {
            Published.Add(domainEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeInventoryReader : ISteamInventoryReader
    {
        public InventoryItemSnapshot? Snapshot { get; set; }

        public Task<InventoryItemSnapshot?> TryGetItemAsync(
            string steamId64,
            string itemAssetId,
            CancellationToken cancellationToken)
            => Task.FromResult(Snapshot);
    }
}

