using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Users.Domain.Entities;

namespace Skinora.Users.Application.Settings;

public sealed class DiscordConnectionService : IDiscordConnectionService
{
    private readonly IDiscordOAuthStateStore _stateStore;
    private readonly IDiscordOAuthClient _oauthClient;
    private readonly INotificationPreferenceStore _preferences;
    private readonly AppDbContext _db;
    private readonly DiscordSettings _settings;

    public DiscordConnectionService(
        IDiscordOAuthStateStore stateStore,
        IDiscordOAuthClient oauthClient,
        INotificationPreferenceStore preferences,
        AppDbContext db,
        IOptions<DiscordSettings> settings)
    {
        _stateStore = stateStore;
        _oauthClient = oauthClient;
        _preferences = preferences;
        _db = db;
        _settings = settings.Value;
    }

    public async Task<DiscordAuthorizeUrl> BuildAuthorizeUrlAsync(
        Guid userId, CancellationToken cancellationToken)
    {
        var state = GenerateState();
        var ttl = TimeSpan.FromSeconds(Math.Max(60, _settings.StateTtlSeconds));
        await _stateStore.IssueAsync(state, userId, ttl, cancellationToken);

        var url = $"{_settings.AuthorizeUrl}" +
                  $"?client_id={Uri.EscapeDataString(_settings.ClientId)}" +
                  $"&redirect_uri={Uri.EscapeDataString(_settings.RedirectUri)}" +
                  $"&response_type=code" +
                  $"&scope={Uri.EscapeDataString(_settings.Scope)}" +
                  $"&state={Uri.EscapeDataString(state)}";

        return new DiscordAuthorizeUrl(url);
    }

    public async Task<DiscordCallbackResult> HandleCallbackAsync(
        string? code, string? state, string? error, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(error))
            return new DiscordCallbackResult(DiscordCallbackStatus.UserDenied, null, null);

        if (string.IsNullOrWhiteSpace(state))
            return new DiscordCallbackResult(DiscordCallbackStatus.InvalidState, null, null);

        var userId = await _stateStore.ConsumeAsync(state, cancellationToken);
        if (userId is null)
            return new DiscordCallbackResult(DiscordCallbackStatus.InvalidState, null, null);

        if (string.IsNullOrWhiteSpace(code))
            return new DiscordCallbackResult(DiscordCallbackStatus.ExchangeFailed, null, null);

        var profile = await _oauthClient.ExchangeAsync(code, cancellationToken);
        if (profile is null)
            return new DiscordCallbackResult(DiscordCallbackStatus.ExchangeFailed, null, null);

        // User-scope guard: if the resolved user doesn't exist or is
        // deactivated, treat as invalid state rather than exchange failure —
        // the state was consumed but the subject is gone.
        var exists = await _db.Set<User>().AsNoTracking()
            .AnyAsync(u => u.Id == userId.Value && !u.IsDeactivated, cancellationToken);
        if (!exists)
            return new DiscordCallbackResult(DiscordCallbackStatus.InvalidState, null, null);

        if (await _preferences.ExternalIdInUseByAnotherUserAsync(
            userId.Value, NotificationChannel.DISCORD, profile.DiscordUserId, cancellationToken))
        {
            return new DiscordCallbackResult(
                DiscordCallbackStatus.AlreadyLinkedToAnotherUser, null, null);
        }

        await _preferences.UpsertPreferenceAsync(
            userId.Value,
            NotificationChannel.DISCORD,
            externalId: profile.DiscordUserId,
            isEnabled: true,
            verifiedAt: DateTime.UtcNow,
            cancellationToken);

        return new DiscordCallbackResult(
            DiscordCallbackStatus.Connected, userId.Value, profile.Username);
    }

    public async Task<DiscordDisconnectResult> DisconnectAsync(
        Guid userId, CancellationToken cancellationToken)
    {
        var user = await _db.Set<User>()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                u => u.Id == userId && !u.IsDeactivated, cancellationToken);

        if (user is null)
            return new DiscordDisconnectResult(DiscordDisconnectStatus.UserNotFound);

        var removed = await _preferences.DeletePreferenceAsync(
            userId, NotificationChannel.DISCORD, cancellationToken);

        return new DiscordDisconnectResult(
            removed ? DiscordDisconnectStatus.Removed : DiscordDisconnectStatus.NotConnected);
    }

    private static string GenerateState()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }
}
