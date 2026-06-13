namespace Skinora.Shared.Enums;

/// <summary>
/// Admin triage state of a <c>BotRecoveryItem</c> — the recovery workflow for an
/// item left in escrow on a bot that has become RESTRICTED/BANNED (T103b-2 —
/// 02 §15, 03 §11.2a, 04 §8.7). Surfaced as the "Recovery Durumu" column of the
/// S18 Recovery Queue (Bekliyor / İnceleniyor / Çözüldü).
/// </summary>
public enum BotRecoveryStatus
{
    /// <summary>Materialised, no admin has acted yet ("Bekliyor").</summary>
    PENDING,

    /// <summary>An admin started manual recovery / is investigating ("İnceleniyor").</summary>
    IN_REVIEW,

    /// <summary>Recovery completed (item returned, delivered, or written off) ("Çözüldü"). Terminal.</summary>
    RESOLVED
}
