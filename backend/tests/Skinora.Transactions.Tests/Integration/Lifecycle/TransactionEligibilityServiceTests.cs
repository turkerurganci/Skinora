using Microsoft.Extensions.Time.Testing;
using Skinora.Shared.Persistence;
using Skinora.Shared.Tests.Integration;
using Skinora.Transactions.Application.Lifecycle;
using Skinora.Transactions.Tests.Helpers;
using Skinora.Transactions.Infrastructure.Persistence;
using Skinora.Users.Domain.Entities;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.Transactions.Tests.Integration.Lifecycle;

/// <summary>
/// Integration coverage for <see cref="TransactionEligibilityService"/> against
/// a real SQL Server instance. Each test pins one ineligibility reason at a
/// time so the contract emitted to <c>GET /transactions/eligibility</c>
/// (07 §7.3) is fully exercised.
/// </summary>
public class TransactionEligibilityServiceTests : IntegrationTestBase
{
    static TransactionEligibilityServiceTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        TransactionsModuleDbRegistration.RegisterTransactionsModule();
        Skinora.Platform.Infrastructure.Persistence.PlatformModuleDbRegistration.RegisterPlatformModule();
    }

    private User _seller = null!;
    private FakeTimeProvider _clock = null!;
    private FakeSteamTradeEligibilityChecker _steamEligibility = null!;

    protected override async Task SeedAsync(AppDbContext context)
    {
        _seller = new User
        {
            Id = Guid.NewGuid(),
            SteamId = "76561198000000050",
            SteamDisplayName = "Seller",
            DefaultPayoutAddress = "TXyzABCDEFGHJKLMNPQRSTUVWXYZ234567",
            MobileAuthenticatorVerified = true,
        };
        context.Set<User>().AddRange(_seller);
        await context.SaveChangesAsync();

        // Bootstrap the four runtime-configured limits used by the eligibility
        // surface. Tests that need to override one redo it after this seed.
        await context.ConfigureSettingAsync(TransactionLimitsProvider.MaxConcurrentKey, "5");
        await context.ConfigureSettingAsync(TransactionLimitsProvider.NewAccountLimitKey, "2");
        await context.ConfigureSettingAsync(TransactionLimitsProvider.NewAccountPeriodKey, "30");
        await context.ConfigureSettingAsync(TransactionLimitsProvider.PayoutCooldownKey, "24");

        _clock = new FakeTimeProvider(new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero));
        _steamEligibility = new FakeSteamTradeEligibilityChecker();
    }

    [Fact]
    public async Task Returns_Eligible_When_All_Preconditions_Pass()
    {
        var sut = BuildSut(flagsActive: false);

        var dto = await sut.GetAsync(_seller.Id, CancellationToken.None);

        Assert.True(dto.Eligible);
        Assert.Null(dto.Reasons);
        Assert.True(dto.MobileAuthenticatorActive);
        Assert.Equal(5, dto.ConcurrentLimit.Max);
        Assert.False(dto.CancelCooldown.Active);
    }

    [Fact]
    public async Task Returns_Mobile_Authenticator_Required_When_User_Not_Verified()
    {
        _seller.MobileAuthenticatorVerified = false;
        Context.Set<User>().Update(_seller);
        await Context.SaveChangesAsync();

        var sut = BuildSut(flagsActive: false);

        var dto = await sut.GetAsync(_seller.Id, CancellationToken.None);

        Assert.False(dto.Eligible);
        Assert.Contains(TransactionErrorCodes.MobileAuthenticatorRequired, dto.Reasons!);
    }

    [Fact]
    public async Task Returns_Account_Flagged_When_Flag_Checker_True()
    {
        var sut = BuildSut(flagsActive: true);

        var dto = await sut.GetAsync(_seller.Id, CancellationToken.None);

        Assert.False(dto.Eligible);
        Assert.Contains(TransactionErrorCodes.AccountFlagged, dto.Reasons!);
    }

    [Fact]
    public async Task Returns_Cancel_Cooldown_Active_When_User_Stamped()
    {
        _seller.CooldownExpiresAt = _clock.GetUtcNow().UtcDateTime.AddHours(2);
        Context.Set<User>().Update(_seller);
        await Context.SaveChangesAsync();

        var sut = BuildSut(flagsActive: false);

        var dto = await sut.GetAsync(_seller.Id, CancellationToken.None);

        Assert.False(dto.Eligible);
        Assert.Contains(TransactionErrorCodes.CancelCooldownActive, dto.Reasons!);
        Assert.True(dto.CancelCooldown.Active);
        Assert.NotNull(dto.CancelCooldown.ExpiresAt);
    }

    [Fact]
    public async Task Returns_Payout_Address_Cooldown_Active_When_Recent_Change()
    {
        _seller.PayoutAddressChangedAt = _clock.GetUtcNow().UtcDateTime.AddHours(-2);
        Context.Set<User>().Update(_seller);
        await Context.SaveChangesAsync();

        var sut = BuildSut(flagsActive: false);

        var dto = await sut.GetAsync(_seller.Id, CancellationToken.None);

        Assert.False(dto.Eligible);
        Assert.Contains(TransactionErrorCodes.PayoutAddressCooldownActive, dto.Reasons!);
    }

    [Fact]
    public async Task Returns_Seller_Wallet_Address_Missing_When_DefaultPayout_Null()
    {
        _seller.DefaultPayoutAddress = null;
        Context.Set<User>().Update(_seller);
        await Context.SaveChangesAsync();

        var sut = BuildSut(flagsActive: false);

        var dto = await sut.GetAsync(_seller.Id, CancellationToken.None);

        Assert.False(dto.Eligible);
        Assert.Contains(TransactionErrorCodes.SellerWalletAddressMissing, dto.Reasons!);
    }

    // ---- 08 §2.2a — the seller's own Steam trade eligibility ----
    //
    // Seller-side twins of the buyer gates already pinned in
    // TransactionAcceptanceServiceTests / TransactionReadinessServiceTests.
    // Until these existed the switch in TransactionEligibilityService was
    // unreachable from any test: the fake defaults to Eligible and no test
    // overrode it, so all three arms could be deleted with the suite green.

    [Fact]
    public async Task Returns_Steam_Account_Limited_When_Steam_Forbids_Trading()
    {
        var sut = BuildSut(flagsActive: false, FakeSteamTradeEligibilityChecker.Limited());

        var dto = await sut.GetAsync(_seller.Id, CancellationToken.None);

        Assert.False(dto.Eligible);
        Assert.Contains(TransactionErrorCodes.SteamAccountLimited, dto.Reasons!);
        // No day count travels with this reason: the restriction lifts by
        // spending, not by waiting, so a number here would name a deadline
        // Steam never gave.
        Assert.Null(dto.SteamAccountRemainingDays);
    }

    [Fact]
    public async Task Returns_Steam_Account_Too_New_With_The_Remaining_Day_Count()
    {
        var sut = BuildSut(flagsActive: false, FakeSteamTradeEligibilityChecker.TooNew(remainingDays: 4));

        var dto = await sut.GetAsync(_seller.Id, CancellationToken.None);

        Assert.False(dto.Eligible);
        Assert.Contains(TransactionErrorCodes.SteamAccountTooNew, dto.Reasons!);
        Assert.Equal(4, dto.SteamAccountRemainingDays);
    }

    [Fact]
    public async Task Returns_Steam_Unavailable_When_Steam_Could_Not_Be_Asked()
    {
        var sut = BuildSut(flagsActive: false, FakeSteamTradeEligibilityChecker.Unknown());

        var dto = await sut.GetAsync(_seller.Id, CancellationToken.None);

        // Fail-closed: "could not ask" blocks, and it blocks under its own
        // transient code so the create path can answer 503 instead of a
        // permanent-looking rejection.
        Assert.False(dto.Eligible);
        Assert.Contains(TransactionErrorCodes.SteamUnavailable, dto.Reasons!);
        Assert.Null(dto.SteamAccountRemainingDays);
    }

    [Fact]
    public async Task Eligible_Seller_Carries_No_Steam_Reason_And_No_Day_Count()
    {
        var steam = new FakeSteamTradeEligibilityChecker();
        var sut = BuildSut(flagsActive: false, steam);

        var dto = await sut.GetAsync(_seller.Id, CancellationToken.None);

        Assert.True(dto.Eligible);
        Assert.Null(dto.SteamAccountRemainingDays);
        // The probe ran, and it ran for this seller — an eligible answer must
        // mean "Steam was asked about them", not "nobody asked".
        Assert.Equal(1, steam.CallCount);
        Assert.Equal(_seller.Id, steam.LastUserId);
    }

    [Fact]
    public async Task Steam_Is_Asked_Even_When_A_Cheaper_Rule_Already_Blocks()
    {
        // Deliberate, and the opposite of the buyer accept gate: this endpoint's
        // contract is the COMPLETE reason list, so a seller blocked by two
        // things is told both at once instead of clearing one and meeting the
        // next. Reordering would not save the Steam quota either — the probe
        // caches a clean result for 24h and never caches a restricted one.
        var steam = FakeSteamTradeEligibilityChecker.Limited();
        var sut = BuildSut(flagsActive: true, steam);

        var dto = await sut.GetAsync(_seller.Id, CancellationToken.None);

        Assert.False(dto.Eligible);
        Assert.Contains(TransactionErrorCodes.AccountFlagged, dto.Reasons!);
        Assert.Contains(TransactionErrorCodes.SteamAccountLimited, dto.Reasons!);
        Assert.Equal(1, steam.CallCount);
    }

    private TransactionEligibilityService BuildSut(
        bool flagsActive,
        FakeSteamTradeEligibilityChecker? steamEligibility = null)
    {
        var limits = new TransactionLimitsProvider(Context);
        return new TransactionEligibilityService(
            Context,
            limits,
            new StubFlagChecker(flagsActive),
            steamEligibility ?? _steamEligibility,
            _clock);
    }

    private sealed class StubFlagChecker : IAccountFlagChecker
    {
        private readonly bool _result;
        public StubFlagChecker(bool result) => _result = result;
        public Task<bool> HasActiveAccountFlagAsync(Guid userId, CancellationToken cancellationToken)
            => Task.FromResult(_result);
    }
}
