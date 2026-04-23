namespace Skinora.Users.Application.Settings;

/// <summary>
/// Exchanges a Discord OAuth authorization code for the user's profile
/// (id + username). Real HTTP implementation arrives with T80 (08 §5.3);
/// until then <see cref="StubDiscordOAuthClient"/> returns a deterministic
/// profile so tests and dev environments can exercise the bind pipeline
/// without a live Discord app.
/// </summary>
public interface IDiscordOAuthClient
{
    Task<DiscordProfile?> ExchangeAsync(
        string authorizationCode, CancellationToken cancellationToken);
}

public sealed record DiscordProfile(string DiscordUserId, string Username);
