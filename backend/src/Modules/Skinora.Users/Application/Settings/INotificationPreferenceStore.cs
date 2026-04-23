using Skinora.Shared.Enums;

namespace Skinora.Users.Application.Settings;

/// <summary>
/// Abstraction over <c>UserNotificationPreference</c> persistence. The concrete
/// implementation lives in <c>Skinora.Notifications</c> because that module
/// owns the entity, but <c>Skinora.Users</c> cannot reference it (the graph
/// points the other way — see .csproj). DI is wired at the API composition
/// root, mirroring <c>IActiveTransactionCounter</c> (T34).
/// </summary>
public interface INotificationPreferenceStore
{
    /// <summary>
    /// Snapshot of the user's active preferences, one per channel. Missing
    /// rows are omitted — the caller supplies the "not connected" default.
    /// </summary>
    Task<IReadOnlyList<NotificationPreferenceSnapshot>> GetAsync(
        Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Toggles the <c>IsEnabled</c> flag on an existing active preference.
    /// Returns <see cref="NotificationToggleOutcome.NotConnected"/> if no
    /// active row exists — the caller decides whether to surface
    /// <c>CHANNEL_NOT_CONNECTED</c> (07 §5.9) or upsert a row.
    /// </summary>
    Task<NotificationToggleOutcome> ToggleEnabledAsync(
        Guid userId,
        NotificationChannel channel,
        bool enabled,
        CancellationToken cancellationToken);

    /// <summary>
    /// Creates or revives a preference row. Used when a user-side flow
    /// (email enable, telegram connect, discord connect) needs to establish
    /// the preference row even if one doesn't already exist.
    /// </summary>
    Task UpsertPreferenceAsync(
        Guid userId,
        NotificationChannel channel,
        string? externalId,
        bool isEnabled,
        DateTime? verifiedAt,
        CancellationToken cancellationToken);

    /// <summary>
    /// Soft-deletes the user's preference row for the given channel. Used by
    /// the <c>DELETE /users/me/settings/{telegram|discord}</c> endpoints
    /// (07 §5.14, §5.15). Idempotent: missing row returns <c>false</c>.
    /// </summary>
    Task<bool> DeletePreferenceAsync(
        Guid userId,
        NotificationChannel channel,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns <c>true</c> when the same <paramref name="externalId"/> on
    /// the given channel is already linked to another user. Used by the
    /// Telegram/Discord connect flows to surface an <c>ALREADY_LINKED</c>
    /// error (07 §5.13 — mirrors the Discord OAuth callback contract).
    /// </summary>
    Task<bool> ExternalIdInUseByAnotherUserAsync(
        Guid userId,
        NotificationChannel channel,
        string externalId,
        CancellationToken cancellationToken);
}

public sealed record NotificationPreferenceSnapshot(
    NotificationChannel Channel,
    bool IsEnabled,
    string? ExternalId,
    DateTime? VerifiedAt);

public enum NotificationToggleOutcome
{
    Updated,
    NotConnected,
}
