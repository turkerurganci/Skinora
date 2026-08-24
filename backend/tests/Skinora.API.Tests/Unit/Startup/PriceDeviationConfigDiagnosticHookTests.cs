using Skinora.API.Startup;
using Skinora.Shared.SteamMarket;

namespace Skinora.API.Tests.Unit.Startup;

/// <summary>
/// WP1 (T81) — the PRICE_DEVIATION rule needs a live price source AND a
/// crossable threshold. Both ship inert by default, and neither default
/// produces an error, so the boot-time verdict is the only signal an operator
/// gets. These pin the verdict itself; the hook only formats it into a log line.
/// </summary>
public class PriceDeviationConfigDiagnosticHookTests
{
    [Theory]
    // The two shipped defaults, together and separately — every one of these is
    // a deployment where the rule silently never flags anything.
    [InlineData(SteamMarketSettings.ProviderLogging, 1.0)]
    [InlineData(SteamMarketSettings.ProviderLogging, 0.3)]
    [InlineData(SteamMarketSettings.ProviderSteamMarket, 1.0)]
    // Above 1.0 is even further out of reach than the seeded default.
    [InlineData(SteamMarketSettings.ProviderSteamMarket, 2.5)]
    // A zero/negative threshold is not "disabled" here — it is unusable.
    [InlineData(SteamMarketSettings.ProviderSteamMarket, 0.0)]
    [InlineData(SteamMarketSettings.ProviderSteamMarket, -0.5)]
    public void Inert_Configurations_Report_The_Rule_As_Unable_To_Fire(string provider, double threshold)
        => Assert.False(PriceDeviationConfigDiagnosticHook.CanRuleFire(provider, (decimal)threshold));

    [Theory]
    [InlineData(0.3)]
    [InlineData(0.99)]
    [InlineData(0.01)]
    public void Live_Source_With_Crossable_Threshold_Reports_The_Rule_As_Active(double threshold)
        => Assert.True(PriceDeviationConfigDiagnosticHook.CanRuleFire(
            SteamMarketSettings.ProviderSteamMarket, (decimal)threshold));

    [Fact]
    public void Unreadable_Threshold_Reports_The_Rule_As_Unable_To_Fire()
        => Assert.False(PriceDeviationConfigDiagnosticHook.CanRuleFire(
            SteamMarketSettings.ProviderSteamMarket, null));

    [Fact]
    public void Absent_Provider_Falls_Back_To_The_Logging_Stub()
    {
        // Program.cs applies the same null → "logging" fallback when wiring
        // ISteamMarketPriceClient; if the two ever drift, the diagnostic would
        // report on a provider the app is not actually using.
        Assert.False(PriceDeviationConfigDiagnosticHook.IsLivePriceSource(null));
        Assert.False(PriceDeviationConfigDiagnosticHook.CanRuleFire(null, 0.3m));
    }

    [Fact]
    public void Provider_Match_Is_Case_Insensitive()
        => Assert.True(PriceDeviationConfigDiagnosticHook.IsLivePriceSource("STEAM-Market"));
}
