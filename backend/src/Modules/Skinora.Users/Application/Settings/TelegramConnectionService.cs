using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Shared.Telegram;
using Skinora.Users.Domain.Entities;

namespace Skinora.Users.Application.Settings;

public sealed class TelegramConnectionService : ITelegramConnectionService
{
    /// <summary>
    /// 08 §5.1 — connect-code TTL floor. The integration tests configure
    /// values shorter than the spec'd 10 minutes, so the service clamps
    /// any value below this floor to keep production-shaped behaviour
    /// without breaking the test rigs.
    /// </summary>
    private static readonly TimeSpan MinCodeTtl = TimeSpan.FromSeconds(60);

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
        var ttl = ResolveTtl();
        await _store.IssueAsync(code, userId, ttl, cancellationToken);
        return new TelegramConnectResult(code, _settings.BotUrl, ttl);
    }

    public async Task<TelegramWebhookResult> ProcessWebhookAsync(
        TelegramWebhookPayload payload, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(payload.Code))
            return new TelegramWebhookResult(TelegramWebhookStatus.Ignored, null);

        // 08 §5.1 brute-force gate — if the Telegram user has already
        // failed MaxFailedAttempts times within the TTL window, drop
        // further redemption attempts silently. Telegram still receives
        // a 200 from the controller so it stops retrying.
        var maxFails = Math.Max(_settings.MaxFailedAttempts, 1);
        if (payload.TelegramUserId is long lockTgId)
        {
            var fails = await _store.GetFailedAttemptsAsync(lockTgId, cancellationToken);
            if (fails >= maxFails)
            {
                return new TelegramWebhookResult(TelegramWebhookStatus.BruteForceLocked, null);
            }
        }

        var userId = await _store.ConsumeAsync(payload.Code, cancellationToken);
        if (userId is null)
        {
            if (payload.TelegramUserId is long failTgId)
            {
                await _store.RegisterFailedAttemptAsync(
                    failTgId, ResolveTtl(), cancellationToken);
            }

            return new TelegramWebhookResult(TelegramWebhookStatus.InvalidOrExpiredCode, null);
        }

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

    private TimeSpan ResolveTtl()
    {
        var configured = TimeSpan.FromSeconds(_settings.CodeTtlSeconds);
        return configured < MinCodeTtl ? MinCodeTtl : configured;
    }

    /// <summary>
    /// 08 §5.1 — 128-bit CSPRNG opaque token (32 hex chars) with the
    /// <c>SKN-</c> prefix the webhook regex already recognises.
    /// Replaces the legacy <c>SKN-XXXXXX</c> 20-bit code so the entropy
    /// budget meets the spec's 122+ bit floor.
    /// </summary>
    private static string GenerateCode()
    {
        Span<byte> bytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(bytes);
        return "SKN-" + Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
