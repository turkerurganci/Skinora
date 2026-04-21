namespace Skinora.Auth.Application.SteamAuthentication;

/// <summary>
/// Fetches Steam profile metadata (display name, avatar) via
/// <c>ISteamUser/GetPlayerSummaries/v2</c> — 08 §2.2.
/// </summary>
public interface ISteamProfileClient
{
    /// <summary>
    /// Returns the profile summary for the given SteamID64, or <c>null</c>
    /// when the API key is missing, Steam returns no players, or the call
    /// fails. Callers fall back to a placeholder display name in that case.
    /// </summary>
    Task<SteamPlayerSummary?> GetPlayerSummaryAsync(
        string steamId64, CancellationToken cancellationToken);
}

public sealed record SteamPlayerSummary(
    string SteamId,
    string PersonaName,
    string? AvatarFull,
    DateTime? AccountCreatedAt);
