using Microsoft.EntityFrameworkCore;
using Skinora.Shared.Persistence;
using Skinora.Users.Domain.Entities;

namespace Skinora.Users.Application.Account;

/// <summary>
/// Executes deactivate (<c>POST /users/me/deactivate</c>) and delete
/// (<c>DELETE /users/me</c>) per 07 §5.17, 02 §19, 06 §6.2. Cross-module
/// side effects (transactions, notifications, refresh tokens) are fanned
/// out through port interfaces so this service only holds <c>User</c>
/// mutations directly.
/// </summary>
public sealed class AccountLifecycleService : IAccountLifecycleService
{
    /// <summary>
    /// 07 §5.17 U14 confirmation phrase — Turkish uppercase "SİL" with the
    /// dotted-I. Exact, case-sensitive match; any other value returns
    /// <c>VALIDATION_ERROR</c>.
    /// </summary>
    internal const string DeleteConfirmationPhrase = "SİL";

    private const string DeactivateMessage =
        "Hesabınız deaktif edildi. Tekrar giriş yaparak aktif edebilirsiniz.";
    private const string DeleteMessage =
        "Hesabınız silindi. Kişisel verileriniz temizlendi.";

    private readonly AppDbContext _db;
    private readonly IUserActiveTransactionChecker _activeTransactions;
    private readonly INotificationAccountAnonymizer _notificationAnonymizer;
    private readonly IAuthAccountAnonymizer _authAnonymizer;
    private readonly TimeProvider _clock;

    public AccountLifecycleService(
        AppDbContext db,
        IUserActiveTransactionChecker activeTransactions,
        INotificationAccountAnonymizer notificationAnonymizer,
        IAuthAccountAnonymizer authAnonymizer,
        TimeProvider clock)
    {
        _db = db;
        _activeTransactions = activeTransactions;
        _notificationAnonymizer = notificationAnonymizer;
        _authAnonymizer = authAnonymizer;
        _clock = clock;
    }

    public async Task<AccountDeactivateOutcome> DeactivateAsync(
        Guid userId, CancellationToken cancellationToken)
    {
        var user = await LoadLiveUserAsync(userId, cancellationToken);
        if (user is null) return new AccountDeactivateOutcome.UserNotFound();

        if (await _activeTransactions.HasActiveTransactionsAsync(userId, cancellationToken))
            return new AccountDeactivateOutcome.HasActiveTransactions();

        var now = _clock.GetUtcNow().UtcDateTime;
        user.IsDeactivated = true;
        user.DeactivatedAt = now;

        await _db.SaveChangesAsync(cancellationToken);

        // Session termination (02 §19, 07 §5.17 U13). Refresh token rows are
        // revoked but not soft-deleted — the user can log back in and the row
        // history remains observable for audit.
        await _authAnonymizer.RevokeAllSessionsAsync(userId, cancellationToken);

        return new AccountDeactivateOutcome.Success(now);
    }

    public async Task<AccountDeleteOutcome> DeleteAsync(
        Guid userId, string? confirmation, CancellationToken cancellationToken)
    {
        if (!string.Equals(confirmation, DeleteConfirmationPhrase, StringComparison.Ordinal))
            return new AccountDeleteOutcome.ConfirmationInvalid();

        var user = await LoadLiveUserAsync(userId, cancellationToken);
        if (user is null) return new AccountDeleteOutcome.UserNotFound();

        if (await _activeTransactions.HasActiveTransactionsAsync(userId, cancellationToken))
            return new AccountDeleteOutcome.HasActiveTransactions();

        var now = _clock.GetUtcNow().UtcDateTime;

        // 06 §6.2 — anonymize User fields that carry PII. Transaction /
        // TransactionHistory / AuditLog / UserLoginLog rows are untouched
        // (kept as anonymous audit trail).
        AnonymizeUserInPlace(user, now);

        // Persist User anonymization first — the NotificationDelivery masking
        // below reads Channel + current TargetExternalId, so a single-round
        // SaveChanges is sufficient and keeps the operation simple.
        await _db.SaveChangesAsync(cancellationToken);

        await _notificationAnonymizer.AnonymizeAsync(userId, cancellationToken);
        await _authAnonymizer.AnonymizeSessionsAsync(userId, cancellationToken);

        return new AccountDeleteOutcome.Success(now);
    }

    private async Task<User?> LoadLiveUserAsync(Guid userId, CancellationToken cancellationToken)
        => await _db.Set<User>()
            .FirstOrDefaultAsync(
                u => u.Id == userId && !u.IsDeactivated, cancellationToken);

    private static void AnonymizeUserInPlace(User user, DateTime now)
    {
        // SteamId: UNIQUE + NOT NULL must survive — use "ANON_" + 15 hex so
        // the combined length stays inside the 20-char column. Collision
        // probability across 15 hex chars (~60 bits) is negligible; a DB
        // unique-constraint violation on insert would still surface to the
        // caller as a transient error rather than silent corruption.
        user.SteamId = $"ANON_{Guid.NewGuid():N}"[..20];
        user.SteamDisplayName = "Deleted User";
        user.SteamAvatarUrl = null;

        user.DefaultPayoutAddress = null;
        user.DefaultRefundAddress = null;

        user.Email = null;
        user.EmailVerifiedAt = null;

        // Steam trade URL triple carries Steam identity — clear together.
        user.SteamTradeUrl = null;
        user.SteamTradePartner = null;
        user.SteamTradeAccessToken = null;

        user.IsDeleted = true;
        user.DeletedAt = now;
    }
}
