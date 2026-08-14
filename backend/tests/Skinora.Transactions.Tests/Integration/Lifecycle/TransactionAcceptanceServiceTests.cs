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
using Skinora.Users.Application.Settings;
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
    private const string LateBuyerSteamId = "76561198000000999";
    private const string RaceWinnerSteamId = "76561198000000777";

    /// <summary>
    /// T119a — SteamID64 → SteamID32 offset, mirrored from the SUT so a typo in
    /// either place shows up as a failing ownership check rather than a silent
    /// pass. A trade URL's <c>partner</c> value is the SteamID32.
    /// </summary>
    private const ulong SteamId64ToId32Offset = 76561197960265728UL;

    /// <summary>Canonical trade URL whose <c>partner</c> belongs to <c>_buyer</c>.</summary>
    private static readonly string BuyerTradeUrl = TradeUrlFor(BuyerSteamId);

    private static string TradeUrlFor(string steamId64, string token = "AbCdEfGh")
        => $"https://steamcommunity.com/tradeoffer/new/?partner={ulong.Parse(steamId64) - SteamId64ToId32Offset}&token={token}";

    private User _seller = null!;
    private User _buyer = null!;
    private FakeTimeProvider _clock = null!;
    private RecordingOutboxService _outbox = null!;
    private CountingTradeHoldChecker _tradeHold = null!;

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
        // T119a — default: Steam reachable, buyer's Mobile Authenticator active.
        _tradeHold = new CountingTradeHoldChecker(new TradeHoldResult(true, true, null));
    }

    [Fact]
    public async Task Happy_Path_SteamId_Method_Transitions_To_Accepted_And_Emits_Outbox()
    {
        var transaction = await CreateTransactionAsync(BuyerIdentificationMethod.STEAM_ID, BuyerSteamId);

        var sut = BuildSut();
        var outcome = await sut.AcceptAsync(
            _buyer.Id, transaction.Id,
            new AcceptTransactionRequest(ValidWallet2, BuyerTradeUrl),
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
        // T119a — 06 §3.5: NOT NULL from ACCEPTED onwards.
        Assert.Equal(BuyerTradeUrl, persisted.BuyerTradeUrl);
        Assert.NotNull(persisted.AcceptedAt);

        Assert.Single(_outbox.Published);
        var evt = Assert.IsType<BuyerAcceptedEvent>(_outbox.Published[0]);
        Assert.Equal(transaction.Id, evt.TransactionId);
        Assert.Equal(_seller.Id, evt.SellerId);
        Assert.Equal(_buyer.Id, evt.BuyerId);

        // WP12 (T90 K3) — the accept-time refund address is a per-transaction
        // snapshot only (asserted on Transaction.BuyerRefundAddress above).
        // Accepting must NOT mutate the buyer's profile default or start the
        // profile-level cooldown (02 §12.2, 04 §7.3 "profil adresi etkilenmez").
        var buyer = await Context.Set<User>().AsNoTracking().SingleAsync(u => u.Id == _buyer.Id);
        Assert.Null(buyer.DefaultRefundAddress);
        Assert.Null(buyer.RefundAddressChangedAt);
    }

    [Fact]
    public async Task Rejects_Accept_When_Buyer_Suspended()
    {
        // T105a — a suspended buyer cannot accept a transaction (02 §14.0).
        var transaction = await CreateTransactionAsync(BuyerIdentificationMethod.STEAM_ID, BuyerSteamId);
        var buyer = await Context.Set<User>().SingleAsync(u => u.Id == _buyer.Id);
        buyer.IsSuspended = true;
        await Context.SaveChangesAsync();

        var sut = BuildSut();
        var outcome = await sut.AcceptAsync(
            _buyer.Id, transaction.Id,
            new AcceptTransactionRequest(ValidWallet2, BuyerTradeUrl),
            CancellationToken.None);

        Assert.Equal(AcceptTransactionStatus.BuyerNotFound, outcome.Status);
    }

    [Fact]
    public async Task Flagged_Buyer_Cannot_Accept_Returns_AccountFlagged()
    {
        // WP4a — an account-flagged buyer is blocked from accepting (02 §14.0).
        // The gate fires before any wallet/state work, so the transaction stays
        // CREATED and no outbox event is emitted (fail-fast, no DB mutation).
        var transaction = await CreateTransactionAsync(BuyerIdentificationMethod.STEAM_ID, BuyerSteamId);

        var sut = BuildSut(flagChecker: new StubAccountFlagChecker(true));
        var outcome = await sut.AcceptAsync(
            _buyer.Id, transaction.Id,
            new AcceptTransactionRequest(ValidWallet2, BuyerTradeUrl),
            CancellationToken.None);

        Assert.Equal(AcceptTransactionStatus.AccountFlagged, outcome.Status);
        Assert.Equal(TransactionErrorCodes.AccountFlagged, outcome.ErrorCode);

        var persisted = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == transaction.Id);
        Assert.Equal(TransactionStatus.CREATED, persisted.Status);
        Assert.Null(persisted.BuyerId);
        Assert.Empty(_outbox.Published);
    }

    [Fact]
    public async Task Steam_Id_Mismatch_Rejects_With_403()
    {
        var transaction = await CreateTransactionAsync(
            BuyerIdentificationMethod.STEAM_ID, "76561198000099999");

        var sut = BuildSut();
        var outcome = await sut.AcceptAsync(
            _buyer.Id, transaction.Id,
            new AcceptTransactionRequest(ValidWallet2, BuyerTradeUrl),
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
            SteamId = LateBuyerSteamId,
            SteamDisplayName = "LateBuyer",
        };
        Context.Set<User>().Add(lateBuyer);
        await Context.SaveChangesAsync();

        var sut = BuildSut();
        var first = await sut.AcceptAsync(
            _buyer.Id, transaction.Id,
            new AcceptTransactionRequest(ValidWallet2, BuyerTradeUrl),
            CancellationToken.None);
        Assert.Equal(AcceptTransactionStatus.Accepted, first.Status);

        // Second comer hits the state guard — CREATED → ACCEPTED happens once,
        // so the second AcceptAsync sees status=ACCEPTED and short-circuits to
        // ALREADY_ACCEPTED. This is the 07 §7.6 contract for repeat callers.
        var second = await sut.AcceptAsync(
            lateBuyer.Id, transaction.Id,
            new AcceptTransactionRequest(ValidWallet1, TradeUrlFor(LateBuyerSteamId)),
            CancellationToken.None);

        Assert.Equal(AcceptTransactionStatus.AlreadyAccepted, second.Status);
        Assert.Equal(TransactionErrorCodes.AlreadyAccepted, second.ErrorCode);
    }

    [Fact]
    public async Task Open_Link_Concurrent_Accept_Race_Loser_Returns_AlreadyAccepted_Not_500()
    {
        // WP12 (T46) — two buyers accept the same OPEN_LINK invite at once: both
        // pass the CREATED state guard, then the race-loser's SaveChanges hits
        // the RowVersion optimistic-concurrency token. RaceAcceptDbContext
        // commits a competing accept (bumping RowVersion) in the window just
        // before the SUT saves, so the SUT's UPDATE matches zero rows →
        // DbUpdateConcurrencyException. The service must surface that as 409
        // ALREADY_ACCEPTED, not an unhandled 500.
        var transaction = await CreateTransactionAsync(
            BuyerIdentificationMethod.OPEN_LINK, targetSteamId: null);
        var winner = new User
        {
            Id = Guid.NewGuid(),
            SteamId = RaceWinnerSteamId,
            SteamDisplayName = "RaceWinner",
        };
        Context.Set<User>().Add(winner);
        await Context.SaveChangesAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;
        await using var raceDb = new RaceAcceptDbContext(options, async () =>
        {
            await using var competing = new AppDbContext(options);
            var row = await competing.Set<Transaction>().SingleAsync(t => t.Id == transaction.Id);
            row.BuyerId = winner.Id;
            row.BuyerRefundAddress = ValidWallet1;
            row.Status = TransactionStatus.ACCEPTED;
            row.AcceptedAt = _clock.GetUtcNow().UtcDateTime;
            await competing.SaveChangesAsync();
        });

        var sut = new TransactionAcceptanceService(
            raceDb,
            new Trc20AddressValidator(),
            new NoMatchWalletSanctionsCheck(),
            new StubAccountFlagChecker(false),
            new TradeUrlParser(),
            _tradeHold,
            _outbox,
            _clock);

        var outcome = await sut.AcceptAsync(
            _buyer.Id, transaction.Id,
            new AcceptTransactionRequest(ValidWallet2, BuyerTradeUrl),
            CancellationToken.None);

        Assert.Equal(AcceptTransactionStatus.AlreadyAccepted, outcome.Status);
        Assert.Equal(TransactionErrorCodes.AlreadyAccepted, outcome.ErrorCode);

        // The winner's accept stuck; the loser overwrote nothing.
        var persisted = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == transaction.Id);
        Assert.Equal(winner.Id, persisted.BuyerId);
    }

    [Fact]
    public async Task Concurrent_Cancel_During_Accept_Rethrows_Not_Masked_As_AlreadyAccepted()
    {
        // WP12 (T46) — the concurrency catch only swallows when the row really
        // reached ACCEPTED. A competing CANCEL during the accept save flips the
        // row to a non-ACCEPTED terminal state; the re-query then fails the
        // ACCEPTED check and the original DbUpdateConcurrencyException re-throws
        // unmasked (it must not be mislabelled ALREADY_ACCEPTED / swallowed).
        var transaction = await CreateTransactionAsync(
            BuyerIdentificationMethod.STEAM_ID, BuyerSteamId);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;
        await using var raceDb = new RaceAcceptDbContext(options, async () =>
        {
            await using var competing = new AppDbContext(options);
            var row = await competing.Set<Transaction>().SingleAsync(t => t.Id == transaction.Id);
            row.Status = TransactionStatus.CANCELLED_ADMIN;
            row.CancelledBy = CancelledByType.ADMIN;
            row.CancelReason = "admin cancel during accept (test)";
            row.CancelledAt = _clock.GetUtcNow().UtcDateTime;
            await competing.SaveChangesAsync();
        });

        var sut = new TransactionAcceptanceService(
            raceDb,
            new Trc20AddressValidator(),
            new NoMatchWalletSanctionsCheck(),
            new StubAccountFlagChecker(false),
            new TradeUrlParser(),
            _tradeHold,
            _outbox,
            _clock);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
            sut.AcceptAsync(
                _buyer.Id, transaction.Id,
                new AcceptTransactionRequest(ValidWallet2, BuyerTradeUrl),
                CancellationToken.None));
    }

    [Fact]
    public async Task Open_Link_Seller_Cannot_Accept_Own_Listing()
    {
        var transaction = await CreateTransactionAsync(
            BuyerIdentificationMethod.OPEN_LINK, targetSteamId: null);

        var sut = BuildSut();
        var outcome = await sut.AcceptAsync(
            _seller.Id, transaction.Id,
            new AcceptTransactionRequest(ValidWallet2, TradeUrlFor(SellerSteamId)),
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
            new AcceptTransactionRequest("   ", BuyerTradeUrl),
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
            new AcceptTransactionRequest("NOT_A_TRC20_ADDRESS", BuyerTradeUrl),
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
            new AcceptTransactionRequest(ValidWallet2, BuyerTradeUrl),
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
            new AcceptTransactionRequest(ValidWallet2, BuyerTradeUrl),
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
            new AcceptTransactionRequest(ValidWallet2, BuyerTradeUrl),
            CancellationToken.None);

        Assert.Equal(AcceptTransactionStatus.Accepted, outcome.Status);
    }

    [Fact]
    public async Task Transaction_Not_Found_Returns_404()
    {
        var sut = BuildSut();
        var outcome = await sut.AcceptAsync(
            _buyer.Id, Guid.NewGuid(),
            new AcceptTransactionRequest(ValidWallet2, BuyerTradeUrl),
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
            new AcceptTransactionRequest(ValidWallet2, BuyerTradeUrl),
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
            new AcceptTransactionRequest(ValidWallet2, BuyerTradeUrl),
            CancellationToken.None);

        Assert.Equal(AcceptTransactionStatus.AlreadyAccepted, outcome.Status);
        Assert.Equal(TransactionErrorCodes.AlreadyAccepted, outcome.ErrorCode);
    }

    [Fact]
    public async Task Accept_With_Different_Address_Does_Not_Mutate_Profile_Default()
    {
        // WP12 (T90 K3) — even when the accept-time address differs from the
        // buyer's profile default, accepting leaves User.DefaultRefundAddress +
        // RefundAddressChangedAt untouched (02 §12.2, 04 §7.3 "profil adresi
        // etkilenmez"). The per-transaction address lives only on
        // Transaction.BuyerRefundAddress. This replaces the prior
        // "same address does not reset cooldown" test — the accept path no
        // longer writes the profile at all, so the differentiating branch is
        // gone and the meaningful invariant is "profile is never touched".
        _buyer.DefaultRefundAddress = ValidWallet1;
        _buyer.RefundAddressChangedAt = null;
        Context.Set<User>().Update(_buyer);
        await Context.SaveChangesAsync();

        var transaction = await CreateTransactionAsync(
            BuyerIdentificationMethod.STEAM_ID, BuyerSteamId);

        var sut = BuildSut();
        var outcome = await sut.AcceptAsync(
            _buyer.Id, transaction.Id,
            new AcceptTransactionRequest(ValidWallet2, BuyerTradeUrl),
            CancellationToken.None);

        Assert.Equal(AcceptTransactionStatus.Accepted, outcome.Status);
        var persisted = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == transaction.Id);
        Assert.Equal(ValidWallet2, persisted.BuyerRefundAddress); // snapshot = request
        var buyer = await Context.Set<User>().AsNoTracking().SingleAsync(u => u.Id == _buyer.Id);
        Assert.Equal(ValidWallet1, buyer.DefaultRefundAddress);   // profile unchanged
        Assert.Null(buyer.RefundAddressChangedAt);                // no cooldown started
    }

    // ---------------------------------------------------------------------
    // T119a — 07 §7.6 v3.0 fields: mandatory steamTradeUrl + buyer MA gate.
    // ---------------------------------------------------------------------

    [Fact]
    public async Task Accept_Persists_Normalized_BuyerTradeUrl_And_Probes_With_Its_Token()
    {
        // AC2 — the value 06 §3.5 requires from ACCEPTED onwards is actually
        // written, and it is the NORMALIZED form (the seller's delivery CTA is
        // generated from this column, 08 §2.2).
        var transaction = await CreateTransactionAsync(BuyerIdentificationMethod.STEAM_ID, BuyerSteamId);

        // Raw input a real buyer produces: http scheme, upper-case host, Steam's
        // own language/tracking parameters, surrounding whitespace.
        var partner = ulong.Parse(BuyerSteamId) - SteamId64ToId32Offset;
        var raw = $"  http://STEAMCOMMUNITY.COM/tradeoffer/new/?partner={partner}&token=AbCdEfGh&l=turkish  ";

        var sut = BuildSut();
        var outcome = await sut.AcceptAsync(
            _buyer.Id, transaction.Id,
            new AcceptTransactionRequest(ValidWallet2, raw),
            CancellationToken.None);

        Assert.Equal(AcceptTransactionStatus.Accepted, outcome.Status);

        var persisted = await Context.Set<Transaction>().AsNoTracking()
            .SingleAsync(t => t.Id == transaction.Id);
        Assert.Equal(
            $"https://steamcommunity.com/tradeoffer/new/?partner={partner}&token=AbCdEfGh",
            persisted.BuyerTradeUrl);

        // AC3 — the probe is keyed on the buyer's own SteamID and on the token
        // parsed from THIS request, not on a profile-stored token.
        Assert.Equal(1, _tradeHold.CallCount);
        Assert.Equal(BuyerSteamId, _tradeHold.LastSteamId);
        Assert.Equal("AbCdEfGh", _tradeHold.LastAccessToken);
    }

    [Theory]
    // Missing / empty — 07 §7.6 makes the field mandatory and gives every
    // malformed case the same code.
    [InlineData("")]
    [InlineData("   ")]
    // Scheme missing — the single most common paste error.
    [InlineData("steamcommunity.com/tradeoffer/new/?partner=39734353&token=AbCdEfGh")]
    // Wrong host family: subdomain, look-alike suffix, non-http scheme.
    [InlineData("https://www.steamcommunity.com/tradeoffer/new/?partner=39734353&token=AbCdEfGh")]
    [InlineData("https://steamcommunity.com.evil.tr/tradeoffer/new/?partner=39734353&token=AbCdEfGh")]
    [InlineData("javascript:alert(1)")]
    // Wrong path (trailing slash / casing are part of the contract).
    [InlineData("https://steamcommunity.com/tradeoffer/new?partner=39734353&token=AbCdEfGh")]
    [InlineData("https://steamcommunity.com/TradeOffer/New/?partner=39734353&token=AbCdEfGh")]
    // Missing or non-conforming query values.
    [InlineData("https://steamcommunity.com/tradeoffer/new/?partner=39734353")]
    [InlineData("https://steamcommunity.com/tradeoffer/new/?token=AbCdEfGh")]
    [InlineData("https://steamcommunity.com/tradeoffer/new/?partner=abc&token=AbCdEfGh")]
    [InlineData("https://steamcommunity.com/tradeoffer/new/?partner=39734353&token=Ab$Cd")]
    public async Task Malformed_Trade_Url_Rejected_With_InvalidTradeUrl(string tradeUrl)
    {
        var transaction = await CreateTransactionAsync(BuyerIdentificationMethod.STEAM_ID, BuyerSteamId);

        var sut = BuildSut();
        var outcome = await sut.AcceptAsync(
            _buyer.Id, transaction.Id,
            new AcceptTransactionRequest(ValidWallet2, tradeUrl),
            CancellationToken.None);

        Assert.Equal(AcceptTransactionStatus.InvalidTradeUrl, outcome.Status);
        Assert.Equal(TransactionErrorCodes.InvalidTradeUrl, outcome.ErrorCode);

        // Rejection happens before any mutation and before the Steam round-trip.
        var persisted = await Context.Set<Transaction>().AsNoTracking()
            .SingleAsync(t => t.Id == transaction.Id);
        Assert.Equal(TransactionStatus.CREATED, persisted.Status);
        Assert.Null(persisted.BuyerId);
        Assert.Null(persisted.BuyerTradeUrl);
        Assert.Empty(_outbox.Published);
        Assert.Equal(0, _tradeHold.CallCount);
    }

    [Fact]
    public async Task Trade_Url_Of_A_Third_Party_Is_Rejected()
    {
        // T119a ownership cross-check (owner decision 2026-08-10). A well-formed
        // URL that points at somebody else's account would make the seller ship
        // the item to a stranger while the escrowed money still settles to the
        // seller — BuyerTradeUrl is the only field deciding the destination.
        var transaction = await CreateTransactionAsync(BuyerIdentificationMethod.STEAM_ID, BuyerSteamId);

        var sut = BuildSut();
        var outcome = await sut.AcceptAsync(
            _buyer.Id, transaction.Id,
            new AcceptTransactionRequest(ValidWallet2, TradeUrlFor(LateBuyerSteamId)),
            CancellationToken.None);

        Assert.Equal(AcceptTransactionStatus.InvalidTradeUrl, outcome.Status);
        Assert.Equal(TransactionErrorCodes.InvalidTradeUrl, outcome.ErrorCode);
        Assert.Equal(0, _tradeHold.CallCount);

        var persisted = await Context.Set<Transaction>().AsNoTracking()
            .SingleAsync(t => t.Id == transaction.Id);
        Assert.Equal(TransactionStatus.CREATED, persisted.Status);
        Assert.Empty(_outbox.Published);
    }

    [Fact]
    public async Task Mobile_Authenticator_Inactive_Rejects_And_Writes_Nothing()
    {
        // 02 §9.1 — without the buyer's MA the seller's trade would land in
        // Steam's 15-day escrow, which is exactly what the P2P pivot removes.
        var transaction = await CreateTransactionAsync(BuyerIdentificationMethod.STEAM_ID, BuyerSteamId);

        var sut = BuildSut(
            tradeHold: new CountingTradeHoldChecker(
                new TradeHoldResult(Available: true, Active: false, SetupGuideUrl: "https://guide")));
        var outcome = await sut.AcceptAsync(
            _buyer.Id, transaction.Id,
            new AcceptTransactionRequest(ValidWallet2, BuyerTradeUrl),
            CancellationToken.None);

        Assert.Equal(AcceptTransactionStatus.MobileAuthenticatorRequired, outcome.Status);
        Assert.Equal(TransactionErrorCodes.MobileAuthenticatorRequired, outcome.ErrorCode);

        var persisted = await Context.Set<Transaction>().AsNoTracking()
            .SingleAsync(t => t.Id == transaction.Id);
        Assert.Equal(TransactionStatus.CREATED, persisted.Status);
        Assert.Null(persisted.BuyerId);
        Assert.Null(persisted.BuyerRefundAddress);
        Assert.Null(persisted.BuyerTradeUrl);
        Assert.Empty(_outbox.Published);
    }

    [Fact]
    public async Task Steam_Unreachable_Fails_Closed_With_SteamUnavailable()
    {
        // 08 §2.2 fail-closed. Deliberately NOT MOBILE_AUTHENTICATOR_REQUIRED:
        // the buyer's authenticator may be perfectly fine, and telling them to
        // enable it would send them after a problem they cannot fix. Same code
        // the 07 §7.6a confirm-ready step uses for the same condition.
        var transaction = await CreateTransactionAsync(BuyerIdentificationMethod.STEAM_ID, BuyerSteamId);

        var sut = BuildSut(
            tradeHold: new CountingTradeHoldChecker(
                new TradeHoldResult(Available: false, Active: false, SetupGuideUrl: null)));
        var outcome = await sut.AcceptAsync(
            _buyer.Id, transaction.Id,
            new AcceptTransactionRequest(ValidWallet2, BuyerTradeUrl),
            CancellationToken.None);

        Assert.Equal(AcceptTransactionStatus.SteamUnavailable, outcome.Status);
        Assert.Equal(TransactionErrorCodes.SteamUnavailable, outcome.ErrorCode);

        var persisted = await Context.Set<Transaction>().AsNoTracking()
            .SingleAsync(t => t.Id == transaction.Id);
        Assert.Equal(TransactionStatus.CREATED, persisted.Status);
        Assert.Null(persisted.BuyerTradeUrl);
        Assert.Empty(_outbox.Published);
    }

    [Fact]
    public async Task Accept_Arms_The_SellerConfirmDeadline_From_The_SystemSetting()
    {
        // T123 — before this task nothing wrote SellerConfirmDeadline. Every
        // reader existed (DeadlineScannerJob's ACCEPTED branch, freeze, the
        // countdown broadcaster, the detail/list timeout blocks) and none of
        // them could ever fire, so a seller who accepted and went quiet had no
        // time bound on them at all.
        await Context.ConfigureSettingAsync(
            TransactionAcceptanceService.SellerConfirmTimeoutKey, "45");
        var transaction = await CreateTransactionAsync(BuyerIdentificationMethod.STEAM_ID, BuyerSteamId);
        var nowUtc = _clock.GetUtcNow().UtcDateTime;

        var outcome = await BuildSut().AcceptAsync(
            _buyer.Id, transaction.Id,
            new AcceptTransactionRequest(ValidWallet2, BuyerTradeUrl),
            CancellationToken.None);

        Assert.Equal(AcceptTransactionStatus.Accepted, outcome.Status);
        var persisted = await Context.Set<Transaction>().AsNoTracking()
            .SingleAsync(t => t.Id == transaction.Id);
        Assert.Equal(nowUtc.AddMinutes(45), persisted.SellerConfirmDeadline);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("not-a-number")]
    public async Task Accept_Falls_Back_To_The_Documented_Default_For_An_Unusable_Setting(
        string? configured)
    {
        // A zero/negative/garbage window would arm the deadline in the past and
        // cancel the transaction on the next scanner pass — punishing the
        // seller for an admin's typo. Fall back to the 02 §16.2 default.
        if (configured is not null)
            await Context.ConfigureSettingAsync(
                TransactionAcceptanceService.SellerConfirmTimeoutKey, configured);
        var transaction = await CreateTransactionAsync(BuyerIdentificationMethod.STEAM_ID, BuyerSteamId);
        var nowUtc = _clock.GetUtcNow().UtcDateTime;

        await BuildSut().AcceptAsync(
            _buyer.Id, transaction.Id,
            new AcceptTransactionRequest(ValidWallet2, BuyerTradeUrl),
            CancellationToken.None);

        var persisted = await Context.Set<Transaction>().AsNoTracking()
            .SingleAsync(t => t.Id == transaction.Id);
        Assert.Equal(
            nowUtc.AddMinutes(TransactionAcceptanceService.DefaultSellerConfirmTimeoutMinutes),
            persisted.SellerConfirmDeadline);
    }

    [Fact]
    public async Task Cheaper_Rejections_Never_Spend_A_Steam_Round_Trip()
    {
        // Pipeline-ordering guard: the MA probe is the last gate precisely
        // because it is the only network call and the sidecar queues Steam at
        // 1 req/s. A wrong-buyer request must be rejected without touching it.
        var transaction = await CreateTransactionAsync(
            BuyerIdentificationMethod.STEAM_ID, "76561198000099999");

        var sut = BuildSut();
        var outcome = await sut.AcceptAsync(
            _buyer.Id, transaction.Id,
            new AcceptTransactionRequest(ValidWallet2, BuyerTradeUrl),
            CancellationToken.None);

        Assert.Equal(AcceptTransactionStatus.SteamIdMismatch, outcome.Status);
        Assert.Equal(0, _tradeHold.CallCount);
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

    private TransactionAcceptanceService BuildSut(
        IWalletSanctionsCheck? sanctions = null,
        IAccountFlagChecker? flagChecker = null,
        ITradeHoldChecker? tradeHold = null) =>
        new(
            Context,
            new Trc20AddressValidator(),
            sanctions ?? new NoMatchWalletSanctionsCheck(),
            flagChecker ?? new StubAccountFlagChecker(false),
            // T119a — the real U17 parser, not a stub: the accept endpoint's
            // trade-URL contract IS that parser's contract (07 §7.6 md.2).
            new TradeUrlParser(),
            tradeHold ?? _tradeHold,
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

    // T119a — drives the Stage 5b Mobile Authenticator probe. Counts calls so a
    // test can prove the probe is NOT reached when a cheaper gate already
    // rejected the request (the sidecar queues Steam at 1 req/s).
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

    // WP4a — drives the account-flag accept gate. The production
    // AccountFlagChecker (Skinora.Fraud) queries FraudFlag rows; the gate logic
    // under test only needs the boolean verdict.
    private sealed class StubAccountFlagChecker : IAccountFlagChecker
    {
        private readonly bool _flagged;
        public StubAccountFlagChecker(bool flagged) => _flagged = flagged;
        public Task<bool> HasActiveAccountFlagAsync(Guid userId, CancellationToken cancellationToken)
            => Task.FromResult(_flagged);
    }

    // WP12 (T46) — fires the injected hook once, immediately before the first
    // Transaction-modifying SaveChanges, to commit a competing write that bumps
    // the row's RowVersion and force the optimistic-concurrency conflict the
    // accept path must translate (mirrors the WP1/WP2 RaceDbContext seam).
    private sealed class RaceAcceptDbContext : AppDbContext
    {
        private readonly Func<Task> _injectBeforeSave;
        private bool _fired;

        public RaceAcceptDbContext(DbContextOptions<AppDbContext> options, Func<Task> injectBeforeSave)
            : base(options) => _injectBeforeSave = injectBeforeSave;

        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (!_fired && ChangeTracker.Entries<Transaction>().Any(e => e.State == EntityState.Modified))
            {
                _fired = true;
                await _injectBeforeSave();
            }
            return await base.SaveChangesAsync(cancellationToken);
        }
    }
}
