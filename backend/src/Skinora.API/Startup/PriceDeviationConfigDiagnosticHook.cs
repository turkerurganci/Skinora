using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Skinora.Shared.SteamMarket;
using Skinora.Platform.Domain.Entities;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Application.Lifecycle;

namespace Skinora.API.Startup;

/// <summary>
/// WP1 (T81) — reports at startup whether the PRICE_DEVIATION fraud rule can
/// actually fire, and warns loudly when it cannot.
/// </summary>
/// <remarks>
/// <para>
/// The rule needs two independent pieces of configuration and <b>both</b>
/// defaults leave it inert: <c>SteamMarket:Provider</c> defaults to
/// <c>logging</c>, whose <c>LoggingSteamMarketPriceClient</c> returns no price
/// (fail-open per 08 §7.4 step 3b), and the seeded
/// <c>price_deviation_threshold</c> is <c>1.0</c> — a 100% deviation that a
/// real listing practically never reaches. Neither produces an error; the rule
/// simply never flags anything.
/// </para>
/// <para>
/// Runs after <see cref="SettingsBootstrapHook"/> so the threshold read here is
/// the post-hydration value an operator's <c>SKINORA_SETTING_*</c> env var may
/// have just written. Purely diagnostic — it never throws, because a
/// deliberately fail-open fraud rule is a valid deployment (DEPLOY_RUNBOOK
/// §C.1) and must not block the host.
/// </para>
/// <para>
/// This is the <c>ForwardedHeadersNotRegistered</c> lesson applied to fraud
/// config: a control that is silently ineffective in the documented default
/// posture has to say so at boot, or the only way to discover it is to notice
/// that nothing was ever flagged.
/// </para>
/// </remarks>
public sealed class PriceDeviationConfigDiagnosticHook : IHostedService
{
    /// <summary>
    /// Deviation ratio at or above which the threshold cannot realistically be
    /// crossed (1.0 = 100%). The seeded default sits exactly here.
    /// </summary>
    private const decimal InertThresholdRatio = 1.0m;

    /// <summary>
    /// The rule's live-fire predicate, split out from the logging so it can be
    /// asserted directly. Both halves must hold: a real price source to compare
    /// against, and a threshold a real listing can actually cross.
    /// </summary>
    public static bool CanRuleFire(string? provider, decimal? threshold) =>
        IsLivePriceSource(provider) && threshold is > 0m and < InertThresholdRatio;

    public static bool IsLivePriceSource(string? provider) => string.Equals(
        provider ?? SteamMarketSettings.ProviderLogging,
        SteamMarketSettings.ProviderSteamMarket,
        StringComparison.OrdinalIgnoreCase);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PriceDeviationConfigDiagnosticHook> _logger;

    public PriceDeviationConfigDiagnosticHook(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<PriceDeviationConfigDiagnosticHook> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var provider = _configuration[
                           $"{SteamMarketSettings.SectionName}:{nameof(SteamMarketSettings.Provider)}"]
                       ?? SteamMarketSettings.ProviderLogging;

        var priceSourceLive = IsLivePriceSource(provider);

        decimal? threshold;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var raw = await db.Set<SystemSetting>()
                .AsNoTracking()
                .Where(s => s.Key == FraudPreCheckService.DeviationThresholdKey)
                .Select(s => s.Value)
                .FirstOrDefaultAsync(cancellationToken);

            threshold = decimal.TryParse(
                raw,
                System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture,
                out var parsed)
                ? parsed
                : null;
        }
        catch (Exception ex)
        {
            // Diagnostics must never be the reason a host fails to start.
            _logger.LogWarning(
                ex,
                "PRICE_DEVIATION config diagnostic could not read {Key} — reporting the price source only.",
                FraudPreCheckService.DeviationThresholdKey);
            threshold = null;
        }

        var thresholdCanFire = threshold is > 0m and < InertThresholdRatio;

        if (CanRuleFire(provider, threshold))
        {
            _logger.LogInformation(
                "PRICE_DEVIATION rule ACTIVE — SteamMarket:Provider={Provider}, {Key}={Threshold} "
                + "({Percent}% deviation flags a transaction).",
                provider,
                FraudPreCheckService.DeviationThresholdKey,
                threshold,
                threshold * 100m);
            return;
        }

        _logger.LogWarning(
            "PRICE_DEVIATION rule INEFFECTIVE — it will never flag a transaction. "
            + "Price source: {PriceSource} (SteamMarket:Provider={Provider}). "
            + "Threshold: {ThresholdState} ({Key}={Threshold}). "
            + "Fix per DEPLOY_RUNBOOK §C.1: set SteamMarket__Provider=steam-market and "
            + "narrow that threshold to a ratio in (0, 1).",
            priceSourceLive ? "live" : "NO PRICE (logging stub, fail-open)",
            provider,
            thresholdCanFire ? "usable" : "UNREACHABLE",
            FraudPreCheckService.DeviationThresholdKey,
            threshold?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "<unreadable>");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
