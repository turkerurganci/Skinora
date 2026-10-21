using Skinora.API.Startup;
using Skinora.Platform.Infrastructure.Persistence;
using static Skinora.API.Startup.AgeGateConfigDiagnosticHook;

namespace Skinora.API.Tests.Unit.Startup;

/// <summary>
/// T166 — <c>auth.min_steam_account_age_days</c> is 30 in production
/// (02 §21.1). The repo seed already writes 30; these pin the boot-time
/// verdict that catches a database lowered by hand (the rehearsal's 1).
/// </summary>
public class AgeGateConfigDiagnosticHookTests
{
    [Theory]
    [InlineData("30")]
    [InlineData("45")]
    public void AtOrAboveProductionValue_IsProduction(string raw)
        => Assert.Equal(Posture.Production, Evaluate(raw));

    [Theory]
    [InlineData("1")]  // the local rehearsal value since 2026-08-29
    [InlineData("29")]
    public void BelowProductionValue_IsFlagged(string raw)
        => Assert.Equal(Posture.BelowProduction, Evaluate(raw));

    [Theory]
    [InlineData("0")]
    [InlineData("-3")]
    public void ZeroOrNegative_IsDisabled(string raw)
        => Assert.Equal(Posture.Disabled, Evaluate(raw));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("thirty")]
    public void MissingOrGarbage_IsUnreadable(string? raw)
        => Assert.Equal(Posture.Unreadable, Evaluate(raw));

    [Fact]
    public void RepoSeed_IsTheProductionValue()
    {
        // The production value lives in the seed (and the T30 migration that
        // inserted it); the diagnostic's constant must not drift from it.
        var row = SystemSettingSeed.All.Single(r => r.Key == "auth.min_steam_account_age_days");
        Assert.Equal(ProductionMinimumDays.ToString(), row.Value);
        Assert.Equal(Posture.Production, Evaluate(row.Value));
    }
}
