using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using Skinora.Notifications.Application.Channels;
using Skinora.Notifications.Application.Templates;
using Skinora.Notifications.Domain.Entities;
using Skinora.Notifications.Infrastructure.Channels;
using Skinora.Notifications.Infrastructure.Persistence;
using Skinora.Shared.Discord;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Shared.Tests.Integration;
using Skinora.Users.Domain.Entities;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.Notifications.Tests.Unit.Channels;

/// <summary>
/// Coverage for the T80 Discord channel handler — exception mapping,
/// DM channel cache happy path + stale-cache recovery, 403 reason →
/// preference auto-disable, and the rate-limiter retry-after handshake
/// (08 §6.4).
/// </summary>
public class DiscordNotificationChannelHandlerTests : IntegrationTestBase
{
    static DiscordNotificationChannelHandlerTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        NotificationsModuleDbRegistration.RegisterNotificationsModule();
    }

    [Fact]
    public async Task SendAsync_NoCacheEntry_CreatesDmThenSends()
    {
        var discordUserId = "u1";
        await SeedPreferenceAsync(discordUserId, isEnabled: true);

        var bot = new FakeBotClient
        {
            CreateDmResponse = new DiscordDmChannel("chan-1"),
            SendResponse = new DiscordSendMessageResult("msg-1"),
        };
        var cache = new InMemoryDiscordDmChannelCache();
        var sut = BuildHandler(bot, cache);

        await sut.SendAsync(
            discordUserId,
            new RenderedNotificationTemplate("Title", "Body"),
            CancellationToken.None);

        Assert.Single(bot.CreateDmCalls);
        Assert.Single(bot.SendCalls);
        Assert.Equal("chan-1", bot.SendCalls[0].ChannelId);
        Assert.Contains("**Title**", bot.SendCalls[0].Content, StringComparison.Ordinal);
        Assert.Contains("Body", bot.SendCalls[0].Content, StringComparison.Ordinal);

        var cached = await cache.GetAsync(discordUserId, CancellationToken.None);
        Assert.Equal("chan-1", cached);
        Assert.True(await PreferenceEnabledAsync(discordUserId));
    }

    [Fact]
    public async Task SendAsync_CacheHit_SkipsCreateDm()
    {
        var discordUserId = "u2";
        await SeedPreferenceAsync(discordUserId, isEnabled: true);

        var cache = new InMemoryDiscordDmChannelCache();
        await cache.SetAsync(discordUserId, "chan-cached", TimeSpan.FromHours(1), CancellationToken.None);

        var bot = new FakeBotClient
        {
            SendResponse = new DiscordSendMessageResult("msg-cached"),
        };
        var sut = BuildHandler(bot, cache);

        await sut.SendAsync(
            discordUserId,
            new RenderedNotificationTemplate("t", "b"),
            CancellationToken.None);

        Assert.Empty(bot.CreateDmCalls);
        Assert.Single(bot.SendCalls);
        Assert.Equal("chan-cached", bot.SendCalls[0].ChannelId);
    }

    [Fact]
    public async Task SendAsync_404WithCachedChannel_InvalidatesAndRetriesOnce()
    {
        var discordUserId = "u3";
        await SeedPreferenceAsync(discordUserId, isEnabled: true);

        var cache = new InMemoryDiscordDmChannelCache();
        await cache.SetAsync(discordUserId, "stale-chan", TimeSpan.FromHours(1), CancellationToken.None);

        var bot = new FakeBotClient
        {
            CreateDmResponse = new DiscordDmChannel("fresh-chan"),
            SendQueue = new Queue<Func<DiscordSendMessageResult>>(new Func<DiscordSendMessageResult>[]
            {
                () => throw new DiscordPermanentException("Unknown Channel", httpStatusCode: 404),
                () => new DiscordSendMessageResult("msg-recovered"),
            }),
        };
        var sut = BuildHandler(bot, cache);

        await sut.SendAsync(
            discordUserId,
            new RenderedNotificationTemplate("t", "b"),
            CancellationToken.None);

        Assert.Equal(2, bot.SendCalls.Count);
        Assert.Equal("stale-chan", bot.SendCalls[0].ChannelId);
        Assert.Equal("fresh-chan", bot.SendCalls[1].ChannelId);
        Assert.True(await PreferenceEnabledAsync(discordUserId));
    }

    [Fact]
    public async Task SendAsync_403MutualGuildRequired_DisablesPreferenceAndThrowsPermanent()
    {
        var discordUserId = "u4";
        await SeedPreferenceAsync(discordUserId, isEnabled: true);

        var bot = new FakeBotClient
        {
            CreateDmException = new DiscordForbiddenException(
                DiscordForbiddenReason.MutualGuildRequired,
                "Cannot create DM"),
        };
        var sut = BuildHandler(bot, new InMemoryDiscordDmChannelCache());

        await Assert.ThrowsAsync<PermanentChannelDeliveryException>(() =>
            sut.SendAsync(
                discordUserId,
                new RenderedNotificationTemplate("t", "b"),
                CancellationToken.None));

        Assert.False(await PreferenceEnabledAsync(discordUserId));
    }

    [Fact]
    public async Task SendAsync_403DmClosed_DisablesPreferenceAndThrowsPermanent()
    {
        var discordUserId = "u5";
        await SeedPreferenceAsync(discordUserId, isEnabled: true);

        var bot = new FakeBotClient
        {
            CreateDmResponse = new DiscordDmChannel("chan-5"),
            SendException = new DiscordForbiddenException(
                DiscordForbiddenReason.DmClosed,
                "Cannot send messages to this user",
                discordErrorCode: 50007),
        };
        var sut = BuildHandler(bot, new InMemoryDiscordDmChannelCache());

        await Assert.ThrowsAsync<PermanentChannelDeliveryException>(() =>
            sut.SendAsync(
                discordUserId,
                new RenderedNotificationTemplate("t", "b"),
                CancellationToken.None));

        Assert.False(await PreferenceEnabledAsync(discordUserId));
    }

    [Fact]
    public async Task SendAsync_401Unauthorized_DoesNotDisablePreference()
    {
        var discordUserId = "u6";
        await SeedPreferenceAsync(discordUserId, isEnabled: true);

        var bot = new FakeBotClient
        {
            CreateDmException = new DiscordUnauthorizedException("Bot token revoked"),
        };
        var sut = BuildHandler(bot, new InMemoryDiscordDmChannelCache());

        await Assert.ThrowsAsync<PermanentChannelDeliveryException>(() =>
            sut.SendAsync(
                discordUserId,
                new RenderedNotificationTemplate("t", "b"),
                CancellationToken.None));

        // 401 is an operational fault on Skinora's side — the user did
        // nothing wrong, so the preference must stay enabled while the
        // admin alert sink raises the rotation request.
        Assert.True(await PreferenceEnabledAsync(discordUserId));
    }

    [Fact]
    public async Task SendAsync_Transient429_ThrowsTransientAndKeepsPreference()
    {
        var discordUserId = "u7";
        await SeedPreferenceAsync(discordUserId, isEnabled: true);

        var bot = new FakeBotClient
        {
            CreateDmResponse = new DiscordDmChannel("chan-7"),
            SendException = new DiscordTransientException(
                "Rate limited",
                httpStatusCode: 429,
                retryAfterSeconds: 2.0,
                bucket: "sendMessage:chan-7"),
        };
        var sut = BuildHandler(bot, new InMemoryDiscordDmChannelCache());

        await Assert.ThrowsAsync<TransientChannelDeliveryException>(() =>
            sut.SendAsync(
                discordUserId,
                new RenderedNotificationTemplate("t", "b"),
                CancellationToken.None));

        Assert.True(await PreferenceEnabledAsync(discordUserId));
    }

    [Fact]
    public async Task SendAsync_Transient5xx_DoesNotDisablePreference()
    {
        var discordUserId = "u8";
        await SeedPreferenceAsync(discordUserId, isEnabled: true);

        var bot = new FakeBotClient
        {
            CreateDmResponse = new DiscordDmChannel("chan-8"),
            SendException = new DiscordTransientException(
                "Service Unavailable",
                httpStatusCode: 503),
        };
        var sut = BuildHandler(bot, new InMemoryDiscordDmChannelCache());

        await Assert.ThrowsAsync<TransientChannelDeliveryException>(() =>
            sut.SendAsync(
                discordUserId,
                new RenderedNotificationTemplate("t", "b"),
                CancellationToken.None));

        Assert.True(await PreferenceEnabledAsync(discordUserId));
    }

    [Fact]
    public async Task SendAsync_404WithoutCachedChannel_DisablesPreference()
    {
        var discordUserId = "u9";
        await SeedPreferenceAsync(discordUserId, isEnabled: true);

        var bot = new FakeBotClient
        {
            CreateDmResponse = new DiscordDmChannel("chan-9"),
            SendException = new DiscordPermanentException(
                "Unknown Channel", httpStatusCode: 404),
        };
        var sut = BuildHandler(bot, new InMemoryDiscordDmChannelCache());

        await Assert.ThrowsAsync<PermanentChannelDeliveryException>(() =>
            sut.SendAsync(
                discordUserId,
                new RenderedNotificationTemplate("t", "b"),
                CancellationToken.None));

        Assert.False(await PreferenceEnabledAsync(discordUserId));
    }

    [Fact]
    public async Task SendAsync_OverLongBody_TruncatesWithinLimit_KeepsPreferenceEnabled()
    {
        var discordUserId = "u10";
        await SeedPreferenceAsync(discordUserId, isEnabled: true);

        var bot = new FakeBotClient
        {
            CreateDmResponse = new DiscordDmChannel("chan-10"),
            SendResponse = new DiscordSendMessageResult("msg-10"),
        };
        var sut = BuildHandler(bot, new InMemoryDiscordDmChannelCache());

        // A 5000-char all-reserved body escapes to ~10000 — well over Discord's
        // 2000 cap. Without the truncation guard this would 400 → permanent failure
        // → auto-disable. The guard keeps the payload within the limit and the
        // preference enabled.
        await sut.SendAsync(
            discordUserId,
            new RenderedNotificationTemplate("Title", new string('*', 5000)),
            CancellationToken.None);

        Assert.Single(bot.SendCalls);
        Assert.True(
            bot.SendCalls[0].Content.Length <= 2000,
            $"payload length {bot.SendCalls[0].Content.Length} exceeds Discord's 2000 limit");
        Assert.True(await PreferenceEnabledAsync(discordUserId));
    }

    private DiscordNotificationChannelHandler BuildHandler(
        IDiscordBotClient botClient, IDiscordDmChannelCache cache)
    {
        var settings = Options.Create(new DiscordSettings
        {
            DmChannelCacheTtlHours = 24,
        });

        return new DiscordNotificationChannelHandler(
            botClient,
            cache,
            settings,
            Context,
            NullLogger<DiscordNotificationChannelHandler>.Instance);
    }

    private async Task SeedPreferenceAsync(string discordUserId, bool isEnabled)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            SteamId = "76561198" + Random.Shared.Next(100_000_000, 999_999_999)
                .ToString(System.Globalization.CultureInfo.InvariantCulture),
            SteamDisplayName = "Discord Test " + discordUserId,
            PreferredLanguage = "en",
        };
        Context.Set<User>().Add(user);
        await Context.SaveChangesAsync();

        var entity = new UserNotificationPreference
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Channel = NotificationChannel.DISCORD,
            ExternalId = discordUserId,
            IsEnabled = isEnabled,
            VerifiedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
        };
        Context.Set<UserNotificationPreference>().Add(entity);
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();
    }

    private async Task<bool> PreferenceEnabledAsync(string discordUserId)
    {
        var pref = await Context.Set<UserNotificationPreference>()
            .AsNoTracking()
            .Where(p => p.Channel == NotificationChannel.DISCORD && p.ExternalId == discordUserId)
            .FirstAsync();
        return pref.IsEnabled;
    }

    private sealed class FakeBotClient : IDiscordBotClient
    {
        public List<DiscordCreateDmRequest> CreateDmCalls { get; } = new();
        public List<DiscordSendMessageRequest> SendCalls { get; } = new();
        public DiscordDmChannel? CreateDmResponse { get; set; }
        public Exception? CreateDmException { get; set; }
        public DiscordSendMessageResult? SendResponse { get; set; }
        public Exception? SendException { get; set; }
        public Queue<Func<DiscordSendMessageResult>>? SendQueue { get; set; }

        public Task<DiscordDmChannel> CreateDmAsync(
            DiscordCreateDmRequest request, CancellationToken cancellationToken)
        {
            CreateDmCalls.Add(request);
            if (CreateDmException is not null) throw CreateDmException;
            return Task.FromResult(
                CreateDmResponse ?? throw new InvalidOperationException("No CreateDM response configured"));
        }

        public Task<DiscordSendMessageResult> SendMessageAsync(
            DiscordSendMessageRequest request, CancellationToken cancellationToken)
        {
            SendCalls.Add(request);

            if (SendQueue is { Count: > 0 } queue)
            {
                return Task.FromResult(queue.Dequeue()());
            }

            if (SendException is not null) throw SendException;

            return Task.FromResult(
                SendResponse ?? throw new InvalidOperationException("No Send response configured"));
        }
    }
}
