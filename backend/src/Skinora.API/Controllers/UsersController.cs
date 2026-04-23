using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Skinora.API.RateLimiting;
using Skinora.Auth.Application.MobileAuthenticator;
using Skinora.Auth.Application.ReAuthentication;
using Skinora.Auth.Configuration;
using Skinora.Shared.Models;
using Skinora.Users.Application.Profiles;
using Skinora.Users.Application.Settings;
using Skinora.Users.Application.Wallet;

namespace Skinora.API.Controllers;

/// <summary>
/// User profile + wallet + account settings endpoints — 07 §5.1–§5.16a.
/// </summary>
[ApiController]
[Route("api/v1/users")]
public sealed class UsersController : ControllerBase
{
    /// <summary>HTTP header name from 07 §4.7 / §5.3 "Ek Auth".</summary>
    private const string ReAuthTokenHeader = "X-ReAuth-Token";

    private readonly IUserProfileService _profileService;
    private readonly IWalletAddressService _walletService;
    private readonly IReAuthTokenValidator _reAuthValidator;
    private readonly IAccountSettingsService _settingsService;
    private readonly ILanguageService _languageService;
    private readonly INotificationPreferenceService _notificationPrefs;
    private readonly IEmailVerificationService _emailVerification;
    private readonly ITelegramConnectionService _telegramService;
    private readonly IDiscordConnectionService _discordService;
    private readonly ISteamTradeUrlService _tradeUrlService;
    private readonly ITradeHoldChecker _tradeHoldChecker;
    private readonly ITradeUrlParser _tradeUrlParser;

    public UsersController(
        IUserProfileService profileService,
        IWalletAddressService walletService,
        IReAuthTokenValidator reAuthValidator,
        IAccountSettingsService settingsService,
        ILanguageService languageService,
        INotificationPreferenceService notificationPrefs,
        IEmailVerificationService emailVerification,
        ITelegramConnectionService telegramService,
        IDiscordConnectionService discordService,
        ISteamTradeUrlService tradeUrlService,
        ITradeHoldChecker tradeHoldChecker,
        ITradeUrlParser tradeUrlParser)
    {
        _profileService = profileService;
        _walletService = walletService;
        _reAuthValidator = reAuthValidator;
        _settingsService = settingsService;
        _languageService = languageService;
        _notificationPrefs = notificationPrefs;
        _emailVerification = emailVerification;
        _telegramService = telegramService;
        _discordService = discordService;
        _tradeUrlService = tradeUrlService;
        _tradeHoldChecker = tradeHoldChecker;
        _tradeUrlParser = tradeUrlParser;
    }

    /// <summary>U1 — <c>GET /users/me</c>. Own profile for S08 (07 §5.1).</summary>
    [HttpGet("me")]
    [Authorize(Policy = AuthPolicies.Authenticated)]
    [RateLimit("user-read")]
    public async Task<ActionResult<UserProfileDto>> GetMe(CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();

        var dto = await _profileService.GetOwnProfileAsync(userId, cancellationToken);
        if (dto is null) return Unauthorized();

        return Ok(dto);
    }

    /// <summary>U2 — <c>GET /users/me/stats</c>. Dashboard quick stats (07 §5.2).</summary>
    [HttpGet("me/stats")]
    [Authorize(Policy = AuthPolicies.Authenticated)]
    [RateLimit("user-read")]
    public async Task<ActionResult<UserStatsDto>> GetMyStats(CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();

        var dto = await _profileService.GetOwnStatsAsync(userId, cancellationToken);
        if (dto is null) return Unauthorized();

        return Ok(dto);
    }

    /// <summary>U5 — <c>GET /users/{steamId}</c>. Public profile (07 §5.5).</summary>
    [HttpGet("{steamId}")]
    [AllowAnonymous]
    [RateLimit("public")]
    public async Task<ActionResult<PublicUserProfileDto>> GetPublic(
        string steamId, CancellationToken cancellationToken)
    {
        var dto = await _profileService.GetPublicProfileAsync(steamId, cancellationToken);
        if (dto is null)
        {
            var body = ApiResponse<object>.Fail(
                "USER_NOT_FOUND",
                $"User '{steamId}' was not found.",
                traceId: HttpContext.TraceIdentifier);
            return NotFound(body);
        }

        return Ok(dto);
    }

