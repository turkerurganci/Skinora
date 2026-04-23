namespace Skinora.Users.Application.Settings;

/// <summary>
/// Strongly-typed config for Discord OAuth linking (07 §5.12, §5.13).
/// Bound at the API composition root from the <c>Discord</c> configuration
/// section.
/// </summary>
public sealed class DiscordSettings
{
    public const string SectionName = "Discord";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string AuthorizeUrl { get; set; } = "https://discord.com/api/oauth2/authorize";
    public string RedirectUri { get; set; } = string.Empty;
    public string Scope { get; set; } = "identify";
    public int StateTtlSeconds { get; set; } = 600;
    public string SuccessRedirectUrl { get; set; } = "/settings?discord=connected";
    public string FailureRedirectUrl { get; set; } = "/settings?discord=error";
}
