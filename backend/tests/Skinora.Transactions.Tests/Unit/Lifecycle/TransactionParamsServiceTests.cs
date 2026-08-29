using Skinora.Transactions.Application.Lifecycle;

namespace Skinora.Transactions.Tests.Unit.Lifecycle;

/// <summary>
/// Unit coverage for <see cref="TransactionParamsService"/> using a stub
/// <see cref="ITransactionLimitsProvider"/>. Verifies storage minutes are
/// projected onto whole display hours without ever offering an hour the
/// creation endpoint would reject, and that documented defaults kick in for
/// unconfigured settings (07 §7.4).
/// </summary>
public class TransactionParamsServiceTests
{
    [Fact]
    public async Task Returns_Configured_Values_When_All_Settings_Bootstrapped()
    {
        var provider = new StubLimitsProvider(new TransactionLimits(
            MaxConcurrent: 5,
            NewAccountTransactionLimit: 2,
            NewAccountPeriodDays: 14,
            PayoutAddressCooldownHours: 24,
            AcceptTimeoutMinutes: 60,
            PaymentTimeoutMinMinutes: 6 * 60,
            PaymentTimeoutMaxMinutes: 72 * 60,
            PaymentTimeoutDefaultMinutes: 24 * 60,
            CommissionRate: 0.025m,
            MinTransactionAmount: 12m,
            MaxTransactionAmount: 60000m,
            OpenLinkEnabled: true));
        var sut = new TransactionParamsService(provider);

        var dto = await sut.GetAsync(CancellationToken.None);

        Assert.Equal("12.00", dto.MinPrice);
        Assert.Equal("60000.00", dto.MaxPrice);
        Assert.Equal(0.025m, dto.CommissionRate);
        Assert.Equal(6, dto.PaymentTimeout.MinHours);
        Assert.Equal(72, dto.PaymentTimeout.MaxHours);
        Assert.Equal(24, dto.PaymentTimeout.DefaultHours);
        Assert.True(dto.OpenLinkEnabled);
        Assert.Equal(["USDT", "USDC"], dto.SupportedStablecoins);
    }

    [Fact]
    public async Task Falls_Back_To_Documented_Defaults_When_Settings_Missing()
    {
        var provider = new StubLimitsProvider(new TransactionLimits(
            MaxConcurrent: null,
            NewAccountTransactionLimit: null,
            NewAccountPeriodDays: null,
            PayoutAddressCooldownHours: null,
            AcceptTimeoutMinutes: null,
            PaymentTimeoutMinMinutes: null,
            PaymentTimeoutMaxMinutes: null,
            PaymentTimeoutDefaultMinutes: null,
            CommissionRate: null,
            MinTransactionAmount: null,
            MaxTransactionAmount: null,
            OpenLinkEnabled: false));
        var sut = new TransactionParamsService(provider);

        var dto = await sut.GetAsync(CancellationToken.None);

        Assert.Equal("10.00", dto.MinPrice);
        Assert.Equal("50000.00", dto.MaxPrice);
        Assert.Equal(0.02m, dto.CommissionRate);
        Assert.Equal(6, dto.PaymentTimeout.MinHours);
        Assert.Equal(72, dto.PaymentTimeout.MaxHours);
        Assert.Equal(24, dto.PaymentTimeout.DefaultHours);
        Assert.False(dto.OpenLinkEnabled);
    }

    [Fact]
    public async Task Rounds_Sub_Hour_Window_Inward_Instead_Of_Truncating_To_Zero()
    {
        // The seeded values (SystemSettingSeed rows 3-5). Truncating toward
        // zero published min 0 / default 0 / max 1, so the wizard pre-selected
        // "0 hours" and treated it as valid while TransactionCreationService
        // rejected the submission with TimeoutOutOfRange (0 < 15).
        var provider = new StubLimitsProvider(new TransactionLimits(
            null, null, null, null, null,
            PaymentTimeoutMinMinutes: 15,
            PaymentTimeoutMaxMinutes: 60,
            PaymentTimeoutDefaultMinutes: 30,
            null, null, null, false));
        var sut = new TransactionParamsService(provider);

        var dto = await sut.GetAsync(CancellationToken.None);

        Assert.Equal(1, dto.PaymentTimeout.MinHours);
        Assert.Equal(1, dto.PaymentTimeout.MaxHours);
        Assert.Equal(1, dto.PaymentTimeout.DefaultHours);
    }

    [Theory]
    // Every hour the form offers must satisfy min <= hour*60 <= max, which is
    // the exact predicate TransactionCreationService re-checks on submit.
    [InlineData(15, 60, 30)]
    [InlineData(6 * 60, 72 * 60, 24 * 60)]
    [InlineData(45, 200, 90)]
    [InlineData(60, 60, 60)]
    public async Task Every_Offered_Hour_Is_Accepted_By_The_Creation_Endpoint(
        int minMinutes,
        int maxMinutes,
        int defaultMinutes)
    {
        var provider = new StubLimitsProvider(new TransactionLimits(
            null, null, null, null, null,
            PaymentTimeoutMinMinutes: minMinutes,
            PaymentTimeoutMaxMinutes: maxMinutes,
            PaymentTimeoutDefaultMinutes: defaultMinutes,
            null, null, null, false));
        var sut = new TransactionParamsService(provider);

        var dto = await sut.GetAsync(CancellationToken.None);
        var window = dto.PaymentTimeout;

        Assert.True(window.MinHours <= window.MaxHours, "window must not be empty");
        Assert.InRange(window.DefaultHours, window.MinHours, window.MaxHours);

        // Step2Details.tsx builds its dropdown as [minHours..maxHours].
        for (var hour = window.MinHours; hour <= window.MaxHours; hour++)
        {
            Assert.InRange(hour * 60, minMinutes, maxMinutes);
        }
    }

    private sealed class StubLimitsProvider : ITransactionLimitsProvider
    {
        private readonly TransactionLimits _limits;
        public StubLimitsProvider(TransactionLimits limits) => _limits = limits;
        public Task<TransactionLimits> GetAsync(CancellationToken cancellationToken)
            => Task.FromResult(_limits);
    }
}
