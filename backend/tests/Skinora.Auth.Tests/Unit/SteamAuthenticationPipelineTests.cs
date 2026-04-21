using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Skinora.Auth.Application.SteamAuthentication;
using Skinora.Auth.Domain.Entities;
using Skinora.Users.Domain.Entities;

namespace Skinora.Auth.Tests.Unit;

public class SteamAuthenticationPipelineTests
{
    private const string SteamId = "76561198000000042";

    private readonly Mock<ISteamOpenIdValidator> _validator = new();
    private readonly Mock<ISteamProfileClient> _profile = new();
    private readonly Mock<IUserProvisioningService> _provisioning = new();
    private readonly Mock<IAccessTokenGenerator> _access = new();
    private readonly Mock<IRefreshTokenGenerator> _refresh = new();
    private readonly Mock<ILoginAuditService> _audit = new();
    private readonly Mock<IGeoBlockCheck> _geo = new();
    private readonly Mock<ISanctionsCheck> _sanctions = new();
    private readonly Mock<IAgeGateCheck> _ageGate = new();

    private readonly ILogger<SteamAuthenticationPipeline> _logger =
        NullLogger<SteamAuthenticationPipeline>.Instance;

    private SteamAuthenticationPipeline BuildPipeline()
    {
        _ageGate.Setup(a => a.EvaluateAsync(It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AgeGateDecision.Allowed());
        return new(_validator.Object, _profile.Object, _provisioning.Object,
            _access.Object, _refresh.Object, _audit.Object,
            _geo.Object, _sanctions.Object, _ageGate.Object, _logger);
    }

    private static Dictionary<string, string> ValidCallback() => new()
    {
        ["openid.mode"] = "id_res",
        ["openid.claimed_id"] = $"https://steamcommunity.com/openid/id/{SteamId}",
    };

    [Fact]
    public async Task ExecuteAsync_InvalidAssertion_ReturnsAuthFailed()
    {
        _validator.Setup(v => v.ValidateAsync(It.IsAny<IReadOnlyDictionary<string, string>>(), default))
            .ReturnsAsync(SteamOpenIdValidationResult.Failure("is_valid:false"));

        var result = await BuildPipeline().ExecuteAsync(ValidCallback(), "1.2.3.4", "agent", default);

        Assert.IsType<AuthenticationOutcome.AuthFailed>(result);
        _provisioning.VerifyNoOtherCalls();
        _access.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ExecuteAsync_GeoBlocked_ReturnsGeoBlockedAndSkipsProvisioning()
    {
        StubValidAssertion();
        _geo.Setup(g => g.EvaluateAsync(It.IsAny<string?>(), default))
            .ReturnsAsync(GeoBlockDecision.Blocked("XX"));

        var result = await BuildPipeline().ExecuteAsync(ValidCallback(), "1.2.3.4", null, default);

        var blocked = Assert.IsType<AuthenticationOutcome.GeoBlocked>(result);
        Assert.Equal("XX", blocked.CountryCode);
        _provisioning.Verify(p => p.UpsertFromSteamLoginAsync(
            It.IsAny<string>(), It.IsAny<SteamPlayerSummary?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_SanctionsMatch_ReturnsSanctionsMatchAndSkipsProvisioning()
    {
        StubValidAssertion();
        _geo.Setup(g => g.EvaluateAsync(It.IsAny<string?>(), default))
            .ReturnsAsync(GeoBlockDecision.Allowed());
        _sanctions.Setup(s => s.EvaluateAsync(SteamId, default))
            .ReturnsAsync(SanctionsDecision.Match("OFAC"));

        var result = await BuildPipeline().ExecuteAsync(ValidCallback(), null, null, default);

        var match = Assert.IsType<AuthenticationOutcome.SanctionsMatch>(result);
        Assert.Equal("OFAC", match.List);
        _provisioning.Verify(p => p.UpsertFromSteamLoginAsync(
            It.IsAny<string>(), It.IsAny<SteamPlayerSummary?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_AgeGateBlocked_ReturnsAgeBlockedAndSkipsProvisioning()
    {
        StubValidAssertion();
        _geo.Setup(g => g.EvaluateAsync(It.IsAny<string?>(), default))
            .ReturnsAsync(GeoBlockDecision.Allowed());
        _sanctions.Setup(s => s.EvaluateAsync(SteamId, default))
            .ReturnsAsync(SanctionsDecision.NoMatch());

        var youngCreated = DateTime.UtcNow.AddDays(-5);
        _profile.Setup(p => p.GetPlayerSummaryAsync(SteamId, default))
            .ReturnsAsync(new SteamPlayerSummary(SteamId, "Persona", null, youngCreated));

        _ageGate.Setup(a => a.EvaluateAsync(youngCreated, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AgeGateDecision.Blocked(accountAgeDays: 5, requiredDays: 30));

        var result = await new SteamAuthenticationPipeline(
            _validator.Object, _profile.Object, _provisioning.Object,
            _access.Object, _refresh.Object, _audit.Object,
            _geo.Object, _sanctions.Object, _ageGate.Object, _logger)
            .ExecuteAsync(ValidCallback(), null, null, default);

        var blocked = Assert.IsType<AuthenticationOutcome.AgeBlocked>(result);
        Assert.Equal(5, blocked.AccountAgeDays);
        Assert.Equal(30, blocked.RequiredDays);
        _provisioning.Verify(p => p.UpsertFromSteamLoginAsync(
            It.IsAny<string>(), It.IsAny<SteamPlayerSummary?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_DeactivatedUser_ReturnsAccountBannedAndSkipsTokenIssuance()
    {
        StubValidAssertion();
        _geo.Setup(g => g.EvaluateAsync(It.IsAny<string?>(), default))
            .ReturnsAsync(GeoBlockDecision.Allowed());
        _sanctions.Setup(s => s.EvaluateAsync(SteamId, default))
            .ReturnsAsync(SanctionsDecision.NoMatch());

        var bannedUser = new User { Id = Guid.NewGuid(), SteamId = SteamId, IsDeactivated = true };
        _provisioning.Setup(p => p.UpsertFromSteamLoginAsync(SteamId, It.IsAny<SteamPlayerSummary?>(), default))
            .ReturnsAsync(new UserProvisioningResult(bannedUser, IsNewUser: false));

        var result = await BuildPipeline().ExecuteAsync(ValidCallback(), null, null, default);

        Assert.IsType<AuthenticationOutcome.AccountBanned>(result);
        _access.Verify(a => a.Generate(It.IsAny<User>()), Times.Never);
        _refresh.Verify(r => r.IssueAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<string?>(), default),
            Times.Never);
        _audit.Verify(a => a.RecordLoginAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<string?>(), default),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_NewUser_ReturnsSuccessWithIsNewUserTrue()
    {
        await RunHappyPath(isNewUser: true);
    }

    [Fact]
    public async Task ExecuteAsync_ExistingUser_ReturnsSuccessWithIsNewUserFalse()
    {
        await RunHappyPath(isNewUser: false);
    }

    private async Task RunHappyPath(bool isNewUser)
    {
        StubValidAssertion();
        _geo.Setup(g => g.EvaluateAsync(It.IsAny<string?>(), default))
            .ReturnsAsync(GeoBlockDecision.Allowed());
        _sanctions.Setup(s => s.EvaluateAsync(SteamId, default))
            .ReturnsAsync(SanctionsDecision.NoMatch());

        var profile = new SteamPlayerSummary(SteamId, "Persona", "avatar-url", null);
        _profile.Setup(p => p.GetPlayerSummaryAsync(SteamId, default))
            .ReturnsAsync(profile);

        var user = new User { Id = Guid.NewGuid(), SteamId = SteamId, SteamDisplayName = "Persona" };
        _provisioning.Setup(p => p.UpsertFromSteamLoginAsync(SteamId, profile, default))
            .ReturnsAsync(new UserProvisioningResult(user, isNewUser));

        var accessToken = new GeneratedAccessToken("access.jwt", DateTime.UtcNow.AddMinutes(15));
        _access.Setup(a => a.Generate(user)).Returns(accessToken);

        var refreshEntity = new RefreshToken { Id = Guid.NewGuid(), UserId = user.Id };
        var refreshToken = new GeneratedRefreshToken(refreshEntity, "refresh-plain",
            DateTime.UtcNow.AddDays(7));
        _refresh.Setup(r => r.IssueAsync(user.Id, "1.2.3.4", "agent", default))
            .ReturnsAsync(refreshToken);

        var result = await BuildPipeline().ExecuteAsync(ValidCallback(), "1.2.3.4", "agent", default);

        var success = Assert.IsType<AuthenticationOutcome.Success>(result);
        Assert.Equal(user, success.User);
        Assert.Equal(accessToken, success.AccessToken);
        Assert.Equal(refreshToken, success.RefreshToken);
        Assert.Equal(isNewUser, success.IsNewUser);
        _audit.Verify(a => a.RecordLoginAsync(user.Id, "1.2.3.4", "agent", default), Times.Once);
    }

    private void StubValidAssertion()
    {
        _validator.Setup(v => v.ValidateAsync(It.IsAny<IReadOnlyDictionary<string, string>>(), default))
            .ReturnsAsync(SteamOpenIdValidationResult.Success(SteamId));
    }
}