    /// <summary>U3 — <c>PUT /users/me/wallet/seller</c> (07 §5.3).</summary>
    [HttpPut("me/wallet/seller")]
    [Authorize(Policy = AuthPolicies.Authenticated)]
    [RateLimit("user-write")]
    public Task<IActionResult> UpdateSellerWallet(
        [FromBody] UpdateWalletRequest request,
        CancellationToken cancellationToken)
        => UpdateWalletAsync(WalletRole.Seller, request, cancellationToken);

    /// <summary>U4 — <c>PUT /users/me/wallet/refund</c> (07 §5.4).</summary>
    [HttpPut("me/wallet/refund")]
    [Authorize(Policy = AuthPolicies.Authenticated)]
    [RateLimit("user-write")]
    public Task<IActionResult> UpdateRefundWallet(
        [FromBody] UpdateWalletRequest request,
        CancellationToken cancellationToken)
        => UpdateWalletAsync(WalletRole.Buyer, request, cancellationToken);

    // ---------- T35 / account settings (07 §5.6–§5.16a) ----------

    /// <summary>U6 — <c>GET /users/me/settings</c> (07 §5.6).</summary>
    [HttpGet("me/settings")]
    [Authorize(Policy = AuthPolicies.Authenticated)]
    [RateLimit("user-read")]
    public async Task<ActionResult<AccountSettingsDto>> GetSettings(
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();

        var dto = await _settingsService.GetAsync(userId, cancellationToken);
        if (dto is null) return Unauthorized();

        return Ok(dto);
    }

    /// <summary>U8 — <c>PUT /users/me/settings/language</c> (07 §5.10).</summary>
    [HttpPut("me/settings/language")]
    [Authorize(Policy = AuthPolicies.Authenticated)]
    [RateLimit("user-write")]
    public async Task<IActionResult> UpdateLanguage(
        [FromBody] UpdateLanguageRequest? request, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        if (request is null) return ValidationError("Request body is required.");

        var result = await _languageService.UpdateAsync(
            userId, request.Language, cancellationToken);

        return result.Status switch
        {
            LanguageUpdateStatus.Success => Ok(new LanguageResponse(result.Language!)),
            LanguageUpdateStatus.UserNotFound => Unauthorized(),
            LanguageUpdateStatus.InvalidLanguage => BadRequest(ApiResponse<object>.Fail(
                SettingsErrorCodes.InvalidLanguage,
                "Supported languages: en, zh, es, tr.",
                traceId: HttpContext.TraceIdentifier)),
            _ => StatusCode(StatusCodes.Status500InternalServerError),
        };
    }

