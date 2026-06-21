using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skinora.Notifications.Application.Channels;
using Skinora.Notifications.Application.Templates;
using Skinora.Notifications.Domain.Entities;
using Skinora.Shared.Discord;
using Skinora.Shared.Enums;
using Skinora.Shared.Notifications;
using Skinora.Shared.Persistence;

namespace Skinora.Notifications.Infrastructure.Channels;

/// <summary>
/// Discord-backed <see cref="INotificationChannelHandler"/> for the
/// <see cref="NotificationChannel.DISCORD"/> channel (T80 — 08 §6.1–§6.5).
/// Replaces the T37 logging stub when the <c>Discord:Provider</c>
/// setting is <c>discord</c>; the stub stays registered for tests and
/// development so CI never reaches Discord.
/// </summary>
/// <remarks>
/// <para>
/// Responsibilities:
/// </para>
/// <list type="number">
///   <item>Resolve the DM channel id — either from
///         <see cref="IDiscordDmChannelCache"/> (steady state) or via
///         <c>POST /users/@me/channels</c> (first send / after
///         invalidation).</item>
///   <item>Escape title + body through
///         <see cref="DiscordMarkdownEscaper"/> and compose the
///         <c>**title**\n\nbody</c> envelope (Discord Markdown's bold
///         marker is <c>**</c>, two asterisks).</item>
///   <item>Call <see cref="IDiscordBotClient.SendMessageAsync"/> with
///         <c>allowed_mentions: { "parse": [] }</c> hard-coded in the
///         bot client.</item>
///   <item>Map Discord-specific exceptions onto the channel
///         abstractions (transient ↔ permanent ↔ retry-after) and
///         auto-disable the preference on documented 403 / 404
///         reasons (08 §6.4).</item>
/// </list>
/// </remarks>
public sealed class DiscordNotificationChannelHandler : INotificationChannelHandler
{
    private readonly IDiscordBotClient _botClient;
    private readonly IDiscordDmChannelCache _dmCache;
    private readonly DiscordSettings _settings;
    private readonly AppDbContext _dbContext;
    private readonly ILogger<DiscordNotificationChannelHandler> _logger;

    public DiscordNotificationChannelHandler(
        IDiscordBotClient botClient,
        IDiscordDmChannelCache dmCache,
        IOptions<DiscordSettings> settings,
        AppDbContext dbContext,
        ILogger<DiscordNotificationChannelHandler> logger)
    {
        _botClient = botClient;
        _dmCache = dmCache;
        _settings = settings.Value;
        _dbContext = dbContext;
        _logger = logger;
    }

    public NotificationChannel Channel => NotificationChannel.DISCORD;

