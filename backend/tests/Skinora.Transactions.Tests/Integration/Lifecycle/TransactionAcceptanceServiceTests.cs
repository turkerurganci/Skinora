using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Skinora.Shared.Domain;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Interfaces;
using Skinora.Shared.Persistence;
using Skinora.Shared.Tests.Integration;
using Skinora.Transactions.Application.Lifecycle;
using Skinora.Transactions.Application.Networks;
using Skinora.Transactions.Tests.Helpers;
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

    // Real base58check TRON addresses (T146 — the refund address is validated
    // with the checksum); tronweb 5.3.5 fromHex.
    private const string ValidWallet1 = "TSUj7JoASMqs36TxFnpK9zsHs7wi1ji2vE";
    private const string ValidWallet2 = "TMwDwmmHe5vTFDEiGNWF8RGxic9b8a2sdY";
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
    private IBlockchainNetworkAvailability _networks = null!;
    private RecordingPaymentAddressAllocator _allocator = null!;
    // T149 — 0 on every network unless a test sets a line, so the pre-T149
    // totals asserted below are unchanged.
    private StubNetworkFeeQuotes _networkFees = new();
    private Skinora.Transactions.Application.GasFee.INetworkFeeQuoteService? _networkFeesOverride;

    protected override async Task SeedAsync(AppDbContext context)
    {
        _seller = new User
        {
            Id = Guid.NewGuid(),
            SteamId = SellerSteamId,
            SteamDisplayName = "Seller",
            WalletAddresses = { UserWalletAddress.For(BlockchainNetwork.TRON, WalletAddressType.PAYOUT, ValidWallet1) },
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
        _networks = TestNetworkAvailability.TronOnly();
        _allocator = new RecordingPaymentAddressAllocator();
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
        Assert.False(await Context.Set<UserWalletAddress>().AsNoTracking()
            .AnyAsync(w => w.UserId == _buyer.Id));

        // K9 (11 §F8) — the accept-time refund address is recorded together
        // with the network it belongs to (TRON until T147).
        Assert.Equal(BlockchainNetwork.TRON, persisted.Network);
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
            row.Network = Skinora.Shared.Enums.BlockchainNetwork.TRON;
            row.SellerPayoutAddress = ValidWallet1;
            await competing.SaveChangesAsync();
        });

        var sut = new TransactionAcceptanceService(
            raceDb,
            new WalletAddressValidator(),
            new NoMatchWalletSanctionsCheck(),
            new StubAccountFlagChecker(false),
            new TradeUrlParser(),
            _tradeHold,
            new FakeSteamTradeEligibilityChecker(),
            _outbox,
            _networks,
            _allocator,
            _networkFees,
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
            new WalletAddressValidator(),
            new NoMatchWalletSanctionsCheck(),
            new StubAccountFlagChecker(false),
            new TradeUrlParser(),
            _tradeHold,
            new FakeSteamTradeEligibilityChecker(),
            _outbox,
            _networks,
            _allocator,
            _networkFees,
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

    [Theory]
    [InlineData("NOT_A_TRC20_ADDRESS")]
    // T146 — TRON shape (34 chars, 'T', base58) with a broken checksum: the
    // pre-T146 regex validator accepted it (tronweb isAddress = false).
    [InlineData("TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6u")]
    // T146 — valid base58check over version byte 0x42 instead of 0x41: still
    // 34 chars starting with 'T', not a TRON address (tronweb isAddress = false).
    [InlineData("TpSyGx2w2bR9GdrDrYtrYwbvSPhwSmg7ER")]
    // An Ethereum address is not a TRON refund address while every transaction is TRON.
    [InlineData("0x5aAeb6053F3E94C9b9A09f33669435E7Ef1BeAed")]
    public async Task Refund_Address_Format_Invalid_Returns_400(string refundAddress)
    {
        var transaction = await CreateTransactionAsync(
            BuyerIdentificationMethod.STEAM_ID, BuyerSteamId);

        var sut = BuildSut();
        var outcome = await sut.AcceptAsync(
            _buyer.Id, transaction.Id,
            new AcceptTransactionRequest(refundAddress, BuyerTradeUrl),
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
        await SetBuyerRefundAsync(BlockchainNetwork.TRON, ValidWallet1, _clock.GetUtcNow().UtcDateTime.AddHours(-1));

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
        await SetBuyerRefundAsync(BlockchainNetwork.TRON, ValidWallet1, _clock.GetUtcNow().UtcDateTime.AddHours(-25));

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
    public async Task Wallet_Cooldown_Is_Per_Network_Ethereum_Change_Does_Not_Block_Tron_Acceptance()
    {
        // T146 (02 §12.3, 06 §1.4): the buyer changed the ETHEREUM refund
        // address 1h ago (24h cooldown). The transaction is TRON (until T147),
        // so only the TRON refund address's cooldown applies — and the TRON
        // address was set long ago.
        await SetBuyerRefundAsync(BlockchainNetwork.TRON, ValidWallet1, _clock.GetUtcNow().UtcDateTime.AddDays(-10));
        await SetBuyerRefundAsync(BlockchainNetwork.ETHEREUM, "0x5aaeb6053f3e94c9b9a09f33669435e7ef1beaed",
            _clock.GetUtcNow().UtcDateTime.AddHours(-1));

        var transaction = await CreateTransactionAsync(
            BuyerIdentificationMethod.STEAM_ID, BuyerSteamId);

        var sut = BuildSut();
        var outcome = await sut.AcceptAsync(
            _buyer.Id, transaction.Id,
            new AcceptTransactionRequest(ValidWallet2, BuyerTradeUrl),
            CancellationToken.None);

        Assert.Equal(AcceptTransactionStatus.Accepted, outcome.Status);
    }

    private async Task SetBuyerRefundAsync(BlockchainNetwork network, string address, DateTime? changedAt)
    {
        var row = UserWalletAddress.For(network, WalletAddressType.REFUND, address, changedAt);
        row.UserId = _buyer.Id;
        Context.Set<UserWalletAddress>().Add(row);
        await Context.SaveChangesAsync();
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
        // buyer's profile default, accepting leaves the buyer's REFUND
        // UserWalletAddress row (address + ChangedAt) untouched (02 §12.2, 04 §7.3 "profil adresi
        // etkilenmez"). The per-transaction address lives only on
        // Transaction.BuyerRefundAddress. This replaces the prior
        // "same address does not reset cooldown" test — the accept path no
        // longer writes the profile at all, so the differentiating branch is
        // gone and the meaningful invariant is "profile is never touched".
        await SetBuyerRefundAsync(BlockchainNetwork.TRON, ValidWallet1, changedAt: null);

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
        var profile = await Context.Set<UserWalletAddress>().AsNoTracking()
            .SingleAsync(w => w.UserId == _buyer.Id && w.Type == WalletAddressType.REFUND);
        Assert.Equal(ValidWallet1, profile.Address);   // profile unchanged
        Assert.Null(profile.ChangedAt);                // no cooldown started
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

    // ---------- 08 §2.2a — Steam trade eligibility (the 🔴 this round closes) ----------

    [Fact]
    public async Task Limited_Buyer_Is_Rejected_With_Its_Own_Code()
    {
        // The 2026-09-02 rehearsal in one test: the trade-hold probe says the
        // buyer is fine (a limited account reports a 0-second hold too), and
        // before this gate the accept went through, the buyer paid, and the
        // item turned out to be undeliverable AFTER the money was escrowed.
        var transaction = await CreateTransactionAsync(BuyerIdentificationMethod.STEAM_ID, BuyerSteamId);

        var sut = BuildSut(steamEligibility: FakeSteamTradeEligibilityChecker.Limited());
        var outcome = await sut.AcceptAsync(
            _buyer.Id, transaction.Id,
            new AcceptTransactionRequest(ValidWallet2, BuyerTradeUrl),
            CancellationToken.None);

        Assert.Equal(AcceptTransactionStatus.SteamAccountLimited, outcome.Status);
        Assert.Equal(TransactionErrorCodes.SteamAccountLimited, outcome.ErrorCode);

        var persisted = await Context.Set<Transaction>().AsNoTracking()
            .SingleAsync(t => t.Id == transaction.Id);
        Assert.Equal(TransactionStatus.CREATED, persisted.Status);
        Assert.Null(persisted.BuyerId);
        Assert.Null(persisted.BuyerTradeUrl);
        Assert.Empty(_outbox.Published);
    }

    [Fact]
    public async Task Buyer_Inside_The_15_Day_Wait_Is_Rejected_With_Its_Own_Code()
    {
        var transaction = await CreateTransactionAsync(BuyerIdentificationMethod.STEAM_ID, BuyerSteamId);

        var sut = BuildSut(steamEligibility: FakeSteamTradeEligibilityChecker.TooNew(4));
        var outcome = await sut.AcceptAsync(
            _buyer.Id, transaction.Id,
            new AcceptTransactionRequest(ValidWallet2, BuyerTradeUrl),
            CancellationToken.None);

        Assert.Equal(AcceptTransactionStatus.SteamAccountTooNew, outcome.Status);
        Assert.Equal(TransactionErrorCodes.SteamAccountTooNew, outcome.ErrorCode);
        // The remedy is time, so the message has to carry how much is left.
        Assert.Contains("4", outcome.ErrorMessage);
    }

    [Fact]
    public async Task Unreadable_Eligibility_Fails_Closed_As_SteamUnavailable()
    {
        // Absence of information is not a positive finding: a buyer whose
        // account is perfectly fine must not be told they are limited.
        var transaction = await CreateTransactionAsync(BuyerIdentificationMethod.STEAM_ID, BuyerSteamId);

        var sut = BuildSut(steamEligibility: FakeSteamTradeEligibilityChecker.Unknown());
        var outcome = await sut.AcceptAsync(
            _buyer.Id, transaction.Id,
            new AcceptTransactionRequest(ValidWallet2, BuyerTradeUrl),
            CancellationToken.None);

        Assert.Equal(AcceptTransactionStatus.SteamUnavailable, outcome.Status);
        Assert.Equal(TransactionErrorCodes.SteamUnavailable, outcome.ErrorCode);
    }

    [Fact]
    public async Task Eligibility_Is_Not_Probed_When_A_Cheaper_Rule_Already_Rejected()
    {
        // The Steam Community budget is ~10 requests a minute and it is shared
        // with delivery verification, so a request already doomed by the MA
        // check must not spend one.
        var transaction = await CreateTransactionAsync(BuyerIdentificationMethod.STEAM_ID, BuyerSteamId);
        var eligibility = new FakeSteamTradeEligibilityChecker();

        var sut = BuildSut(
            tradeHold: new CountingTradeHoldChecker(
                new TradeHoldResult(Available: true, Active: false, SetupGuideUrl: "https://guide")),
            steamEligibility: eligibility);
        await sut.AcceptAsync(
            _buyer.Id, transaction.Id,
            new AcceptTransactionRequest(ValidWallet2, BuyerTradeUrl),
            CancellationToken.None);

        Assert.Equal(0, eligibility.CallCount);
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

    // ---- T147 — network choice at acceptance (07 §1.3, 11 §F8 K3/K9) -------

    // EIP-55 specification vectors (eips.ethereum.org/EIPS/eip-55).
    private const string SellerEthereumPayout = "0x5aaeb6053f3e94c9b9a09f33669435e7ef1beaed";
    private const string BuyerEthereumRefundChecksummed = "0xfB6916095ca1df60bB79Ce92cE3Ea74c37c5d359";
    private const string BuyerEthereumRefundCanonical = "0xfb6916095ca1df60bb79ce92ce3ea74c37c5d359";

    private async Task<Transaction> CreateListingAsync(params BlockchainNetwork[] networks)
    {
        var transaction = await CreateTransactionAsync(
            BuyerIdentificationMethod.STEAM_ID, BuyerSteamId, networks: networks);
        return transaction;
    }

    [Fact]
    public async Task Buyer_Chooses_Ethereum_Payout_And_Refund_Fixed_On_Ethereum_Deposit_Allocated()
    {
        _networks = TestNetworkAvailability.All();
        var transaction = await CreateListingAsync(BlockchainNetwork.TRON, BlockchainNetwork.ETHEREUM);

        var outcome = await BuildSut().AcceptAsync(
            _buyer.Id, transaction.Id,
            new AcceptTransactionRequest(BuyerEthereumRefundChecksummed, BuyerTradeUrl, Network: "ETHEREUM"),
            CancellationToken.None);

        Assert.Equal(AcceptTransactionStatus.Accepted, outcome.Status);
        Assert.Equal("ETHEREUM", outcome.Body!.Network);
        Assert.Equal("102.00", outcome.Body.TotalAmount);
        var persisted = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == transaction.Id);
        Assert.Equal(BlockchainNetwork.ETHEREUM, persisted.Network);
        // K3 — the seller is paid on the network the buyer pays on, from the
        // snapshot fixed at creation.
        Assert.Equal(SellerEthereumPayout, persisted.SellerPayoutAddress);
        // K9 — the refund target on that network, canonical (06 §1.4).
        Assert.Equal(BuyerEthereumRefundCanonical, persisted.BuyerRefundAddress);
        // The deposit is derived on acceptance, after the commit.
        Assert.Equal([transaction.Id], _allocator.Allocations);
    }

    [Fact]
    public async Task Buyer_Chooses_Tron_On_A_Two_Network_Listing_Gets_The_Tron_Payout()
    {
        _networks = TestNetworkAvailability.All();
        var transaction = await CreateListingAsync(BlockchainNetwork.TRON, BlockchainNetwork.ETHEREUM);

        var outcome = await BuildSut().AcceptAsync(
            _buyer.Id, transaction.Id,
            new AcceptTransactionRequest(ValidWallet2, BuyerTradeUrl, Network: "TRON"),
            CancellationToken.None);

        Assert.Equal(AcceptTransactionStatus.Accepted, outcome.Status);
        var persisted = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == transaction.Id);
        Assert.Equal(BlockchainNetwork.TRON, persisted.Network);
        Assert.Equal(ValidWallet1, persisted.SellerPayoutAddress);
        Assert.Equal(ValidWallet2, persisted.BuyerRefundAddress);
    }

    [Fact]
    public async Task Omitted_Network_Reads_As_Tron_For_The_Pre_F8_Client()
    {
        var transaction = await CreateListingAsync(BlockchainNetwork.TRON);

        var outcome = await BuildSut().AcceptAsync(
            _buyer.Id, transaction.Id,
            new AcceptTransactionRequest(ValidWallet2, BuyerTradeUrl),
            CancellationToken.None);

        Assert.Equal(AcceptTransactionStatus.Accepted, outcome.Status);
        Assert.Equal("TRON", outcome.Body!.Network);
        var persisted = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == transaction.Id);
        Assert.Equal(BlockchainNetwork.TRON, persisted.Network);
        Assert.Equal(ValidWallet1, persisted.SellerPayoutAddress);
    }

    [Theory]
    [InlineData("ETHEREUM", new[] { BlockchainNetwork.TRON })]
    [InlineData(null, new[] { BlockchainNetwork.ETHEREUM })]
    public async Task A_Network_The_Listing_Does_Not_Accept_Is_Refused_And_Nothing_Is_Written(
        string? chosen, BlockchainNetwork[] listed)
    {
        _networks = TestNetworkAvailability.All();
        var transaction = await CreateListingAsync(listed);
        var refund = chosen == "ETHEREUM" ? BuyerEthereumRefundChecksummed : ValidWallet2;

        var outcome = await BuildSut().AcceptAsync(
            _buyer.Id, transaction.Id,
            new AcceptTransactionRequest(refund, BuyerTradeUrl, Network: chosen),
            CancellationToken.None);

        Assert.Equal(AcceptTransactionStatus.NetworkNotAccepted, outcome.Status);
        Assert.Equal(TransactionErrorCodes.NetworkNotAccepted, outcome.ErrorCode);
        Assert.Equal(chosen ?? "TRON", NetworkOf(outcome.ErrorDetails));
        await AssertUntouchedAsync(transaction.Id);
    }

    [Fact]
    public async Task A_Network_Without_A_Service_Fails_Closed_And_Nothing_Is_Written()
    {
        // The listing accepts Ethereum (created while it was open); Ethereum
        // then has no service / is frozen at acceptance time.
        var transaction = await CreateListingAsync(BlockchainNetwork.TRON, BlockchainNetwork.ETHEREUM);

        var outcome = await BuildSut().AcceptAsync(
            _buyer.Id, transaction.Id,
            new AcceptTransactionRequest(BuyerEthereumRefundChecksummed, BuyerTradeUrl, Network: "ETHEREUM"),
            CancellationToken.None);

        Assert.Equal(AcceptTransactionStatus.NetworkUnavailable, outcome.Status);
        Assert.Equal(TransactionErrorCodes.NetworkUnavailable, outcome.ErrorCode);
        Assert.Equal("ETHEREUM", NetworkOf(outcome.ErrorDetails));
        await AssertUntouchedAsync(transaction.Id);
        // Rejected before the Steam probes (no quota spent).
        Assert.Equal(0, _tradeHold.CallCount);
    }

    [Fact]
    public async Task A_Frozen_Network_Fails_Closed_And_The_Other_Network_Still_Accepts()
    {
        // T148 health freeze (11 §6.2 FK8-6): Ethereum's service is configured
        // but in a detected outage — closed for acceptance; TRON is not affected.
        _networks = TestNetworkAvailability.AllWithFrozen(BlockchainNetwork.ETHEREUM);
        var frozen = await CreateListingAsync(BlockchainNetwork.TRON, BlockchainNetwork.ETHEREUM);

        var refused = await BuildSut().AcceptAsync(
            _buyer.Id, frozen.Id,
            new AcceptTransactionRequest(BuyerEthereumRefundChecksummed, BuyerTradeUrl, Network: "ETHEREUM"),
            CancellationToken.None);

        Assert.Equal(AcceptTransactionStatus.NetworkUnavailable, refused.Status);
        Assert.Equal(TransactionErrorCodes.NetworkUnavailable, refused.ErrorCode);
        Assert.Equal("ETHEREUM", NetworkOf(refused.ErrorDetails));
        await AssertUntouchedAsync(frozen.Id);
        Assert.Equal(0, _tradeHold.CallCount);

        var accepted = await BuildSut().AcceptAsync(
            _buyer.Id, frozen.Id,
            new AcceptTransactionRequest(ValidWallet2, BuyerTradeUrl, Network: "TRON"),
            CancellationToken.None);

        Assert.Equal(AcceptTransactionStatus.Accepted, accepted.Status);
    }

    [Theory]
    [InlineData("ETHEREUM", ValidWallet2)]                          // TRON address on Ethereum
    [InlineData("TRON", BuyerEthereumRefundChecksummed)]            // Ethereum address on TRON
    [InlineData("ETHEREUM", "0xfB6916095ca1df60bB79Ce92cE3Ea74c37c5d358")] // broken EIP-55 checksum
    public async Task The_Refund_Address_Must_Be_Valid_On_The_Chosen_Network(string network, string refundAddress)
    {
        _networks = TestNetworkAvailability.All();
        var transaction = await CreateListingAsync(BlockchainNetwork.TRON, BlockchainNetwork.ETHEREUM);

        var outcome = await BuildSut().AcceptAsync(
            _buyer.Id, transaction.Id,
            new AcceptTransactionRequest(refundAddress, BuyerTradeUrl, Network: network),
            CancellationToken.None);

        Assert.Equal(AcceptTransactionStatus.InvalidWallet, outcome.Status);
        Assert.Equal(TransactionErrorCodes.InvalidWalletAddress, outcome.ErrorCode);
        Assert.Equal(network, NetworkOf(outcome.ErrorDetails));
        await AssertUntouchedAsync(transaction.Id);
    }

    // T156 verification (owner decision, SECURITY): the second confirmation for
    // a checksum-less Ethereum refund address is enforced by the server, not
    // only by the client UI.
    [Theory]
    [InlineData("0xfb6916095ca1df60bb79ce92ce3ea74c37c5d359")]   // all lowercase
    [InlineData("0xFB6916095CA1DF60BB79CE92CE3EA74C37C5D359")]   // all uppercase
    public async Task Checksumless_Ethereum_Refund_Without_Confirmation_Is_Refused_Before_Any_Write(string refundAddress)
    {
        _networks = TestNetworkAvailability.All();
        var transaction = await CreateListingAsync(BlockchainNetwork.TRON, BlockchainNetwork.ETHEREUM);

        var outcome = await BuildSut().AcceptAsync(
            _buyer.Id, transaction.Id,
            new AcceptTransactionRequest(refundAddress, BuyerTradeUrl, Network: "ETHEREUM"),
            CancellationToken.None);

        Assert.Equal(AcceptTransactionStatus.InvalidWallet, outcome.Status);
        Assert.Equal(TransactionErrorCodes.InvalidWalletAddress, outcome.ErrorCode);
        Assert.Equal("ETHEREUM", NetworkOf(outcome.ErrorDetails));
        Assert.Equal("CHECKSUM_CONFIRMATION_REQUIRED", DetailOf<string>(outcome.ErrorDetails, "reason"));
        Assert.True(DetailOf<bool>(outcome.ErrorDetails, "requiresConfirmation"));
        // No status change, no payout/refund snapshot, no event, no deposit.
        await AssertUntouchedAsync(transaction.Id);
    }

    [Fact]
    public async Task Checksumless_Ethereum_Refund_With_Confirmation_Is_Accepted_And_Stored_Canonical()
    {
        _networks = TestNetworkAvailability.All();
        var transaction = await CreateListingAsync(BlockchainNetwork.TRON, BlockchainNetwork.ETHEREUM);

        var outcome = await BuildSut().AcceptAsync(
            _buyer.Id, transaction.Id,
            new AcceptTransactionRequest(
                BuyerEthereumRefundCanonical, BuyerTradeUrl, Network: "ETHEREUM", ConfirmLowercase: true),
            CancellationToken.None);

        Assert.Equal(AcceptTransactionStatus.Accepted, outcome.Status);
        var persisted = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == transaction.Id);
        Assert.Equal(BuyerEthereumRefundCanonical, persisted.BuyerRefundAddress);
    }

    [Fact]
    public async Task Checksummed_Ethereum_Refund_Is_Accepted_Without_Confirmation()
    {
        _networks = TestNetworkAvailability.All();
        var transaction = await CreateListingAsync(BlockchainNetwork.TRON, BlockchainNetwork.ETHEREUM);

        var outcome = await BuildSut().AcceptAsync(
            _buyer.Id, transaction.Id,
            new AcceptTransactionRequest(BuyerEthereumRefundChecksummed, BuyerTradeUrl, Network: "ETHEREUM"),
            CancellationToken.None);

        Assert.Equal(AcceptTransactionStatus.Accepted, outcome.Status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ConfirmLowercase_Is_Ignored_On_Tron(bool? confirm)
    {
        _networks = TestNetworkAvailability.All();
        var transaction = await CreateListingAsync(BlockchainNetwork.TRON, BlockchainNetwork.ETHEREUM);

        var outcome = await BuildSut().AcceptAsync(
            _buyer.Id, transaction.Id,
            new AcceptTransactionRequest(ValidWallet2, BuyerTradeUrl, Network: "TRON", ConfirmLowercase: confirm),
            CancellationToken.None);

        Assert.Equal(AcceptTransactionStatus.Accepted, outcome.Status);
    }

    [Theory]
    [InlineData("BSC")]
    [InlineData("tron")]
    [InlineData("")]
    public async Task An_Unknown_Network_Name_Is_A_Validation_Error(string network)
    {
        var transaction = await CreateListingAsync(BlockchainNetwork.TRON);

        var outcome = await BuildSut().AcceptAsync(
            _buyer.Id, transaction.Id,
            new AcceptTransactionRequest(ValidWallet2, BuyerTradeUrl, Network: network),
            CancellationToken.None);

        Assert.Equal(AcceptTransactionStatus.ValidationFailed, outcome.Status);
        Assert.Equal(TransactionErrorCodes.ValidationError, outcome.ErrorCode);
        await AssertUntouchedAsync(transaction.Id);
    }

    [Fact]
    public async Task Refund_Cooldown_Is_Read_From_The_Chosen_Networks_Refund_Row()
    {
        // A fresh Ethereum refund address blocks an Ethereum acceptance only.
        _networks = TestNetworkAvailability.All();
        var row = UserWalletAddress.For(
            BlockchainNetwork.ETHEREUM, WalletAddressType.REFUND, BuyerEthereumRefundCanonical,
            changedAt: _clock.GetUtcNow().UtcDateTime.AddHours(-1));
        row.UserId = _buyer.Id;
        Context.Set<UserWalletAddress>().Add(row);
        await Context.SaveChangesAsync();
        var ethereum = await CreateListingAsync(BlockchainNetwork.ETHEREUM);

        var blocked = await BuildSut().AcceptAsync(
            _buyer.Id, ethereum.Id,
            new AcceptTransactionRequest(BuyerEthereumRefundChecksummed, BuyerTradeUrl, Network: "ETHEREUM"),
            CancellationToken.None);

        Assert.Equal(AcceptTransactionStatus.WalletCooldownActive, blocked.Status);
    }

    [Fact]
    public async Task Allocation_Failure_After_Commit_Does_Not_Undo_The_Acceptance()
    {
        // Best effort, like the creation-time call it replaces: the address is
        // only shown from SELLER_CONFIRMED on and EnsurePaymentAddressJob
        // recovers it.
        _allocator.DefaultStatus = Skinora.Transactions.Application.PaymentAddresses
            .PaymentAddressAllocationStatus.SidecarUnavailable;
        var transaction = await CreateListingAsync(BlockchainNetwork.TRON);

        var outcome = await BuildSut().AcceptAsync(
            _buyer.Id, transaction.Id,
            new AcceptTransactionRequest(ValidWallet2, BuyerTradeUrl),
            CancellationToken.None);

        Assert.Equal(AcceptTransactionStatus.Accepted, outcome.Status);
        Assert.Equal([transaction.Id], _allocator.Allocations);
    }

    // ─── T149: the network fee line (11 §F8 K8, 07 §7.6, 08 §3.1a) ─────────

    [Fact]
    public async Task Tron_Acceptance_Fixes_The_Network_Fee_Line_Into_The_Total()
    {
        _networkFees.With(BlockchainNetwork.TRON, 2.90m);
        var transaction = await CreateListingAsync(BlockchainNetwork.TRON);

        var outcome = await BuildSut().AcceptAsync(
            _buyer.Id, transaction.Id,
            new AcceptTransactionRequest(ValidWallet2, BuyerTradeUrl),
            CancellationToken.None);

        Assert.Equal(AcceptTransactionStatus.Accepted, outcome.Status);
        Assert.Equal("2.90", outcome.Body!.NetworkFeeAmount);
        Assert.Equal("104.90", outcome.Body.TotalAmount);
        var persisted = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == transaction.Id);
        Assert.Equal(2.90m, persisted.NetworkFeeAmount);
        Assert.Equal(104.90m, persisted.TotalAmount);
        // Price and commission are untouched — the line is a separate item.
        Assert.Equal(100m, persisted.Price);
        Assert.Equal(2m, persisted.CommissionAmount);
        Assert.Equal([BlockchainNetwork.TRON], _networkFees.Requests);
    }

    [Fact]
    public async Task Ethereum_Acceptance_Writes_A_Zero_Line()
    {
        _networks = TestNetworkAvailability.All();
        // Even if a quote for TRON exists, the Ethereum acceptance asks Ethereum's.
        _networkFees.With(BlockchainNetwork.TRON, 2.90m);
        var transaction = await CreateListingAsync(BlockchainNetwork.ETHEREUM);

        var outcome = await BuildSut().AcceptAsync(
            _buyer.Id, transaction.Id,
            new AcceptTransactionRequest(BuyerEthereumRefundChecksummed, BuyerTradeUrl, Network: "ETHEREUM"),
            CancellationToken.None);

        Assert.Equal(AcceptTransactionStatus.Accepted, outcome.Status);
        Assert.Equal("0.00", outcome.Body!.NetworkFeeAmount);
        Assert.Equal("102.00", outcome.Body.TotalAmount);
        var persisted = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == transaction.Id);
        Assert.Equal(0m, persisted.NetworkFeeAmount);
        Assert.Equal(102m, persisted.TotalAmount);
        Assert.Equal([BlockchainNetwork.ETHEREUM], _networkFees.Requests);
    }

    [Fact]
    public async Task Tron_Acceptance_Without_An_Estimate_Writes_The_Seeded_Fallback_And_Does_Not_Wait()
    {
        // The real quote service over the real seeded settings, with a TRON
        // service that cannot answer (none configured — fail closed): the
        // acceptance goes through with the 3.00 fallback line (08 §3.1a).
        _networkFeesOverride = new Skinora.Transactions.Application.GasFee.NetworkFeeQuoteService(
            new FakeBlockchainServiceRegistry(),
            new BlockchainNetworkSettingsProvider(Context),
            new Skinora.Transactions.Application.GasFee.SweepCostEstimateCache(_clock),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<
                Skinora.Transactions.Application.GasFee.NetworkFeeQuoteService>.Instance);
        var transaction = await CreateListingAsync(BlockchainNetwork.TRON);

        var outcome = await BuildSut().AcceptAsync(
            _buyer.Id, transaction.Id,
            new AcceptTransactionRequest(ValidWallet2, BuyerTradeUrl),
            CancellationToken.None);

        Assert.Equal(AcceptTransactionStatus.Accepted, outcome.Status);
        Assert.Equal("3.00", outcome.Body!.NetworkFeeAmount);
        var persisted = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == transaction.Id);
        Assert.Equal(3.00m, persisted.NetworkFeeAmount);
        Assert.Equal(105.00m, persisted.TotalAmount);
    }

    [Fact]
    public async Task Pre_T147_Deposit_Gets_The_Line_In_Its_Expected_Amount()
    {
        // A listing from before T147 already has a TRON deposit derived at
        // creation with ExpectedAmount = 102. Amount validation matches on that
        // column, so acceptance must carry the 2.90 line into it — or the
        // buyer's 104.90 would be judged an overpayment of exactly the line.
        _networkFees.With(BlockchainNetwork.TRON, 2.90m);
        var transaction = await CreateListingAsync(BlockchainNetwork.TRON);
        Context.Set<PaymentAddress>().Add(new PaymentAddress
        {
            Id = Guid.NewGuid(),
            TransactionId = transaction.Id,
            Network = BlockchainNetwork.TRON,
            Address = "TDepositPreT147000000000000000000",
            HdWalletIndex = 41,
            ExpectedAmount = 102m,
            ExpectedToken = StablecoinType.USDT,
            MonitoringStatus = MonitoringStatus.ACTIVE,
        });
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var outcome = await BuildSut().AcceptAsync(
            _buyer.Id, transaction.Id,
            new AcceptTransactionRequest(ValidWallet2, BuyerTradeUrl),
            CancellationToken.None);

        Assert.Equal(AcceptTransactionStatus.Accepted, outcome.Status);
        var deposit = await Context.Set<PaymentAddress>().AsNoTracking().SingleAsync(p => p.TransactionId == transaction.Id);
        Assert.Equal(104.90m, deposit.ExpectedAmount);
    }

    [Fact]
    public async Task Refused_Acceptance_Asks_No_Quote_And_Writes_No_Line()
    {
        _networkFees.With(BlockchainNetwork.TRON, 2.90m);
        var transaction = await CreateListingAsync(BlockchainNetwork.TRON);

        var outcome = await BuildSut(sanctions: new MatchingSanctionsCheck("OFAC")).AcceptAsync(
            _buyer.Id, transaction.Id,
            new AcceptTransactionRequest(ValidWallet2, BuyerTradeUrl),
            CancellationToken.None);

        Assert.Equal(AcceptTransactionStatus.SanctionsMatch, outcome.Status);
        Assert.Empty(_networkFees.Requests);
        var persisted = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == transaction.Id);
        Assert.Null(persisted.NetworkFeeAmount);
        Assert.Equal(102m, persisted.TotalAmount);
    }

    private async Task AssertUntouchedAsync(Guid transactionId)
    {
        var persisted = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == transactionId);
        Assert.Equal(TransactionStatus.CREATED, persisted.Status);
        Assert.Null(persisted.Network);
        Assert.Null(persisted.SellerPayoutAddress);
        Assert.Null(persisted.BuyerRefundAddress);
        Assert.Empty(_outbox.Published);
        Assert.Empty(_allocator.Allocations);
    }

    private static string? NetworkOf(object? details) =>
        details?.GetType().GetProperty("network")?.GetValue(details) as string;

    private static T? DetailOf<T>(object? details, string name) =>
        details?.GetType().GetProperty(name)?.GetValue(details) is T value ? value : default;

    private async Task<Transaction> CreateTransactionAsync(
        BuyerIdentificationMethod method,
        string? targetSteamId,
        TransactionStatus status = TransactionStatus.CREATED,
        BlockchainNetwork[]? networks = null)
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
            // T147 — the listing accepts TRON; the seller's payout address is
            // the option row's snapshot until acceptance copies it.
            SellerPayoutAddress = null,
            NetworkOptions = (networks ?? [BlockchainNetwork.TRON])
                .Select(n => new TransactionNetworkOption
                {
                    Id = Guid.NewGuid(),
                    Network = n,
                    SellerPayoutAddress = n == BlockchainNetwork.TRON ? ValidWallet1 : SellerEthereumPayout,
                    CreatedAt = nowUtc,
                })
                .ToList(),
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
        ITradeHoldChecker? tradeHold = null,
        ISteamTradeEligibilityChecker? steamEligibility = null) =>
        new(
            Context,
            new WalletAddressValidator(),
            sanctions ?? new NoMatchWalletSanctionsCheck(),
            flagChecker ?? new StubAccountFlagChecker(false),
            // T119a — the real U17 parser, not a stub: the accept endpoint's
            // trade-URL contract IS that parser's contract (07 §7.6 md.2).
            new TradeUrlParser(),
            tradeHold ?? _tradeHold,
            steamEligibility ?? new FakeSteamTradeEligibilityChecker(),
            _outbox,
            _networks,
            _allocator,
            _networkFeesOverride ?? _networkFees,
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
        public Task<WalletSanctionsDecision> EvaluateAsync(BlockchainNetwork network, string address, CancellationToken cancellationToken)
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
        public Task<bool> HasPendingAccountFlagAsync(Guid userId, FraudFlagType type, CancellationToken cancellationToken)
            => Task.FromResult(false);
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
