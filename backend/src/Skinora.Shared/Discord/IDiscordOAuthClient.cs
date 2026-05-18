namespace Skinora.Shared.Discord;

/// <summary>
/// Exchanges a Discord OAuth2 authorization code for the user's
/// identity (T80 — 08 §6.2 "OAuth2 callback akışı"). Two-step:
/// <list type="number">
///   <item><c>POST /oauth2/token</c> — code → access_token
///         (<c>application/x-www-form-urlencoded</c> body).</item>
///   <item><c>GET /users/@me</c> with the bearer token →
///         <see cref="DiscordProfile"/>.</item>
/// </list>
/// </summary>
/// <remarks>
/// <para>
/// Throws <see cref="DiscordOAuthExchangeException"/> for failures the
/// connection service must distinguish (invalid_grant vs transport
/// error). The access_token is never returned to callers — it's used
/// inline for the <c>/users/@me</c> lookup and then dropped (bot token
/// handles every subsequent API call).
/// </para>
/// <para>
/// Lives in <c>Skinora.Shared.Discord</c> alongside the bot transport
/// so the OAuth + bot DM concerns share a single configuration
/// surface (<see cref="DiscordSettings"/>).
/// </para>
/// </remarks>
public interface IDiscordOAuthClient
{
    Task<DiscordProfile?> ExchangeAsync(
        string authorizationCode, CancellationToken cancellationToken);
}
