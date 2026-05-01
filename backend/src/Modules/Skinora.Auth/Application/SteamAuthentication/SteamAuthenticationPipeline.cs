using Microsoft.Extensions.Logging;

namespace Skinora.Auth.Application.SteamAuthentication;

public sealed class SteamAuthenticationPipeline : ISteamAuthenticationPipeline
{
    private readonly ISteamOpenIdValidator _validator;
    private readonly ISteamProfileClient _profileClient;
    private readonly IUserProvisioningService _provisioning;
    private readonly IAccessTokenGenerator _accessTokenGenerator;
    private readonly IRefreshTokenGenerator _refreshTokenGenerator;
    private readonly ILoginAuditService _loginAudit;
    private readonly IGeoBlockCheck _geoBlock;
    private readonly ISanctionsCheck _sanctions;
    private readonly IAgeGateCheck _ageGate;
    private readonly ILogger<SteamAuthenticationPipeline> _logger;

    public SteamAuthenticationPipeline(
        ISteamOpenIdValidator validator,
        ISteamProfileClient profileClient,
        IUserProvisioningService provisioning,
        IAccessTokenGenerator accessTokenGenerator,
        IRefreshTokenGenerator refreshTokenGenerator,
        ILoginAuditService loginAudit,
        IGeoBlockCheck geoBlock,
        ISanctionsCheck sanctions,
        IAgeGateCheck ageGate,
        ILogger<SteamAuthenticationPipeline> logger)
    {
        _validator = validator;
        _profileClient = profileClient;
        _provisioning = provisioning;
        _accessTokenGenerator = accessTokenGenerator;
        _refreshTokenGenerator = refreshTokenGenerator;
        _loginAudit = loginAudit;
        _geoBlock = geoBlock;
        _sanctions = sanctions;
        _ageGate = ageGate;
        _logger = logger;
    }

    public async Task<AuthenticationOutcome> ExecuteAsync(
        IReadOnlyDictionary<string, string> callbackParameters,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken)
    {
        var validation = await _validator.ValidateAsync(callbackParameters, cancellationToken);
        if (!validation.IsValid || validation.SteamId64 is null)
        {
            _logger.LogWarning("Steam OpenID assertion rejected: {Reason}", validation.FailureReason);
            return new AuthenticationOutcome.AuthFailed(validation.FailureReason ?? "unknown");
        }

        var steamId64 = validation.SteamId64;

        var geo = await _geoBlock.EvaluateAsync(ipAddress, cancellationToken);
        if (geo.IsBlocked)
            return new AuthenticationOutcome.GeoBlocked(geo.CountryCode);

        var sanctions = await _sanctions.EvaluateAsync(steamId64, cancellationToken);
        if (sanctions.IsMatch)
            return new AuthenticationOutcome.SanctionsMatch(sanctions.MatchedList);

        var profile = await _profileClient.GetPlayerSummaryAsync(steamId64, cancellationToken);

        var age = await _ageGate.EvaluateAsync(profile?.AccountCreatedAt, cancellationToken);
        if (age.IsBlocked)
            return new AuthenticationOutcome.AgeBlocked(age.AccountAgeDays!.Value, age.RequiredDays!.Value);

        var provisioning = await _provisioning.UpsertFromSteamLoginAsync(
            steamId64, profile, cancellationToken);

        if (provisioning.User.IsDeactivated)
            return new AuthenticationOutcome.AccountBanned(provisioning.User.Id);

        var access = await _accessTokenGenerator.GenerateAsync(
            provisioning.User, cancellationToken);
        var refresh = await _refreshTokenGenerator.IssueAsync(
            provisioning.User.Id, ipAddress, userAgent, cancellationToken);

        await _loginAudit.RecordLoginAsync(
            provisioning.User.Id, ipAddress, userAgent, cancellationToken);

        return new AuthenticationOutcome.Success(
            provisioning.User, access, refresh, provisioning.IsNewUser);
    }
}
