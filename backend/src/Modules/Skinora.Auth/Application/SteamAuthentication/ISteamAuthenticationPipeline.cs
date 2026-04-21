namespace Skinora.Auth.Application.SteamAuthentication;

/// <summary>
/// End-to-end orchestration for <c>GET /auth/steam/callback</c> — runs the
/// deterministic check pipeline from 03 §2.1 and 07 §4.3 in order:
/// assertion validation → geo-block → suspended account → sanctions →
/// user upsert → token issuance → audit.
/// </summary>
public interface ISteamAuthenticationPipeline
{
    Task<AuthenticationOutcome> ExecuteAsync(
        IReadOnlyDictionary<string, string> callbackParameters,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken);
}
