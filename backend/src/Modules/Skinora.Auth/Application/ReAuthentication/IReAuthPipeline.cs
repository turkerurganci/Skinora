namespace Skinora.Auth.Application.ReAuthentication;

/// <summary>
/// Orchestrates the Steam re-verify flow — 07 §4.6–§4.7.
/// </summary>
public interface IReAuthPipeline
{
    /// <summary>
    /// A5 — builds the Steam OpenID URL and returns the protected state string
    /// to persist in the client cookie.
    /// </summary>
    ReAuthInitiation Initiate(Guid userId, string steamId, string? returnUrl);

    /// <summary>
    /// A6 — validates the Steam assertion, matches it to the cookie state,
    /// issues a single-use reAuthToken, and returns the outcome.
    /// </summary>
    Task<ReAuthOutcome> HandleCallbackAsync(
        IReadOnlyDictionary<string, string> callbackParameters,
        string? protectedState,
        CancellationToken cancellationToken);
}

public sealed record ReAuthInitiation(string SteamAuthUrl, string ProtectedState);

public abstract record ReAuthOutcome
{
    public sealed record Success(string ReAuthToken, string ReturnUrl) : ReAuthOutcome;
    public sealed record SteamIdMismatch : ReAuthOutcome;
    public sealed record AuthFailed(string Reason) : ReAuthOutcome;
    public sealed record StateMissing : ReAuthOutcome;
}
