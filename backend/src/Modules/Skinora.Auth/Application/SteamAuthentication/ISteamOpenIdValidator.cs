namespace Skinora.Auth.Application.SteamAuthentication;

/// <summary>
/// Validates a Steam OpenID callback assertion by POSTing
/// <c>openid.mode=check_authentication</c> back to Steam — 08 §2.1.
/// </summary>
public interface ISteamOpenIdValidator
{
    /// <summary>
    /// Validates an assertion bound to the configured login <c>return_to</c> URL.
    /// </summary>
    Task<SteamOpenIdValidationResult> ValidateAsync(
        IReadOnlyDictionary<string, string> callbackParameters,
        CancellationToken cancellationToken);

    /// <summary>
    /// Validates an assertion bound to an explicit <c>return_to</c> URL — used
    /// by the re-verify flow (07 §4.7) so a login assertion cannot be replayed
    /// against the re-verify callback and vice versa.
    /// </summary>
    Task<SteamOpenIdValidationResult> ValidateAsync(
        IReadOnlyDictionary<string, string> callbackParameters,
        string expectedReturnTo,
        CancellationToken cancellationToken);
}

public sealed record SteamOpenIdValidationResult(bool IsValid, string? SteamId64, string? FailureReason)
{
    public static SteamOpenIdValidationResult Success(string steamId64) => new(true, steamId64, null);
    public static SteamOpenIdValidationResult Failure(string reason) => new(false, null, reason);
}