    /// <summary>U7 — <c>PUT /users/me/settings/notifications</c> (07 §5.9).</summary>
    [HttpPut("me/settings/notifications")]
    [Authorize(Policy = AuthPolicies.Authenticated)]
    [RateLimit("user-write")]
    public async Task<IActionResult> UpdateNotifications(
        [FromBody] UpdateNotificationsRequest? request, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        if (request is null) return ValidationError("Request body is required.");

        var result = await _notificationPrefs.UpdateAsync(userId, request, cancellationToken);

        switch (result.Status)
        {
            case NotificationPreferenceUpdateStatus.Success:
                var dto = await _settingsService.GetAsync(userId, cancellationToken);
                return dto is null ? Unauthorized() : Ok(dto);

            case NotificationPreferenceUpdateStatus.UserNotFound:
                return Unauthorized();

            case NotificationPreferenceUpdateStatus.ValidationError:
                return ValidationError(
                    $"Validation failed for channel '{result.FailedChannel}'.");

            case NotificationPreferenceUpdateStatus.ChannelNotConnected:
                return UnprocessableEntity(ApiResponse<object>.Fail(
                    SettingsErrorCodes.ChannelNotConnected,
                    $"Channel '{result.FailedChannel}' must be connected before it can be toggled.",
                    traceId: HttpContext.TraceIdentifier));

            default:
                return StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>U15 — <c>POST /users/me/settings/email/send-verification</c> (07 §5.7).</summary>
    [HttpPost("me/settings/email/send-verification")]
    [Authorize(Policy = AuthPolicies.Authenticated)]
    [RateLimit("user-write")]
    public async Task<IActionResult> SendEmailVerification(CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();

        var result = await _emailVerification.SendAsync(userId, cancellationToken);
        return result.Status switch
        {
            EmailVerificationSendStatus.Sent => Ok(new EmailVerificationSentResponse(
                result.MaskedAddress!, result.ExpiresInSeconds)),
            EmailVerificationSendStatus.UserNotFound => Unauthorized(),
            EmailVerificationSendStatus.NoEmailSet => UnprocessableEntity(ApiResponse<object>.Fail(
                SettingsErrorCodes.NoEmailSet,
                "No email address is configured on the account.",
                traceId: HttpContext.TraceIdentifier)),
            EmailVerificationSendStatus.Cooldown => StatusCode(
                StatusCodes.Status429TooManyRequests,
                ApiResponse<object>.Fail(
                    SettingsErrorCodes.VerificationCooldown,
                    $"Wait {result.RetryAfterSeconds}s before requesting another code.",
                    traceId: HttpContext.TraceIdentifier)),
            _ => StatusCode(StatusCodes.Status500InternalServerError),
        };
    }

    /// <summary>U16 — <c>POST /users/me/settings/email/verify</c> (07 §5.8).</summary>
    [HttpPost("me/settings/email/verify")]
    [Authorize(Policy = AuthPolicies.Authenticated)]
    [RateLimit("user-write")]
    public async Task<IActionResult> VerifyEmail(
        [FromBody] EmailVerifyRequest? request, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        if (request is null) return ValidationError("Request body is required.");

        var result = await _emailVerification.VerifyAsync(userId, request.Code, cancellationToken);
        return result.Status switch
        {
            EmailVerifyStatus.Verified => Ok(new EmailVerifiedResponse(true, result.VerifiedAt!.Value)),
            EmailVerifyStatus.UserNotFound => Unauthorized(),
            EmailVerifyStatus.NoEmailSet => UnprocessableEntity(ApiResponse<object>.Fail(
                SettingsErrorCodes.NoEmailSet,
                "No email address is configured on the account.",
                traceId: HttpContext.TraceIdentifier)),
            EmailVerifyStatus.CodeExpired => UnprocessableEntity(ApiResponse<object>.Fail(
                SettingsErrorCodes.VerificationCodeExpired,
                "Verification code has expired. Request a new one.",
                traceId: HttpContext.TraceIdentifier)),
            EmailVerifyStatus.InvalidCode => BadRequest(ApiResponse<object>.Fail(
                SettingsErrorCodes.InvalidVerificationCode,
                "Verification code is invalid.",
                traceId: HttpContext.TraceIdentifier)),
            _ => StatusCode(StatusCodes.Status500InternalServerError),
        };
    }

    /// <summary>U9 — <c>POST /users/me/settings/telegram/connect</c> (07 §5.11).</summary>
    [HttpPost("me/settings/telegram/connect")]
    [Authorize(Policy = AuthPolicies.Authenticated)]
    [RateLimit("user-write")]
    public async Task<ActionResult<TelegramConnectResponse>> InitiateTelegramConnect(
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();

        var result = await _telegramService.InitiateAsync(userId, cancellationToken);
        return Ok(new TelegramConnectResponse(
            result.Code, result.BotUrl, (int)result.Ttl.TotalSeconds));
    }

    /// <summary>U11 — <c>DELETE /users/me/settings/telegram</c> (07 §5.14).</summary>
    [HttpDelete("me/settings/telegram")]
    [Authorize(Policy = AuthPolicies.Authenticated)]
    [RateLimit("user-write")]
    public async Task<IActionResult> DisconnectTelegram(CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();

        var result = await _telegramService.DisconnectAsync(userId, cancellationToken);
        return result.Status switch
        {
            TelegramDisconnectStatus.Removed => Ok(),
            TelegramDisconnectStatus.NotConnected => Ok(),
            TelegramDisconnectStatus.UserNotFound => Unauthorized(),
            _ => StatusCode(StatusCodes.Status500InternalServerError),
        };
    }

    /// <summary>U10 — <c>POST /users/me/settings/discord/connect</c> (07 §5.12).</summary>
    [HttpPost("me/settings/discord/connect")]
    [Authorize(Policy = AuthPolicies.Authenticated)]
    [RateLimit("user-write")]
    public async Task<ActionResult<DiscordConnectResponse>> InitiateDiscordConnect(
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();

        var result = await _discordService.BuildAuthorizeUrlAsync(userId, cancellationToken);
        return Ok(new DiscordConnectResponse(result.Url));
    }

    /// <summary>U10b — <c>GET /users/me/settings/discord/callback</c> (07 §5.13).</summary>
    [HttpGet("me/settings/discord/callback")]
    [AllowAnonymous]
    [RateLimit("auth")]
    public async Task<IActionResult> DiscordCallback(
        [FromQuery(Name = "code")] string? code,
        [FromQuery(Name = "state")] string? state,
        [FromQuery(Name = "error")] string? error,
        [FromServices] Microsoft.Extensions.Options.IOptions<DiscordSettings> settingsOptions,
        CancellationToken cancellationToken)
    {
        var settings = settingsOptions.Value;
        var result = await _discordService.HandleCallbackAsync(code, state, error, cancellationToken);

        var redirect = result.Status switch
        {
            DiscordCallbackStatus.Connected => settings.SuccessRedirectUrl,
            DiscordCallbackStatus.UserDenied =>
                $"{settings.FailureRedirectUrl}&reason=denied",
            DiscordCallbackStatus.AlreadyLinkedToAnotherUser =>
                $"{settings.FailureRedirectUrl}&reason=already_linked",
            DiscordCallbackStatus.InvalidState =>
                $"{settings.FailureRedirectUrl}&reason=invalid_state",
            _ => $"{settings.FailureRedirectUrl}&reason=exchange_failed",
        };

        return Redirect(redirect);
    }

    /// <summary>U12 — <c>DELETE /users/me/settings/discord</c> (07 §5.15).</summary>
    [HttpDelete("me/settings/discord")]
    [Authorize(Policy = AuthPolicies.Authenticated)]
    [RateLimit("user-write")]
    public async Task<IActionResult> DisconnectDiscord(CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();

        var result = await _discordService.DisconnectAsync(userId, cancellationToken);
        return result.Status switch
        {
            DiscordDisconnectStatus.Removed => Ok(),
            DiscordDisconnectStatus.NotConnected => Ok(),
            DiscordDisconnectStatus.UserNotFound => Unauthorized(),
            _ => StatusCode(StatusCodes.Status500InternalServerError),
        };
    }

    /// <summary>U17 — <c>PUT /users/me/settings/steam/trade-url</c> (07 §5.16a).</summary>
    [HttpPut("me/settings/steam/trade-url")]
    [Authorize(Policy = AuthPolicies.Authenticated)]
    [RateLimit("user-write")]
    public async Task<IActionResult> UpdateTradeUrl(
        [FromBody] UpdateTradeUrlRequest? request, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        if (request is null) return ValidationError("Request body is required.");

        var steamIdClaim = User.FindFirstValue(AuthClaimTypes.SteamId);
        if (string.IsNullOrWhiteSpace(steamIdClaim)) return Unauthorized();

        // Parse first so an invalid URL short-circuits before we call Steam.
        var parsed = _tradeUrlParser.Parse(request.TradeUrl);
        if (parsed is null)
            return UnprocessableEntity(ApiResponse<object>.Fail(
                SettingsErrorCodes.InvalidTradeUrl,
                "Trade URL format is invalid — expected https://steamcommunity.com/tradeoffer/new/?partner=...&token=...",
                traceId: HttpContext.TraceIdentifier));

        TradeHoldResult holdResult;
        try
        {
            holdResult = await _tradeHoldChecker.CheckAsync(
                steamIdClaim, parsed.Token, cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            // Any failure talking to Steam is treated as "pending" — 07 §5.16a
            // fallback: URL is persisted but surfaced as pending MA.
            holdResult = new TradeHoldResult(Available: false, Active: false, SetupGuideUrl: null);
        }

        var result = await _tradeUrlService.UpdateAsync(
            userId,
            parsed.Normalized,
            new TradeUrlMaOutcome(holdResult.Available, holdResult.Active, holdResult.SetupGuideUrl),
            cancellationToken);

        return result.Status switch
        {
            TradeUrlUpdateStatus.Success => Ok(new TradeUrlResponse(
                result.TradeUrl!, result.MobileAuthenticatorActive, result.SetupGuideUrl)),

            TradeUrlUpdateStatus.UserNotFound => Unauthorized(),

            TradeUrlUpdateStatus.InvalidTradeUrl => UnprocessableEntity(ApiResponse<object>.Fail(
                SettingsErrorCodes.InvalidTradeUrl,
                "Trade URL format is invalid.",
                traceId: HttpContext.TraceIdentifier)),

            TradeUrlUpdateStatus.SteamApiUnavailable => StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                ApiResponse<TradeUrlResponse>.Ok(new TradeUrlResponse(
                    result.TradeUrl!, false, null),
                    traceId: HttpContext.TraceIdentifier)),

            _ => StatusCode(StatusCodes.Status500InternalServerError),
        };
    }

    private async Task<IActionResult> UpdateWalletAsync(
        WalletRole role,
        UpdateWalletRequest? request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();

        if (request is null)
        {
            return BadRequest(ApiResponse<object>.Fail(
                WalletErrorCodes.ValidationError,
                "Request body is required.",
                traceId: HttpContext.TraceIdentifier));
        }

        // 07 §4.7 / §5.3 "Ek Auth": validate re-auth token *before* touching
        // domain state. Token is single-use and bound to a user id — reject
        // with RE_AUTH_TOKEN_INVALID if it fails for any reason (missing
        // header is a different branch handled downstream via RE_AUTH_REQUIRED).
        var reAuthHeader = Request.Headers.TryGetValue(ReAuthTokenHeader, out var headerValues)
            ? headerValues.ToString()
            : null;

        var reAuthValidated = false;
        if (!string.IsNullOrWhiteSpace(reAuthHeader))
        {
            var payload = await _reAuthValidator.ValidateAsync(reAuthHeader, cancellationToken);
            if (payload is null || payload.UserId != userId)
            {
                return StatusCode(StatusCodes.Status403Forbidden, ApiResponse<object>.Fail(
                    WalletErrorCodes.ReAuthTokenInvalid,
                    "Re-auth token is missing, expired, already consumed, or bound to a different user.",
                    traceId: HttpContext.TraceIdentifier));
            }
            reAuthValidated = true;
        }

        var result = await _walletService.UpdateWalletAsync(
            userId, role, request.WalletAddress, reAuthValidated, cancellationToken);

        return result.Status switch
        {
            WalletUpdateStatus.Success => Ok(new UpdateWalletResponse(
                result.WalletAddress!,
                result.UpdatedAt!.Value,
                result.ActiveTransactionsUsingOldAddress)),

            WalletUpdateStatus.UserNotFound => Unauthorized(),

            WalletUpdateStatus.InvalidAddress => BadRequest(ApiResponse<object>.Fail(
                WalletErrorCodes.InvalidWalletAddress,
                "Wallet address is not a valid Tron (TRC-20) address.",
                traceId: HttpContext.TraceIdentifier)),

            WalletUpdateStatus.SanctionsMatch => StatusCode(
                StatusCodes.Status403Forbidden,
                ApiResponse<object>.Fail(
                    WalletErrorCodes.SanctionsMatch,
                    "Wallet address matched a sanctions list and was rejected.",
                    traceId: HttpContext.TraceIdentifier)),

            WalletUpdateStatus.ReAuthRequired => StatusCode(
                StatusCodes.Status403Forbidden,
                ApiResponse<object>.Fail(
                    WalletErrorCodes.ReAuthRequired,
                    "Changing an existing wallet address requires Steam re-verification (X-ReAuth-Token).",
                    traceId: HttpContext.TraceIdentifier)),

            _ => StatusCode(StatusCodes.Status500InternalServerError),
        };
    }

    private bool TryGetUserId(out Guid userId)
    {
        var claim = User.FindFirstValue(AuthClaimTypes.UserId);
        return Guid.TryParse(claim, out userId);
    }

    private IActionResult ValidationError(string message)
        => BadRequest(ApiResponse<object>.Fail(
            SettingsErrorCodes.ValidationError, message, traceId: HttpContext.TraceIdentifier));
}
