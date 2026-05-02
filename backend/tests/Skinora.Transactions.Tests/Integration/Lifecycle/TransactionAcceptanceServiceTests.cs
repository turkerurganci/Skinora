using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Skinora.Shared.Domain;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Interfaces;
using Skinora.Shared.Persistence;
using Skinora.Shared.Tests.Integration;
using Skinora.Transactions.Application.Lifecycle;
using Skinora.Transactions.Domain.Entities;
using Skinora.Transactions.Infrastructure.Persistence;
using Skinora.Users.Application.Wallet;
using Skinora.Users.Domain.Entities;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.Transactions.Tests.Integration.Lifecycle;

/// <summary>
/// End-to-end coverage for <see cref="TransactionAcceptanceService"/>
/// (T46 — 07 §7.6, 03 §3.2). Verifies Yöntem 1 (Steam ID match), Yöntem 2
/// (open link first-comer wins), refund-wallet pipeline (TRC-20 + sanctions
/// + cooldown), CREATED → ACCEPTED state transition, BuyerAcceptedEvent
/// emission, and per-rejection error codes.
/// </summary>
public class TransactionAcceptanceServiceTests : IntegrationTestBase
{
    static TransactionAcceptanceServiceTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        TransactionsModuleDbRegistration.RegisterTransactionsModule();
        Skinora.Platform.Infrastructure.Persistence.PlatformModuleDbRegistration.RegisterPlatformModule();
    }

    private const string ValidWallet1 = "TXyzABCDEFGHJKLMNPQRSTUVWXYZ234567";
    private const string ValidWallet2 = "TabcDEFGHJKLMNPQRSTUVWXYZ234567Xyz";
    private const string SellerSteamId = "76561198000000080";
    private const string BuyerSteamId = "76561198000000081";

    private User _seller = null!;
    private User _buyer = null!;
    private FakeTimeProvider _clock = null!;
    private RecordingOutboxService _outbox = null!;

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

        await context.ConfigureSettingAsync(TransactionAcceptanceService.RefundCooldownKey, "24");

        _clock = new FakeTimeProvider(new DateTimeOffset(2026, 5, 2, 12, 0, 0, TimeSpan.Zero));
        _outbox = new RecordingOutboxService();
    }

    [Fact]
    public async Task Happy_Path_SteamId_Method_Transitions_To_Accepted_And_Emits_Outbox()
    {
        var transaction = await CreateTransactionAsync(BuyerIdentificationMethod.STEAM_ID, BuyerSteamId);

        var sut = BuildSut();
        var outcome = await sut.AcceptAsync(
            _buyer.Id, transaction.Id,
            new AcceptTransactionRequest(ValidWallet2),
            CancellationToken.None);

        Assert.Equal(AcceptTransactionStatus.Accepted, outcome.Status);
        Assert.NotNull(outcome.Body);
        Assert.Equal(TransactionStatus.ACCEPTED, outcome.Body.Status);

        var persisted = await Context.Set<Transaction>()
            .AsNoTracking()
            .SingleAsync(t => t.Id == transaction.Id);
        Assert.Equal(TransactionStatus.ACCEPTED, persisted.Status);
        Assert.Equal(_buyer.Id, persisted.BuyerId);
        Assert.Equal(ValidWallet2, persisted.BuyerRefundAddress);
        Assert.NotNull(persisted.AcceptedAt);

        Assert.Single(_outbox.Published);
        var evt = Assert.IsType<BuyerAcceptedEvent>(_outbox.Published[0]);
        Assert.Equal(transaction.Id, evt.TransactionId);
        Assert.Equal(_seller.Id, evt.SellerId);
        Assert.Equal(_buyer.Id, evt.BuyerId);

        // RefundAddressChangedAt updated → cooldown timer starts ticking.
        var buyer = await Context.Set<User>().AsNoTracking().SingleAsync(u => u.Id == _buyer.Id);
        Assert.Equal(ValidWallet2, buyer.DefaultRefundAddress);
        Assert.NotNull(buyer.RefundAddressChangedAt);
    }

    [Fact]
    public async Task Steam_Id_Mismatch_Rejects_With_403()
    {
        var transaction = await CreateTransactionAsync(
            BuyerIdentificationMethod.STEAM_ID, "76561198000099999");

        var sut = BuildSut();
        var outcome = await sut.AcceptAsync(
            _buyer.Id, transaction.Id,
            new AcceptTransactionRequest(ValidWallet2),
            CancellationToken.None);

        Assert.Equal(AcceptTransactionStatus.SteamIdMismatch, outcome.Status);
        Assert.Equal(TransactionErrorCodes.SteamIdMismatch, outcome.ErrorCode);

        var persisted = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == transaction.Id);
        Assert.Equal(TransactionStatus.CREATED, persisted.Status);
        Assert.Empty(_outbox.Published);
    }

    [Fact]
    public async Task Open_Link_Method_First_Comer_Wins_And_Subsequent_Get_AlreadyAccepted()
    {
        var transaction = await CreateTransactionAsync(
            BuyerIdentificationMethod.OPEN_LINK, targetSteamId: null);

        // Second-comer must exist as an authenticated user — JWT auth
        // guarantees this in production. Seed an extra user to mirror it.
        var lateBuyer = new User
        {
            Id = Guid.NewGuid(),
            SteamId = "76561198000000999",
            SteamDisplayName = "LateBuyer",
        };
        Context.Set<User>().Add(lateBuyer);
        await Context.SaveChangesAsync();

        var sut = BuildSut();
        var first = await sut.AcceptAsync(
            _buyer.Id, transaction.Id,
            new AcceptTransactionRequest(ValidWallet2),
            CancellationToken.None);
        Assert.Equal(AcceptTransactionStatus.Accepted, first.Status);

        // Second comer hits the state guard — CREATED → ACCEPTED happens once,
        // so the second AcceptAsync sees status=ACCEPTED and short-circuits to
        // ALREADY_ACCEPTED. This is the 07 §7.6 contract for repeat callers.
        var second = await sut.AcceptAsync(
            lateBuyer.Id, transaction.Id,
            new AcceptTransactionRequest(ValidWallet1),
            CancellationToken.None);

        Assert.Equal(AcceptTransactionStatus.AlreadyAccepted, second.Status);
        Assert.Equal(TransactionErrorCodes.AlreadyAccepted, second.ErrorCode);
    }

    [Fact]
    public async Task Open_Link_Seller_Cannot_Accept_Own_Listing()
    {
        var transaction = await CreateTransactionAsync(
            BuyerIdentificationMethod.OPEN_LINK, targetSteamId: null);

        var sut = BuildSut();
        var outcome = await sut.AcceptAsync(
            _seller.Id, transaction.Id,
            new AcceptTransactionRequest(ValidWallet2),
            CancellationToken.None);

        Assert.Equal(AcceptTransactionStatus.NotAParty, outcome.Status);
        Assert.Equal(TransactionErrorCodes.NotAParty, outcome.ErrorCode);
    }

    [Fact]
    public async Task Refund_Address_Required_When_Empty()
    {
        var transaction = await CreateTransactionAsync(
            BuyerIdentificationMethod.STEAM_ID, BuyerSteamId);

        var sut = BuildSut();
        var outcome = await sut.AcceptAsync(
            _buyer.Id, transaction.Id,
            new AcceptTransactionRequest("   "),
            CancellationToken.None);

        Assert.Equal(AcceptTransactionStatus.ValidationFailed, outcome.Status);
        Assert.Equal(TransactionErrorCodes.RefundAddressRequired, outcome.ErrorCode);
    }

    [Fact]
    public async Task Refund_Address_Format_Invalid_Returns_400()
    {
        var transaction = await CreateTransactionAsync(
            BuyerIdentificationMethod.STEAM_ID, BuyerSteamId);

        var sut = BuildSut();
        var outcome = await sut.AcceptAsync(
            _buyer.Id, transaction.Id,
            new AcceptTransactionRequest("NOT_A_TRC20_ADDRESS"),
            CancellationToken.None);

        Assert.Equal(AcceptTransactionStatus.InvalidWallet, outcome.Status);
        Assert.Equal(TransactionErrorCodes.InvalidWalletAddress, outcome.ErrorCode);
    }

    [Fact]
    public async Task Sanctions_Match_Rejects_With_403()
    {
        var transaction = await CreateTransactionAsync(
            BuyerIdentificationMethod.STEAM_ID, BuyerSteamId);

        var sut = BuildSut(sanctions: new MatchingSanctionsCheck("OFAC"));
        var outcome = await sut.AcceptAsync(
            _buyer.Id, transaction.Id,
            new AcceptTransactionRequest(ValidWallet2),
            CancellationToken.None);

        Assert.Equal(AcceptTransactionStatus.SanctionsMatch, outcome.Status);
        Assert.Equal(TransactionErrorCodes.SanctionsMatch, outcome.ErrorCode);
    }

    [Fact]
    public async Task Wallet_Cooldown_Active_Rejects_With_403()
    {
        // Buyer changed refund address 1h ago, cooldown is 24h → blocked.
        _buyer.RefundAddressChangedAt = _clock.GetUtcNow().UtcDateTime.AddHours(-1);
        Context.Set<User>().Update(_buyer);
        await Context.SaveChangesAsync();

        var transaction = await CreateTransactionAsync(
            BuyerIdentificationMethod.STEAM_ID, BuyerSteamId);

        var sut = BuildSut();
        var outcome = await sut.AcceptAsync(
            _buyer.Id, transaction.Id,
            new AcceptTransactionRequest(ValidWallet2),
            CancellationToken.None);

        Assert.Equal(AcceptTransactionStatus.WalletCooldownActive, outcome.Status);
        Assert.Equal(TransactionErrorCodes.WalletChangeCooldownActive, outcome.ErrorCode);
    }

    [Fact]
    public async Task Wallet_Cooldown_Expired_Allows_Acceptance()
    {
        // Buyer changed address 25h ago, cooldown is 24h → allowed.
        _buyer.RefundAddressChangedAt = _clock.GetUtcNow().UtcDateTime.AddHours(-25);
        Context.Set<User>().Update(_buyer);
        await Context.SaveChangesAsync();

        var transaction = await CreateTransactionAsync(
            BuyerIdentificationMethod.STEAM_ID, BuyerSteamId);

        var sut = BuildSut();
        var outcome = await sut.AcceptAsync(
            _buyer.Id, transaction.Id,
            new AcceptTransactionRequest(ValidWallet2),
            CancellationToken.None);

        Assert.Equal(AcceptTransactionStatus.Accepted, outcome.Status);
    }

    [Fact]
    public async Task Transaction_Not_Found_Returns_404()
    {
        var sut = BuildSut();
        var outcome = await sut.AcceptAsync(
            _buyer.Id, Guid.NewGuid(),
            new AcceptTransactionRequest(ValidWallet2),
            CancellationToken.None);

        Assert.Equal(AcceptTransactionStatus.NotFound, outcome.Status);
        Assert.Equal(TransactionErrorCodes.TransactionNotFound, outcome.ErrorCode);
    }

    [Fact]
    public async Task Flagged_State_Rejects_Acceptance_As_Invalid_Transition()
    {
        // FLAGGED → ACCEPTED is not a permitted transition (05 §4.2): admin
        // must approve the flag back to CREATED first. The accept endpoint
        // should surface this as INVALID_STATE_TRANSITION.
        var transaction = await CreateTransactionAsync(
            BuyerIdentificationMethod.STEAM_ID, BuyerSteamId, status: TransactionStatus.FLAGGED);

        var sut = BuildSut();
        var outcome = await sut.AcceptAsync(
            _buyer.Id, transaction.Id,
            new AcceptTransactionRequest(ValidWallet2),
            CancellationToken.None);

        Assert.Equal(AcceptTransactionStatus.InvalidStateTransition, outcome.Status);
        Assert.Equal(TransactionErrorCodes.InvalidStateTransition, outcome.ErrorCode);
    }

    [Fact]
    public async Task Already_Accepted_Returns_Conflict()
    {
        var transaction = await CreateTransactionAsync(
            BuyerIdentificationMethod.STEAM_ID, BuyerSteamId, status: TransactionStatus.ACCEPTED);

        var sut = BuildSut();
        var outcome = await sut.AcceptAsync(
            _buyer.Id, transaction.Id,
            new AcceptTransactionRequest(ValidWallet2),
            CancellationToken.None);

        Assert.Equal(AcceptTransactionStatus.AlreadyAccepted, outcome.Status);
        Assert.Equal(TransactionErrorCodes.AlreadyAccepted, outcome.ErrorCode);
    }

    [Fact]
    public async Task Same_Refund_Address_Does_Not_Reset_Cooldown_Timer()
    {
        // Buyer's existing default address matches the request → no
        // RefundAddressChangedAt update; cooldown remains untouched.
        _buyer.DefaultRefundAddress = ValidWallet2;
        _buyer.RefundAddressChangedAt = null;
        Context.Set<User>().Update(_buyer);
        await Context.SaveChangesAsync();

        var transaction = await CreateTransactionAsync(
            BuyerIdentificationMethod.STEAM_ID, BuyerSteamId);

        var sut = BuildSut();
        var outcome = await sut.AcceptAsync(
            _buyer.Id, transaction.Id,
            new AcceptTransactionRequest(ValidWallet2),
            CancellationToken.None);

        Assert.Equal(AcceptTransactionStatus.Accepted, outcome.Status);
        var buyer = await Context.Set<User>().AsNoTracking().SingleAsync(u => u.Id == _buyer.Id);
        Assert.Null(buyer.RefundAddressChangedAt);
    }

    private async Task<Transaction> CreateTransactionAsync(
        BuyerIdentificationMethod method,
        string? targetSteamId,
        TransactionStatus status = TransactionStatus.CREATED)
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var transaction = new Transaction
        {
            Id = Guid.NewGuid(),
            Status = status,
            SellerId = _seller.Id,
            BuyerIdentificationMethod = method,
            TargetBuyerSteamId = method == BuyerIdentificationMethod.STEAM_ID ? targetSteamId : null,
            InviteToken = method == BuyerIdentificationMethod.OPEN_LINK ? "T46-test-invite" : null,
            ItemAssetId = "27348562891",
            ItemClassId = "abc-class",
            ItemName = "AK-47 | Redline",
            StablecoinType = StablecoinType.USDT,
            Price = 100m,
            CommissionRate = 0.02m,
            CommissionAmount = 2m,
            TotalAmount = 102m,
            SellerPayoutAddress = ValidWallet1,
            PaymentTimeoutMinutes = 1440,
            // CREATED + FLAGGED have different deadline invariants (06 §3.5).
            AcceptDeadline = status == TransactionStatus.CREATED ? nowUtc.AddHours(1) : null,
        };
        Context.Set<Transaction>().Add(transaction);
        await Context.SaveChangesAsync();
        return transaction;
    }

    private TransactionAcceptanceService BuildSut(IWalletSanctionsCheck? sanctions = null) =>
        new(
            Context,
            new Trc20AddressValidator(),
            sanctions ?? new NoMatchWalletSanctionsCheck(),
            _outbox,
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

    private sealed class MatchingSanctionsCheck : IWalletSanctionsCheck
    {
        private readonly string _list;
        public MatchingSanctionsCheck(string list) => _list = list;
        public Task<WalletSanctionsDecision> EvaluateAsync(string address, CancellationToken cancellationToken)
            => Task.FromResult(WalletSanctionsDecision.Match(_list));
    }
}
