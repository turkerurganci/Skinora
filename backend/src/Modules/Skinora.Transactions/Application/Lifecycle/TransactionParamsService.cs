using System.Globalization;

namespace Skinora.Transactions.Application.Lifecycle;

/// <summary>
/// Reader for the form parameters returned by <c>GET /transactions/params</c>
/// (07 §7.4). Delegates to <see cref="ITransactionLimitsProvider"/> for the
/// raw SystemSetting values and only handles presentation: price → 2-decimal
/// invariant string, minutes → integer hours.
/// </summary>
/// <remarks>
/// <para>
/// Storage uses minutes for every timeout setting (see <c>SystemSettingSeed</c>),
/// while 07 §7.4 contracts the form on hours. Conversion happens here using
/// integer division so the response stays a plain JSON number; admins who
/// need sub-hour precision configure the timeout via the per-step scanner job
/// rather than the form.
/// </para>
/// <para>
/// Documented defaults (02 §5, §16.2) act as a fail-safe when a row is
/// missing or NULL — the values match the seed comments in
/// <c>SystemSettingSeed.cs</c>. In a properly bootstrapped environment the
/// fallback is never hit because partial seeds fail the
/// <c>SettingsBootstrapService</c> startup gate (06 §8.9).
/// </para>
/// </remarks>
public sealed class TransactionParamsService : ITransactionParamsService
{
    public const decimal DefaultCommissionRate = 0.02m;
    public const decimal DefaultMinPrice = 10m;
    public const decimal DefaultMaxPrice = 50000m;
    public const int DefaultPaymentTimeoutMinHours = 6;
    public const int DefaultPaymentTimeoutMaxHours = 72;
    public const int DefaultPaymentTimeoutDefaultHours = 24;

    private static readonly string[] _supportedStablecoins = ["USDT", "USDC"];

    private readonly ITransactionLimitsProvider _limitsProvider;

    public TransactionParamsService(ITransactionLimitsProvider limitsProvider)
    {
        _limitsProvider = limitsProvider;
    }

    public async Task<TransactionParamsDto> GetAsync(CancellationToken cancellationToken)
    {
        var limits = await _limitsProvider.GetAsync(cancellationToken);

        var minPrice = limits.MinTransactionAmount ?? DefaultMinPrice;
        var maxPrice = limits.MaxTransactionAmount ?? DefaultMaxPrice;
        var commission = limits.CommissionRate ?? DefaultCommissionRate;

        var minHours = (limits.PaymentTimeoutMinMinutes ?? DefaultPaymentTimeoutMinHours * 60) / 60;
        var maxHours = (limits.PaymentTimeoutMaxMinutes ?? DefaultPaymentTimeoutMaxHours * 60) / 60;
        var defaultHours = (limits.PaymentTimeoutDefaultMinutes ?? DefaultPaymentTimeoutDefaultHours * 60) / 60;

        return new TransactionParamsDto(
            MinPrice: minPrice.ToString("0.00", CultureInfo.InvariantCulture),
            MaxPrice: maxPrice.ToString("0.00", CultureInfo.InvariantCulture),
            CommissionRate: commission,
            PaymentTimeout: new PaymentTimeoutWindowDto(
                MinHours: minHours,
                MaxHours: maxHours,
                DefaultHours: defaultHours),
            OpenLinkEnabled: limits.OpenLinkEnabled,
            SupportedStablecoins: _supportedStablecoins);
    }
}
