using Skinora.Shared.Steam;
using Skinora.Users.Application.Settings;

namespace Skinora.Users.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="SidecarTradeHoldChecker"/> (WP6) — the U17
/// trade-URL save mapping from <see cref="ISteamTradeHoldProbe"/> onto
/// <see cref="TradeHoldResult"/> (07 §5.16a).
/// </summary>
public sealed class SidecarTradeHoldCheckerTests
{
    private const string SteamId = "76561198000000001";
    private const string Token = "abc123";

    [Fact]
    public async Task CheckAsync_Maps_Active_Probe_To_Active_Without_SetupUrl()
    {
        var sut = new SidecarTradeHoldChecker(new FakeProbe(SteamTradeHoldProbeResult.Active));

        var result = await sut.CheckAsync(SteamId, Token, CancellationToken.None);

        Assert.True(result.Available);
        Assert.True(result.Active);
        Assert.Null(result.SetupGuideUrl);
    }

    [Fact]
    public async Task CheckAsync_Maps_Inactive_Probe_To_Inactive_With_SetupUrl()
    {
        var sut = new SidecarTradeHoldChecker(new FakeProbe(SteamTradeHoldProbeResult.Inactive));

        var result = await sut.CheckAsync(SteamId, Token, CancellationToken.None);

        Assert.True(result.Available);
        Assert.False(result.Active);
        Assert.Equal(StubTradeHoldChecker.DefaultSetupGuideUrl, result.SetupGuideUrl);
    }

    [Fact]
    public async Task CheckAsync_Maps_Unavailable_Probe_To_Unavailable()
    {
        var sut = new SidecarTradeHoldChecker(new FakeProbe(SteamTradeHoldProbeResult.Unavailable));

        var result = await sut.CheckAsync(SteamId, Token, CancellationToken.None);

        Assert.False(result.Available);
        Assert.False(result.Active);
        Assert.Null(result.SetupGuideUrl);
    }

    [Fact]
    public async Task CheckAsync_Forwards_SteamId_And_Token_To_Probe()
    {
        var probe = new FakeProbe(SteamTradeHoldProbeResult.Active);
        var sut = new SidecarTradeHoldChecker(probe);

        await sut.CheckAsync(SteamId, Token, CancellationToken.None);

        Assert.Equal(SteamId, probe.LastSteamId);
        Assert.Equal(Token, probe.LastToken);
    }

    private sealed class FakeProbe : ISteamTradeHoldProbe
    {
        private readonly SteamTradeHoldProbeResult _result;
        public string? LastSteamId { get; private set; }
        public string? LastToken { get; private set; }

        public FakeProbe(SteamTradeHoldProbeResult result) => _result = result;

        public Task<SteamTradeHoldProbeResult> ProbeAsync(
            string steamId64, string tradeOfferAccessToken, CancellationToken cancellationToken)
        {
            LastSteamId = steamId64;
            LastToken = tradeOfferAccessToken;
            return Task.FromResult(_result);
        }
    }
}
