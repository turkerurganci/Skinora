using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Skinora.Auth.Application.ReAuthentication;
using Skinora.Auth.Application.SteamAuthentication;
using Skinora.Auth.Configuration;

namespace Skinora.Auth.Tests.Unit;

public class ReAuthPipelineTests
{
    private const string SteamId = "76561198000000042";
    private const string ReVerifyReturnTo =
        "https://skinora.test/api/v1/auth/steam/re-verify/callback";

    private readonly Mock<ISteamOpenIdValidator> _validator = new();
    private readonly Mock<IReAuthStateProtector> _protector = new();
    private readonly Mock<IReAuthTokenStore> _store = new();
    private readonly IReturnUrlValidator _returnUrlValidator = new ReturnUrlValidator("/profile");
    private readonly TimeProvider _timeProvider = TimeProvider.System;

    private ReAuthPipeline BuildPipeline() => new(
        Options.Create(new SteamOpenIdSettings
        {
            Realm = "https://skinora.test",
            ReturnToUrl = "https://skinora.test/api/v1/auth/steam/callback",
            ReVerifyReturnToUrl = ReVerifyReturnTo,
            FrontendCallbackUrl = "https://skinora.test/auth/callback",
        }),
        _validator.Object,
        _protector.Object,
        _store.Object,
        _returnUrlValidator,
        _timeProvider,
        NullLogger<ReAuthPipeline>.Instance);

    private static Dictionary<string, string> ValidCallback() => new()
    {
        ["openid.mode"] = "id_res",
        ["openid.return_to"] = ReVerifyReturnTo,
        ["openid.claimed_id"] = $"https://steamcommunity.com/openid/id/{SteamId}",
    };

    [Fact]
    public void Initiate_ProducesSteamUrlAndProtectedState()
    {
        _protector.Setup(p => p.Protect(It.IsAny<ReAuthState>())).Returns("protected");

        var result = BuildPipeline().Initiate(Guid.NewGuid(), SteamId, "/profile");

        Assert.StartsWith("https://steamcommunity.com/openid/login?", result.SteamAuthUrl);
        Assert.Contains("openid.return_to=" + Uri.EscapeDataString(ReVerifyReturnTo), result.SteamAuthUrl);
        Assert.Equal("protected", result.ProtectedState);
    }

    [Fact]
    public void Initiate_RejectsOpenRedirectReturnUrl()
    {
        ReAuthState? captured = null;
        _protector.Setup(p => p.Protect(It.IsAny<ReAuthState>()))
            .Callback<ReAuthState>(s => captured = s)
            .Returns("protected");

        BuildPipeline().Initiate(Guid.NewGuid(), SteamId, "https://evil.com/phish");

        Assert.NotNull(captured);
        Assert.Equal("/profile", captured!.ReturnUrl);
    }

    [Fact]
    public async Task HandleCallbackAsync_NoState_ReturnsStateMissing()
    {
        _protector.Setup(p => p.Unprotect(null)).Returns((ReAuthState?)null);

        var outcome = await BuildPipeline().HandleCallbackAsync(ValidCallback(), null, default);

        Assert.IsType<ReAuthOutcome.StateMissing>(outcome);
        _validator.VerifyNoOtherCalls();
        _store.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task HandleCallbackAsync_InvalidAssertion_ReturnsAuthFailed()
    {
        var userId = Guid.NewGuid();
        var state = new ReAuthState(userId, SteamId, "/profile", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        _protector.Setup(p => p.Unprotect("cookie")).Returns(state);
        _validator.Setup(v => v.ValidateAsync(
                It.IsAny<IReadOnlyDictionary<string, string>>(), ReVerifyReturnTo, default))
            .ReturnsAsync(SteamOpenIdValidationResult.Failure("is_valid:false"));

        var outcome = await BuildPipeline().HandleCallbackAsync(ValidCallback(), "cookie", default);

        var failed = Assert.IsType<ReAuthOutcome.AuthFailed>(outcome);
        Assert.Equal("is_valid:false", failed.Reason);
        _store.Verify(s => s.IssueAsync(
            It.IsAny<string>(), It.IsAny<ReAuthTokenPayload>(), It.IsAny<TimeSpan>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleCallbackAsync_SteamIdMismatch_ReturnsSteamIdMismatch()
    {
        var state = new ReAuthState(
            Guid.NewGuid(), SteamId, "/profile", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        _protector.Setup(p => p.Unprotect("cookie")).Returns(state);
        _validator.Setup(v => v.ValidateAsync(
                It.IsAny<IReadOnlyDictionary<string, string>>(), ReVerifyReturnTo, default))
            .ReturnsAsync(SteamOpenIdValidationResult.Success("76561198999999999"));

        var outcome = await BuildPipeline().HandleCallbackAsync(ValidCallback(), "cookie", default);

        Assert.IsType<ReAuthOutcome.SteamIdMismatch>(outcome);
        _store.Verify(s => s.IssueAsync(
            It.IsAny<string>(), It.IsAny<ReAuthTokenPayload>(), It.IsAny<TimeSpan>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleCallbackAsync_Valid_IssuesTokenAndReturnsSuccess()
    {
        var userId = Guid.NewGuid();
        var state = new ReAuthState(userId, SteamId, "/profile", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        _protector.Setup(p => p.Unprotect("cookie")).Returns(state);
        _validator.Setup(v => v.ValidateAsync(
                It.IsAny<IReadOnlyDictionary<string, string>>(), ReVerifyReturnTo, default))
            .ReturnsAsync(SteamOpenIdValidationResult.Success(SteamId));

        string? issuedPlainText = null;
        ReAuthTokenPayload? issuedPayload = null;
        TimeSpan? issuedTtl = null;
        _store.Setup(s => s.IssueAsync(
                It.IsAny<string>(), It.IsAny<ReAuthTokenPayload>(), It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, ReAuthTokenPayload, TimeSpan, CancellationToken>((t, p, ttl, _) =>
            {
                issuedPlainText = t;
                issuedPayload = p;
                issuedTtl = ttl;
            })
            .Returns(Task.CompletedTask);

        var outcome = await BuildPipeline().HandleCallbackAsync(ValidCallback(), "cookie", default);

        var success = Assert.IsType<ReAuthOutcome.Success>(outcome);
        Assert.False(string.IsNullOrWhiteSpace(success.ReAuthToken));
        Assert.Equal("/profile", success.ReturnUrl);
        Assert.Equal(success.ReAuthToken, issuedPlainText);
        Assert.Equal(userId, issuedPayload!.UserId);
        Assert.Equal(SteamId, issuedPayload.SteamId);
        Assert.Equal(ReAuthPipeline.ReAuthTokenTtl, issuedTtl);
    }
}
