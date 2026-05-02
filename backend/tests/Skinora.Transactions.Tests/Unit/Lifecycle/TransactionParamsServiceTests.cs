using Skinora.Transactions.Application.Lifecycle;

namespace Skinora.Transactions.Tests.Unit.Lifecycle;

/// <summary>
/// Unit coverage for <see cref="TransactionParamsService"/> using a stub
/// <see cref="ITransactionLimitsProvider"/>. Verifies storage minutes are
/// converted to display hours via integer division and that documented
/// defaults kick in for unconfigured settings (07 §7.4).
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
    public async Task Converts_Minutes_To_Hours_Via_Integer_Division()
    {
        // 90 minutes → 1 hour (integer divide rounds toward zero, matches
        // the ParamsService doc: admins set whole-hour values).
        var provider = new StubLimitsProvider(new TransactionLimits(
            null, null, null, null, null,
            PaymentTimeoutMinMinutes: 90,
            PaymentTimeoutMaxMinutes: 119,
            PaymentTimeoutDefaultMinutes: 60,
            null, null, null, false));
        var sut = new TransactionParamsService(provider);

        var dto = await sut.GetAsync(CancellationToken.None);

        Assert.Equal(1, dto.PaymentTimeout.MinHours);
        Assert.Equal(1, dto.PaymentTimeout.MaxHours);
        Assert.Equal(1, dto.PaymentTimeout.DefaultHours);
    }

    private sealed class StubLimitsProvider : ITransactionLimitsProvider
    {
        private readonly TransactionLimits _limits;
        public StubLimitsProvider(TransactionLimits limits) => _limits = limits;
        public Task<TransactionLimits> GetAsync(CancellationToken cancellationToken)
            => Task.FromResult(_limits);
    }
}
