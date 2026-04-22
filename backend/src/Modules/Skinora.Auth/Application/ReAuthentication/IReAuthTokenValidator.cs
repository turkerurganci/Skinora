namespace Skinora.Auth.Application.ReAuthentication;

/// <summary>
/// Consumer-side validator for the <c>X-ReAuth-Token</c> header (07 §4.7).
/// Downstream security-critical flows (wallet change — T34) call
/// <see cref="ValidateAsync"/> to redeem the token, which invalidates it in
/// one atomic step. Binding the redeemed payload to the current session is
/// the caller's responsibility.
/// </summary>
public interface IReAuthTokenValidator
{
    /// <summary>
    /// Redeems the token. Returns the bound payload on success, or <c>null</c>
    /// when the token is missing, unknown, expired, or already consumed.
    /// </summary>
    Task<ReAuthTokenPayload?> ValidateAsync(string? tokenHeaderValue, CancellationToken cancellationToken);
}

public sealed class ReAuthTokenValidator : IReAuthTokenValidator
{
    private readonly IReAuthTokenStore _store;

    public ReAuthTokenValidator(IReAuthTokenStore store)
    {
        _store = store;
    }

    public Task<ReAuthTokenPayload?> ValidateAsync(string? tokenHeaderValue, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tokenHeaderValue))
            return Task.FromResult<ReAuthTokenPayload?>(null);

        return _store.ConsumeAsync(tokenHeaderValue, cancellationToken);
    }
}
