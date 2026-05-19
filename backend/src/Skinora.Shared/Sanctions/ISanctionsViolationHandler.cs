namespace Skinora.Shared.Sanctions;

/// <summary>
/// Records a sanctions list match as an account-level fraud flag with the
/// EMERGENCY_HOLD cascade applied (02 §21.1, 03 §11a.3, 06 §3.25). Port lives
/// in Skinora.Shared so the wallet pipeline (Users) ve login pipeline (Auth)
/// callers do not pull a dependency on Fraud (one-way Fraud → Users / Auth).
/// </summary>
/// <remarks>
/// <para>
/// Implementation persists the staged flag immediately (`SaveChangesAsync`)
/// and emits <c>FraudFlagCreatedEvent</c> through the outbox — consumers
/// (notifications, realtime, audit) handle downstream side effects.
/// </para>
/// <para>
/// Idempotency: if the user already has a PENDING account-level
/// <c>SANCTIONS_MATCH</c> flag, the handler skips re-creating the flag.
/// The underlying emergency-hold cascade is also idempotent (skips
/// transactions already on hold), so repeated invocations are safe.
/// </para>
/// </remarks>
public interface ISanctionsViolationHandler
{
    /// <summary>
    /// Wallet pipeline match path (T34 — adres kaydı/güncelleme sırasında).
    /// <paramref name="userId"/> kayıtlı bir kullanıcıdır;
    /// <paramref name="attemptedAddress"/> reddedilen yeni adres (savunma
    /// amaçlı kayıt — User.DefaultPayoutAddress / DefaultRefundAddress'e
    /// yazılmaz).
    /// </summary>
    Task RecordWalletAttemptAsync(
        Guid userId,
        string attemptedAddress,
        string matchedList,
        CancellationToken cancellationToken);

    /// <summary>
    /// Login pipeline match path (T29 — Steam login sırasında mevcut wallet
    /// adresleri eşleşmesi). Handler kullanıcıyı <paramref name="steamId64"/>
    /// ile çözer; kullanıcı yoksa no-op.
    /// </summary>
    Task RecordLoginAttemptAsync(
        string steamId64,
        string matchedList,
        CancellationToken cancellationToken);

    /// <summary>
    /// Admin retroaktif scan (AD23 POST /admin/sanctions/addresses sonrası).
    /// Belirli bir adresi <c>DefaultPayoutAddress</c> veya
    /// <c>DefaultRefundAddress</c> olarak tutan kullanıcılar için sanctions
    /// match'i kaydeder. Caller (admin service) eşleşen kullanıcıları zaten
    /// listeler ve her biri için bu metodu çağırır.
    /// </summary>
    Task RecordRetroactiveMatchAsync(
        Guid userId,
        string matchedAddress,
        string matchedList,
        CancellationToken cancellationToken);
}
