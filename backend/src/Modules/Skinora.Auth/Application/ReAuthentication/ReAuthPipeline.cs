using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skinora.Auth.Application.SteamAuthentication;
using Skinora.Auth.Configuration;

namespace Skinora.Auth.Application.ReAuthentication;

public sealed class ReAuthPipeline : IReAuthPipeline
{
    private const int TokenByteLength = 48;
    public static readonly TimeSpan ReAuthTokenTtl = TimeSpan.FromMinutes(5);

    private readonly SteamOpenIdSettings _settings;
    private readonly ISteamOpenIdValidator _validator;
    private readonly IReAuthStateProtector _stateProtector;
    private readonly IReAuthTokenStore _tokenStore;
    private readonly IReturnUrlValidator _returnUrlValidator;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ReAuthPipeline> _logger;

    public ReAuthPipeline(
        IOptions<SteamOpenIdSettings> settings,
        ISteamOpenIdValidator validator,
        IReAuthStateProtector stateProtector,
        IReAuthTokenStore tokenStore,
        IReturnUrlValidator returnUrlValidator,
        TimeProvider timeProvider,
        ILogger<ReAuthPipeline> logger)
    {
        _settings = settings.Value;
        _validator = validator;
        _stateProtector = stateProtector;
        _tokenStore = tokenStore;
        _returnUrlValidator = returnUrlValidator;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public ReAuthInitiation Initiate(Guid userId, string steamId, string? returnUrl)
    {
        var sanitized = _returnUrlValidator.Sanitize(returnUrl);
        var state = new ReAuthState(
            userId,
            steamId,
            sanitized,
            _timeProvider.GetUtcNow().ToUnixTimeSeconds());

        var protectedState = _stateProtector.Protect(state);
        var steamUrl = SteamOpenIdUrlBuilder.Build(_settings, _settings.ReVerifyReturnToUrl);

        return new ReAuthInitiation(steamUrl, protectedState);
    }

    public async Task<ReAuthOutcome> HandleCallbackAsync(
        IReadOnlyDictionary<string, string> callbackParameters,
        string? protectedState,
        CancellationToken cancellationToken)
    {
        var state = _stateProtector.Unprotect(protectedState);
        if (state is null)
        {
            _logger.LogWarning("Re-verify callback hit without valid state cookie");
            return new ReAuthOutcome.StateMissing();
        }

        var validation = await _validator.ValidateAsync(
            callbackParameters, _settings.ReVerifyReturnToUrl, cancellationToken);

        if (!validation.IsValid || validation.SteamId64 is null)
        {
            _logger.LogWarning(
                "Re-verify assertion rejected: {Reason}", validation.FailureReason);
            return new ReAuthOutcome.AuthFailed(validation.FailureReason ?? "unknown");
        }

        if (!string.Equals(validation.SteamId64, state.SteamId, StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "Re-verify SteamID mismatch: state={StateSteamId}, callback={CallbackSteamId}",
                state.SteamId, validation.SteamId64);
            return new ReAuthOutcome.SteamIdMismatch();
        }

        var plainText = GenerateToken();
        await _tokenStore.IssueAsync(
            plainText,
            new ReAuthTokenPayload(state.UserId, state.SteamId),
            ReAuthTokenTtl,
            cancellationToken);

        return new ReAuthOutcome.Success(plainText, state.ReturnUrl);
    }

    private static string GenerateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(TokenByteLength);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
