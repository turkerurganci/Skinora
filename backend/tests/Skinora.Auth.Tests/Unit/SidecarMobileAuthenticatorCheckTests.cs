using Skinora.Auth.Application.MobileAuthenticator;
using Skinora.Shared.Steam;

namespace Skinora.Auth.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="SidecarMobileAuthenticatorCheck"/> (WP6) — the A7
/// re-verify mapping from <see cref="ISteamTradeHoldProbe"/> onto
/// <see cref="MobileAuthenticatorResult"/> (07 §4.8). The A7 contract is binary
/// (no "unavailable" state), so a failed probe must fail closed to inactive.
/// </summary>
public sealed class SidecarMobileAuthenticatorCheckTests
{
    private const string SteamId = "76561198000000001";
    private const string Token = "abc123";

    [Fact]
    public async Task CheckAsync_Maps_Active_Probe_To_Active_Without_SetupUrl()
    {
        var sut = new SidecarMobileAuthenticatorCheck(new FakeProbe(SteamTradeHoldProbeResult.Active));

        var result = await sut.CheckAsync(SteamId, Token, CancellationToken.None);

        Assert.True(result.Active);
        Assert.Null(result.SetupGuideUrl);
    }

    [Fact]
    public async Task CheckAsync_Maps_Inactive_Probe_To_Inactive_With_SetupUrl()
    {
        var sut = new SidecarMobileAuthenticatorCheck(new FakeProbe(SteamTradeHoldProbeResult.Inactive));

        var result = await sut.CheckAsync(SteamId, Token, CancellationToken.None);

        Assert.False(result.Active);
        Assert.Equal(StubMobileAuthenticatorCheck.DefaultSetupGuideUrl, result.SetupGuideUrl);
    }

    [Fact]
    public async Task CheckAsync_Fails_Closed_To_Inactive_When_Probe_Unavailable()
    {
        var sut = new SidecarMobileAuthenticatorCheck(
            new FakeProbe(SteamTradeHoldProbeResult.Unavailable));

        var result = await sut.CheckAsync(SteamId, Token, CancellationToken.None);

        // Steam outage must never surface as "MA active".
        Assert.False(result.Active);
        Assert.Equal(StubMobileAuthenticatorCheck.DefaultSetupGuideUrl, result.SetupGuideUrl);
    }

    [Fact]
    public async Task CheckAsync_Forwards_SteamId_And_Token_To_Probe()
    {
        var probe = new FakeProbe(SteamTradeHoldProbeResult.Active);
        var sut = new SidecarMobileAuthenticatorCheck(probe);

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
