namespace Skinora.Shared.Discord;

/// <summary>
/// Request DTO for the bot-scope <c>POST /users/@me/channels</c> call
/// (08 §6.2). <paramref name="RecipientId"/> is the Discord user
/// snowflake stored on <see cref="Skinora.Notifications.Domain.Entities.UserNotificationPreference.ExternalId"/>
/// after the OAuth bind (T35).
/// </summary>
public sealed record DiscordCreateDmRequest(string RecipientId);

/// <summary>
/// Result of <c>createDM</c> — the channel id is cached
/// (<see cref="IDiscordDmChannelCache"/>) so subsequent sendMessage
/// calls skip the round-trip.
/// </summary>
public sealed record DiscordDmChannel(string ChannelId);

/// <summary>
/// Request DTO for <c>POST /channels/{channel.id}/messages</c>
/// (08 §6.2). The channel id comes from <see cref="DiscordDmChannel"/>;
/// the content is the rendered template envelope ("*title*\n\nbody").
/// </summary>
public sealed record DiscordSendMessageRequest(string ChannelId, string Content);

/// <summary>
/// Result of <c>sendMessage</c> — the Discord message id is returned
/// for downstream correlation (logging, delivery audit).
/// </summary>
public sealed record DiscordSendMessageResult(string MessageId);

/// <summary>
/// OAuth2 access token exchange request (08 §6.2). Body must be
/// <c>application/x-www-form-urlencoded</c>; the client adds the form
/// fields automatically.
/// </summary>
public sealed record DiscordOAuthExchangeRequest(string Code, string RedirectUri);

/// <summary>
/// Resolved Discord profile after the OAuth2 round-trip — the
/// connection service writes <see cref="DiscordUserId"/> to the
/// preference store and discards the access_token (bot token handles
/// all subsequent calls).
/// </summary>
public sealed record DiscordProfile(string DiscordUserId, string Username);
