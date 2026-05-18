namespace Skinora.Shared.Discord;

/// <summary>
/// Low-level HTTP transport for the Discord bot-scope endpoints
/// (<c>POST /users/@me/channels</c> + <c>POST /channels/{id}/messages</c>)
/// — 08 §6.2. Independent of who is calling it; the notification
/// channel handler is the only runtime consumer today, but the
/// interface is small enough that an admin diagnostic CLI can reuse it
/// without paying the channel-handler wiring cost.
/// </summary>
/// <remarks>
/// <para>
/// Throws either <see cref="DiscordTransientException"/> (5xx, 429,
/// network / transport), <see cref="DiscordPermanentException"/> (400,
/// 404), <see cref="DiscordUnauthorizedException"/> (401) or
/// <see cref="DiscordForbiddenException"/> (403). Successful sends
/// return the Discord message id for downstream correlation.
/// </para>
/// </remarks>
public interface IDiscordBotClient
{
    /// <summary>
    /// <c>POST /users/@me/channels</c> — opens (or returns the cached)
    /// DM channel between the bot and <paramref name="request"/>'s
    /// recipient. A 403 here means mutual-guild prerequisite (08 §6.4
    /// row 2 — <see cref="DiscordForbiddenReason.MutualGuildRequired"/>).
    /// </summary>
    Task<DiscordDmChannel> CreateDmAsync(
        DiscordCreateDmRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// <c>POST /channels/{channel.id}/messages</c> — pushes a DM with
    /// <c>allowed_mentions: { "parse": [] }</c> hard-coded so item
    /// names / usernames cannot trigger unintended pings (08 §6.2).
    /// A 403 here means the user has closed DMs (08 §6.4 row 1 —
    /// <see cref="DiscordForbiddenReason.DmClosed"/>).
    /// </summary>
    Task<DiscordSendMessageResult> SendMessageAsync(
        DiscordSendMessageRequest request, CancellationToken cancellationToken);
}