    public async Task SendAsync(
        string targetExternalId,
        RenderedNotificationTemplate rendered,
        CancellationToken cancellationToken)
    {
        var content = FormatMessage(rendered);

        var channelId = await _dmCache.GetAsync(targetExternalId, cancellationToken);
        var cacheHit = !string.IsNullOrEmpty(channelId);

        try
        {
            if (!cacheHit)
            {
                channelId = await EnsureDmChannelAsync(
                    targetExternalId, cancellationToken);
            }

            try
            {
                var result = await _botClient.SendMessageAsync(
                    new DiscordSendMessageRequest(channelId!, content),
                    cancellationToken);

                _logger.LogInformation(
                    "Discord DM delivered — target={Target} messageId={MessageId} cacheHit={CacheHit}",
                    TargetExternalIdMasker.Mask(Channel, targetExternalId),
                    result.MessageId,
                    cacheHit);
            }
            catch (DiscordPermanentException ex) when (ex.HttpStatusCode == 404 && cacheHit)
            {
                // The cached channel id was stale (channel deleted
                // server-side). Drop the cache entry, re-open the DM
                // and resend exactly once. A second 404 falls through
                // to the permanent path below.
                _logger.LogWarning(
                    "Discord DM 404 with cached channel id — invalidating and retrying once. target={Target}",
                    TargetExternalIdMasker.Mask(Channel, targetExternalId));

                await _dmCache.ForgetAsync(targetExternalId, cancellationToken);
                channelId = await EnsureDmChannelAsync(
                    targetExternalId, cancellationToken);

                var result = await _botClient.SendMessageAsync(
                    new DiscordSendMessageRequest(channelId!, content),
                    cancellationToken);

                _logger.LogInformation(
                    "Discord DM delivered after cache invalidation — target={Target} messageId={MessageId}",
                    TargetExternalIdMasker.Mask(Channel, targetExternalId),
                    result.MessageId);
            }
        }
        catch (DiscordTransientException ex)
        {
            // The bot client already registered any retry-after with
            // the rate limiter; just translate to the channel exception.
            throw new TransientChannelDeliveryException(
                $"Discord send failed transiently ({ex.HttpStatusCode}/{ex.DiscordErrorCode}): {ex.Message}",
                ex);
        }
        catch (DiscordUnauthorizedException ex)
        {
            // 401 indicates the bot token was revoked / rotated. Do
            // not disable the preference — the user did nothing wrong.
            // The admin alert sink ([T37]) surfaces the operational
            // fault; the row goes to FAILED via the permanent path.
            throw new PermanentChannelDeliveryException(
                $"Discord bot token rejected ({ex.Message}); admin attention required.",
                ex);
        }
        catch (DiscordForbiddenException ex)
        {
            await DisablePreferenceAsync(targetExternalId, ex.Reason.ToString(), cancellationToken);

            throw new PermanentChannelDeliveryException(
                $"Discord forbidden ({ex.Reason}/{ex.DiscordErrorCode}): {ex.Message}; preference disabled.",
                ex);
        }
        catch (DiscordPermanentException ex)
        {
            // 404 / 400 on /messages with no cached channel id means
            // the user/channel is gone — disable the preference so the
            // retry pipeline doesn't keep firing into the void.
            await _dmCache.ForgetAsync(targetExternalId, cancellationToken);
            await DisablePreferenceAsync(targetExternalId, "PermanentRejection", cancellationToken);

            throw new PermanentChannelDeliveryException(
                $"Discord rejected message permanently ({ex.HttpStatusCode}/{ex.DiscordErrorCode}): {ex.Message}",
                ex);
        }
    }

    private async Task<string> EnsureDmChannelAsync(
        string discordUserId, CancellationToken cancellationToken)
    {
        var channel = await _botClient.CreateDmAsync(
            new DiscordCreateDmRequest(discordUserId), cancellationToken);

        if (string.IsNullOrWhiteSpace(channel.ChannelId))
        {
            throw new DiscordPermanentException(
                "Discord createDM returned 200 with no channel id.",
                httpStatusCode: 200);
        }

        await _dmCache.SetAsync(
            discordUserId,
            channel.ChannelId,
            TimeSpan.FromHours(Math.Max(1, _settings.DmChannelCacheTtlHours)),
            cancellationToken);

        return channel.ChannelId;
    }

    // Discord's hard cap on message content (08 §6.2). Over-length content is
    // truncated raw-then-escaped (never the escaped string) so a markdown escape
    // pair is never split — otherwise the 400 would auto-disable the preference.
    private const int MaxMessageLength = 2000;

    private static string FormatMessage(RenderedNotificationTemplate rendered) =>
        BoldHeaderMessageComposer.Compose(
            rendered.Title,
            rendered.Body,
            MaxMessageLength,
            DiscordMarkdownEscaper.Escape,
            boldOpen: "**",
            boldClose: "**");

    private async Task DisablePreferenceAsync(
        string discordUserId,
        string reason,
        CancellationToken cancellationToken)
    {
        var preference = await _dbContext.Set<UserNotificationPreference>()
            .Where(p => p.Channel == NotificationChannel.DISCORD
                        && p.ExternalId == discordUserId
                        && !p.IsDeleted)
            .FirstOrDefaultAsync(cancellationToken);

        if (preference is null || !preference.IsEnabled)
        {
            return;
        }

        preference.IsEnabled = false;
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogWarning(
            "Discord preference auto-disabled — target={Target} reason={Reason}",
            TargetExternalIdMasker.Mask(NotificationChannel.DISCORD, discordUserId),
            reason);
    }
}
