namespace Skinora.Users.Application.Settings;

/// <summary>
/// Error codes returned by the account settings endpoints (07 §5.6–§5.16a).
/// Stable strings that the frontend maps to localized copy.
/// </summary>
public static class SettingsErrorCodes
{
    public const string ValidationError = "VALIDATION_ERROR";
    public const string InvalidLanguage = "INVALID_LANGUAGE";
    public const string NoEmailSet = "NO_EMAIL_SET";
    public const string VerificationCooldown = "VERIFICATION_COOLDOWN";
    public const string InvalidVerificationCode = "INVALID_VERIFICATION_CODE";
    public const string VerificationCodeExpired = "VERIFICATION_CODE_EXPIRED";
    public const string ChannelNotConnected = "CHANNEL_NOT_CONNECTED";
    public const string InvalidTradeUrl = "INVALID_TRADE_URL";
    public const string SteamApiUnavailable = "STEAM_API_UNAVAILABLE";
    public const string InvalidOAuthState = "INVALID_OAUTH_STATE";
    public const string AlreadyLinked = "ALREADY_LINKED";
    public const string WebhookUnauthorized = "WEBHOOK_UNAUTHORIZED";
}
