namespace Skinora.Shared.Discord;

/// <summary>
/// Caches the bot ⇄ user DM channel id (08 §6.3 — "DM channel ID cache:
/// Redis"). Discord createDM is idempotent (returns the existing
/// channel for the same recipient) but every call still counts against
/// the per-bucket rate-limit budget; caching the channel id removes one
/// round-trip per outbound message in the steady state.
/// </summary>
/// <remarks>
/// <para>
/// Keys are scoped per Discord user snowflake. The cache is best-effort
/// — if the read returns null (or Redis is down), the channel handler
/// falls back to <see cref="IDiscordBotClient.CreateDmAsync"/> on every
/// send. Stale entries (channel deleted server-side) surface as 404 on
/// <see cref="IDiscordBotClient.SendMessageAsync"/>; the handler then
/// invalidates the cache via <see cref="ForgetAsync"/> and retries the
/// createDM step.
/// </para>
/// </remarks>
public interface IDiscordDmChannelCache
{
    Task<string?> GetAsync(string discordUserId, CancellationToken cancellationToken);

    Task SetAsync(
        string discordUserId, string channelId, TimeSpan ttl, CancellationToken cancellationToken);

    Task ForgetAsync(string discordUserId, CancellationToken cancellationToken);
}
