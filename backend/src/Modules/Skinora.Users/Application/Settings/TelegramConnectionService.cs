using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Users.Domain.Entities;

namespace Skinora.Users.Application.Settings;

public sealed class TelegramConnectionService : ITelegramConnectionService
{
    private readonly ITelegramVerificationStore _store;
    private readonly INotificationPreferenceStore _preferences;
    private readonly AppDbContext _db;
    private readonly TelegramSettings _settings;

    public TelegramConnectionService(
        ITelegramVerificationStore store,
        INotificationPreferenceStore preferences,
        AppDbContext db,
        IOptions<TelegramSettings> settings)
    {
        _store = store;
        _preferences = preferences;
        _db = db;
        _settings = settings.Value;
    }

    public async Task<TelegramConnectResult> InitiateAsync(
        Guid userId, CancellationToken cancellationToken)
    {
        var code = GenerateCode();
        var ttl = TimeSpan.FromSeconds(Math.Max(60, _settings.CodeTtlSeconds));
        await _store.IssueAsync(code, userId, ttl, cancellationToken);
        return new TelegramConnectResult(code, _settings.BotUrl, ttl);
    }

    public async Task<TelegramWebhookResult> ProcessWebhookAsync(
        TelegramWebhookPayload payload, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(payload.Code))
            return new TelegramWebhookResult(TelegramWebhookStatus.Ignored, null);

        var userId = await _store.ConsumeAsync(payload.Code, cancellationToken);
        if (userId is null)
            return new TelegramWebhookResult(TelegramWebhookStatus.InvalidOrExpiredCode, null);

        // External id = the Telegram user id (stable, unlike @username). The
        // username is stored alongside for display; if absent we keep only the
        // user id to keep the link functional.
        var externalId = payload.TelegramUserId is long tgId
            ? tgId.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : payload.TelegramUsername?.TrimStart('@');

        if (string.IsNullOrWhiteSpace(externalId))
            return new TelegramWebhookResult(TelegramWebhookStatus.Ignored, null);

        if (await _preferences.ExternalIdInUseByAnotherUserAsync(
            userId.Value, NotificationChannel.TELEGRAM, externalId, cancellationToken))
        {
            return new TelegramWebhookResult(
                TelegramWebhookStatus.AlreadyLinkedToAnotherUser, null);
        }

        await _preferences.UpsertPreferenceAsync(
            userId.Value,
            NotificationChannel.TELEGRAM,
            externalId: externalId,
            isEnabled: true,
            verifiedAt: DateTime.UtcNow,
            cancellationToken);

        return new TelegramWebhookResult(TelegramWebhookStatus.Linked, userId);
    }

    public async Task<TelegramDisconnectResult> DisconnectAsync(
        Guid userId, CancellationToken cancellationToken)
    {
        var user = await _db.Set<User>()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                u => u.Id == userId && !u.IsDeactivated, cancellationToken);

        if (user is null)
            return new TelegramDisconnectResult(TelegramDisconnectStatus.UserNotFound);

        var removed = await _preferences.DeletePreferenceAsync(
            userId, NotificationChannel.TELEGRAM, cancellationToken);

        return new TelegramDisconnectResult(
            removed ? TelegramDisconnectStatus.Removed : TelegramDisconnectStatus.NotConnected);
    }

    private static string GenerateCode()
    {
        // 07 §5.11 — "SKN-" + 6-digit numeric, pasted into the Telegram bot.
        Span<byte> bytes = stackalloc byte[4];
        RandomNumberGenerator.Fill(bytes);
        var value = BitConverter.ToUInt32(bytes) % 1_000_000u;
        return "SKN-" + value.ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }
}
