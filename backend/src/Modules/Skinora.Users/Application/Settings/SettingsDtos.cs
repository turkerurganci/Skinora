namespace Skinora.Users.Application.Settings;

// --- GET /users/me/settings (07 §5.6) ----------------------------------------

public sealed record AccountSettingsDto(
    string Language,
    NotificationSettingsDto Notifications);

public sealed record NotificationSettingsDto(
    EmailChannelDto Email,
    ExternalChannelDto Telegram,
    ExternalChannelDto Discord,
    PlatformChannelDto Platform);

/// <param name="Available">
/// T165 — false when this deployment cannot send on the channel (switched off with
/// <c>Provider = disabled</c>, or the real provider is missing a secret). Enabling,
/// connecting and e-mail verification answer <c>CHANNEL_UNAVAILABLE</c> (409) then.
/// </param>
public sealed record EmailChannelDto(bool Enabled, string? Address, bool Verified, bool Available);

/// <param name="Available">See <see cref="EmailChannelDto"/>.</param>
public sealed record ExternalChannelDto(bool Enabled, bool Connected, string? Username, bool Available);

/// <summary>
/// Platform (in-app) channel — <c>canDisable=false</c> always (04 §7.6).
/// </summary>
public sealed record PlatformChannelDto(bool Enabled, bool CanDisable);

// --- PUT /users/me/settings/language (07 §5.10) ------------------------------

public sealed record UpdateLanguageRequest(string? Language);

public sealed record LanguageResponse(string Language);

// --- PUT /users/me/settings/notifications (07 §5.9) --------------------------

public sealed record UpdateNotificationsRequest(
    NotificationChannelUpdate? Email,
    NotificationChannelUpdate? Telegram,
    NotificationChannelUpdate? Discord);

public sealed record NotificationChannelUpdate(bool? Enabled, string? Address);

// --- Email verification (07 §5.7, §5.8) --------------------------------------

public sealed record EmailVerificationSentResponse(string SentTo, int ExpiresIn);

public sealed record EmailVerifyRequest(string? Code);

public sealed record EmailVerifiedResponse(bool Verified, DateTime VerifiedAt);

// --- Telegram / Discord connect (07 §5.11–§5.13) -----------------------------

public sealed record TelegramConnectResponse(string VerificationCode, string BotUrl, int ExpiresIn);

public sealed record DiscordConnectResponse(string DiscordAuthUrl);

// --- Steam trade URL (07 §5.16a) ---------------------------------------------

public sealed record UpdateTradeUrlRequest(string? TradeUrl);

public sealed record TradeUrlResponse(
    string TradeUrl,
    bool MobileAuthenticatorActive,
    string? SetupGuideUrl);
