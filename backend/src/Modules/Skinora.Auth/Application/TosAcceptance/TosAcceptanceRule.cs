namespace Skinora.Auth.Application.TosAcceptance;

/// <summary>
/// T166 — the single definition of "this account has accepted the Terms of
/// Service AND attested 18+" (02 §21.1, 03 §1 step "ilk kez giriş", 03 §11a.2).
/// </summary>
/// <remarks>
/// <para>
/// Three call sites must agree on it, and they used to read it differently:
/// the server-side gate (<c>TosAcceptanceGateFilter</c> — rejects the request),
/// <c>/auth/me</c>'s <c>tosAccepted</c> (the client shows the modal when it is
/// false) and <see cref="TosAcceptanceService"/>'s duplicate check (409 when
/// nothing is left to accept). If the gate demanded both stamps while
/// <c>tosAccepted</c> looked only at the ToS stamp, an account missing the
/// age stamp would be rejected everywhere, never shown the modal, and — had it
/// been shown — refused with 409: a lock-out with no way out.
/// </para>
/// <para>
/// The version is deliberately NOT part of the rule. Re-prompting after a ToS
/// version bump stays the client's job (<c>TosRepromptGate</c>, 07 §4.4); the
/// server has no authoritative "current version" yet, and making one up here
/// would lock every existing account out the moment the two sides disagreed
/// (öneriyle kaydedildi — ⚠ GÜVENLİK; T168 hukuk metinleri sürüm kararıyla
/// yeniden ele alınabilir).
/// </para>
/// </remarks>
public static class TosAcceptanceRule
{
    public static bool IsSatisfied(DateTime? tosAcceptedAt, DateTime? ageConfirmedAt) =>
        tosAcceptedAt is not null && ageConfirmedAt is not null;
}
