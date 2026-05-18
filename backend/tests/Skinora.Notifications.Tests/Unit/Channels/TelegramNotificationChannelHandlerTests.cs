using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.EntityFrameworkCore;
using Skinora.Notifications.Application.Channels;
using Skinora.Notifications.Application.Templates;
using Skinora.Notifications.Domain.Entities;
using Skinora.Notifications.Infrastructure.Channels;
using Skinora.Notifications.Infrastructure.Persistence;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Shared.Telegram;
using Skinora.Shared.Tests.Integration;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.Notifications.Tests.Unit.Channels;

/// <summary>
/// Coverage for the T79 Telegram channel handler — exception → channel
/// exception mapping, auto-disable of the
/// <see cref="UserNotificationPreference"/> on documented 403 reasons +
/// permanent 400 / chat-not-found, and the retry-after handshake with
/// the rate limiter.
/// </summary>
/// <remarks>
/// Lives under <c>Unit/</c> by convention (one-class focus) but inherits
/// the shared <see cref="IntegrationTestBase"/> because SQL Server is
/// the only provider with full support for the filtered indexes /
/// CHECK constraints that the preference entity declares (the SQLite
/// equivalent rejects them at <c>EnsureCreated</c>, T11.1 retrospective).
/// </remarks>
public class TelegramNotificationChannelHandlerTests : IntegrationTestBase
{
    static TelegramNotificationChannelHandlerTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        NotificationsModuleDbRegistration.RegisterNotificationsModule();
    }

    [Fact]
    public async Task SendAsync_OkResult_CompletesAndDoesNotTouchPreference()
    {
        var chatId = "11111";
        await SeedPreferenceAsync(chatId, isEnabled: true);

        var botClient = new FakeBotClient { Response = new TelegramSendMessageResult(1) };
        var sut = BuildHandler(botClient);

        await sut.SendAsync(
            chatId,
            new RenderedNotificationTemplate("Title", "Body"),
            CancellationToken.None);

        Assert.True(await PreferenceEnabledAsync(chatId));
        Assert.Single(botClient.SendCalls);
        Assert.Contains("*Title*\n\nBody", botClient.SendCalls[0].Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendAsync_ForbiddenBotBlocked_DisablesPreferenceAndThrowsPermanent()
    {
        var chatId = "22222";
        await SeedPreferenceAsync(chatId, isEnabled: true);

        var botClient = new FakeBotClient
        {
            Exception = new TelegramForbiddenException(
                TelegramForbiddenReason.BotBlockedByUser,
                "Forbidden: bot was blocked by the user"),
        };
        var sut = BuildHandler(botClient);

        await Assert.ThrowsAsync<PermanentChannelDeliveryException>(() =>
            sut.SendAsync(
                chatId,
                new RenderedNotificationTemplate("t", "b"),
                CancellationToken.None));

        Assert.False(await PreferenceEnabledAsync(chatId));
    }

    [Fact]
    public async Task SendAsync_Permanent400_DisablesPreferenceAndThrowsPermanent()
    {
        var chatId = "33333";
        await SeedPreferenceAsync(chatId, isEnabled: true);

        var botClient = new FakeBotClient
        {
            Exception = new TelegramPermanentException(
                "Bad Request: chat not found",
                httpStatusCode: 400),
        };
        var sut = BuildHandler(botClient);

        await Assert.ThrowsAsync<PermanentChannelDeliveryException>(() =>
            sut.SendAsync(
                chatId,
                new RenderedNotificationTemplate("t", "b"),
                CancellationToken.None));

        Assert.False(await PreferenceEnabledAsync(chatId));
    }

    [Fact]
    public async Task SendAsync_Transient429_RegistersRetryAfterAndThrowsTransient()
    {
        var chatId = "44444";
        await SeedPreferenceAsync(chatId, isEnabled: true);

        var botClient = new FakeBotClient
        {
            Exception = new TelegramTransientException(
                "Too many",
                httpStatusCode: 429,
                retryAfterSeconds: 7),
        };
        var rateLimiter = new SpyRateLimiter();
        var sut = BuildHandler(botClient, rateLimiter);

        await Assert.ThrowsAsync<TransientChannelDeliveryException>(() =>
            sut.SendAsync(
                chatId,
                new RenderedNotificationTemplate("t", "b"),
                CancellationToken.None));

        Assert.True(await PreferenceEnabledAsync(chatId));
        Assert.Single(rateLimiter.RetryAfterCalls);
        Assert.Equal((chatId, 7), rateLimiter.RetryAfterCalls[0]);
    }

    [Fact]
    public async Task SendAsync_Transient5xx_DoesNotDisablePreference()
    {
        var chatId = "55555";
        await SeedPreferenceAsync(chatId, isEnabled: true);

        var botClient = new FakeBotClient
        {
            Exception = new TelegramTransientException(
                "Service Unavailable",
                httpStatusCode: 503),
        };
        var sut = BuildHandler(botClient);

        await Assert.ThrowsAsync<TransientChannelDeliveryException>(() =>
            sut.SendAsync(
                chatId,
                new RenderedNotificationTemplate("t", "b"),
                CancellationToken.None));

        Assert.True(await PreferenceEnabledAsync(chatId));
    }

    private TelegramNotificationChannelHandler BuildHandler(
        ITelegramBotClient botClient,
        ITelegramRateLimiter? rateLimiter = null)
    {
        var limiter = rateLimiter ?? new SpyRateLimiter();

        return new TelegramNotificationChannelHandler(
            botClient,
            limiter,
            Context,
            NullLogger<TelegramNotificationChannelHandler>.Instance);
    }

    private async Task SeedPreferenceAsync(string chatId, bool isEnabled)
    {
        var entity = new UserNotificationPreference
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Channel = NotificationChannel.TELEGRAM,
            ExternalId = chatId,
            IsEnabled = isEnabled,
            VerifiedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
        };
        Context.Set<UserNotificationPreference>().Add(entity);
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();
    }

    private async Task<bool> PreferenceEnabledAsync(string chatId)
    {
        var pref = await Context.Set<UserNotificationPreference>()
            .AsNoTracking()
            .Where(p => p.Channel == NotificationChannel.TELEGRAM && p.ExternalId == chatId)
            .FirstAsync();
        return pref.IsEnabled;
    }

    private sealed class FakeBotClient : ITelegramBotClient
    {
        public List<TelegramSendMessageRequest> SendCalls { get; } = new();
        public Exception? Exception { get; set; }
        public TelegramSendMessageResult Response { get; set; } = new(0);

        public Task<TelegramSendMessageResult> SendMessageAsync(
            TelegramSendMessageRequest request, CancellationToken cancellationToken)
        {
            SendCalls.Add(request);
            if (Exception is not null) throw Exception;
            return Task.FromResult(Response);
        }

        public Task SetWebhookAsync(
            TelegramSetWebhookRequest request, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class SpyRateLimiter : ITelegramRateLimiter
    {
        public List<(string chatId, int seconds)> RetryAfterCalls { get; } = new();

        public Task WaitAsync(string chatId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public void RegisterRetryAfter(string chatId, int seconds)
            => RetryAfterCalls.Add((chatId, seconds));
    }
}
