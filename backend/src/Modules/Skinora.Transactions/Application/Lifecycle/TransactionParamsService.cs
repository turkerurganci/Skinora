using System.Globalization;
using Skinora.Transactions.Application.Networks;

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
/// while 07 §7.4 contracts the form on hours. Conversion happens in
/// <see cref="ToHourWindow"/>, which rounds the window inward so the response
/// stays a plain JSON number and every hour it offers is one the creation
/// endpoint accepts; admins who need sub-hour precision configure the timeout
/// via the per-step scanner job rather than the form.
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

    // 11 §F8 K2 (T166) — USDT only. USDC stays a known token for the
    // wrong-token refund path (AmountValidationService) and for existing
    // records, but is no longer offered for a new listing.
    private static readonly string[] _supportedStablecoins = ["USDT"];

    private readonly ITransactionLimitsProvider _limitsProvider;
    private readonly IBlockchainNetworkAvailability _networks;

    public TransactionParamsService(
        ITransactionLimitsProvider limitsProvider,
        IBlockchainNetworkAvailability networks)
    {
        _limitsProvider = limitsProvider;
        _networks = networks;
    }

    public async Task<TransactionParamsDto> GetAsync(CancellationToken cancellationToken)
    {
        var limits = await _limitsProvider.GetAsync(cancellationToken);

        var minPrice = limits.MinTransactionAmount ?? DefaultMinPrice;
        var maxPrice = limits.MaxTransactionAmount ?? DefaultMaxPrice;
        var commission = limits.CommissionRate ?? DefaultCommissionRate;

        var (minHours, maxHours, defaultHours) = ToHourWindow(
            limits.PaymentTimeoutMinMinutes ?? DefaultPaymentTimeoutMinHours * 60,
            limits.PaymentTimeoutMaxMinutes ?? DefaultPaymentTimeoutMaxHours * 60,
            limits.PaymentTimeoutDefaultMinutes ?? DefaultPaymentTimeoutDefaultHours * 60);

        return new TransactionParamsDto(
            MinPrice: minPrice.ToString("0.00", CultureInfo.InvariantCulture),
            MaxPrice: maxPrice.ToString("0.00", CultureInfo.InvariantCulture),
            CommissionRate: commission,
            PaymentTimeout: new PaymentTimeoutWindowDto(
                MinHours: minHours,
                MaxHours: maxHours,
                DefaultHours: defaultHours),
            OpenLinkEnabled: limits.OpenLinkEnabled,
            SupportedStablecoins: _supportedStablecoins,
            // T148 (07 §1.3, §7.4) — networks open for a new transaction right
            // now: a configured service that is not in a health freeze. The
            // buyer's choice among them arrives with T147.
            SupportedNetworks: _networks.OpenForNewTransactions()
                .Select(n => n.ToString())
                .ToList());
    }

    /// <summary>
    /// Project a minute-based timeout window onto the whole-hour window the
    /// form speaks (07 §7.4), keeping every hour it offers acceptable to
    /// <c>TransactionCreationService</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Plain integer division truncated toward zero on all three values, which
    /// silently produced a window the creation endpoint rejects: the seeded
    /// 15/30/60-minute settings became <c>min 0 / default 0 / max 1</c>, so the
    /// wizard pre-selected "0 hours", treated it as valid, and the request
    /// failed with <c>TimeoutOutOfRange</c> (0 &lt; 15) only after submission.
    /// Rounding inward keeps every offered hour inside the stored minutes: the
    /// minimum rounds up, the maximum rounds down, and the default is clamped
    /// between them.
    /// </para>
    /// <para>
    /// Rounding inward can only empty the window when no whole hour lies inside
    /// it at all, which <c>SystemSettingsValidator.ValidateCrossKey</c> rejects
    /// at configuration time. The <c>Math.Max</c> below is the fail-safe for the
    /// documented fallbacks and any row that predates that rule: it prefers the
    /// minimum, since a window shorter than the configured floor is the half of
    /// the contract a seller can still work around by paying sooner.
    /// </para>
    /// </remarks>
    internal static (int MinHours, int MaxHours, int DefaultHours) ToHourWindow(
        int minMinutes,
        int maxMinutes,
        int defaultMinutes)
    {
        var minHours = Math.Max(1, CeilToHours(minMinutes));
        var maxHours = Math.Max(minHours, maxMinutes / 60);
        var defaultHours = Math.Clamp(CeilToHours(defaultMinutes), minHours, maxHours);

        return (minHours, maxHours, defaultHours);

        static int CeilToHours(int minutes) => (minutes + 59) / 60;
    }
}
