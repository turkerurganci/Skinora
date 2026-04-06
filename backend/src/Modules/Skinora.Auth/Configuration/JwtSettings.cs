namespace Skinora.Auth.Configuration;

public sealed class JwtSettings
{
    public const string SectionName = "Jwt";

    public required string Secret { get; init; }
    public required string Issuer { get; init; }
    public required string Audience { get; init; }
    public int AccessTokenExpiryMinutes { get; init; } = 15;
    public int RefreshTokenExpiryDays { get; init; } = 7;

    /// <summary>
    /// Previous signing key kept during rotation. Tokens signed with this key
    /// are still accepted until it is removed from configuration.
    /// </summary>
    public string? PreviousSecret { get; init; }
}
