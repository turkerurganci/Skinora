namespace Skinora.Auth.Application.SteamAuthentication;

/// <summary>
/// Validates a Steam OpenID callback assertion by POSTing
/// <c>openid.mode=check_authentication</c> back to Steam — 08 §2.1.
/// </summary>
public interface ISteamOpenIdValidator
{
    Task<SteamOpenIdValidationResult> ValidateAsync(
        IReadOnlyDictionary<string, string> callbackParameters,
        CancellationToken cancellationToken);
}

public sealed record SteamOpenIdValidationResult(bool IsValid, string? SteamId64, string? FailureReason)
{
    public static SteamOpenIdValidationResult Success(string steamId64) => new(true, steamId64, null);
    public static SteamOpenIdValidationResult Failure(string reason) => new(false, null, reason);
}
