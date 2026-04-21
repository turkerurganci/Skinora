namespace Skinora.Auth.Application.SteamAuthentication;

/// <summary>
/// End-to-end orchestration for <c>GET /auth/steam/callback</c> — runs the
/// deterministic check pipeline from 03 §2.1 / §11a, 07 §4.3 in order:
/// assertion validation → geo-block → sanctions → profile fetch → age gate →
/// user upsert → ban check → token issuance → audit.
/// </summary>
public interface ISteamAuthenticationPipeline
{
    Task<AuthenticationOutcome> ExecuteAsync(
        IReadOnlyDictionary<string, string> callbackParameters,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken);
}
