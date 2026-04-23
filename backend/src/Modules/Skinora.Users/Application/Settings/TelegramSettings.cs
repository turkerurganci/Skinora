namespace Skinora.Users.Application.Settings;

/// <summary>
/// Strongly-typed config for Telegram bot linking. Bound at the API
/// composition root from the <c>Telegram</c> configuration section.
/// </summary>
public sealed class TelegramSettings
{
    public const string SectionName = "Telegram";

    public string BotUrl { get; set; } = "https://t.me/SkinoraBot";
    public string WebhookSecretToken { get; set; } = string.Empty;
    public int CodeTtlSeconds { get; set; } = 300;
}
