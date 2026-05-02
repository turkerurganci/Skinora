using Microsoft.Extensions.Time.Testing;
using Skinora.Shared.Persistence;
using Skinora.Shared.Tests.Integration;
using Skinora.Transactions.Application.Lifecycle;
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

    private TransactionEligibilityService BuildSut(bool flagsActive)
    {
        var limits = new TransactionLimitsProvider(Context);
        return new TransactionEligibilityService(
            Context,
            limits,
            new StubFlagChecker(flagsActive),
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
