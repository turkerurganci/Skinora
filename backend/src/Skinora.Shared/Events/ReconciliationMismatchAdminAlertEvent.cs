using Skinora.Shared.Domain;

namespace Skinora.Shared.Events;

/// <summary>
/// T165 — the daily ledger reconciliation (05 §3.3) found the platform's books
/// and the chain apart on a network. One event per network round with at least
/// one mismatch; the Notifications consumer fans an
/// <see cref="Skinora.Shared.Enums.NotificationType.ADMIN_RECONCILIATION_MISMATCH"/>
/// out to every admin (inbox + each admin's connected e-mail / Telegram). The
/// per-token detail is in the <c>RECONCILIATION_MISMATCH</c> audit rows written
/// in the same round; the operator's Telegram chat and e-mail get the
/// <c>reconciliation-mismatch</c> money alarm from the metric, independently.
/// </summary>
/// <remarks>
/// <see cref="Network"/> is the <see cref="Skinora.Shared.Enums.BlockchainNetwork"/>
/// name as a string so the event carries no enum-parity surface (as
/// <see cref="PlatformOutageAlertEvent.Component"/>).
/// </remarks>
/// <param name="EventId">Outbox-level event identifier.</param>
/// <param name="Network">Network name: <c>"TRON"</c> or <c>"ETHEREUM"</c>.</param>
/// <param name="MismatchCount">Token mismatches across all compared scopes.</param>
/// <param name="DepositAddressMismatches">Of which at deposit addresses.</param>
/// <param name="HotWalletMismatches">Of which at the hot wallet.</param>
/// <param name="ColdWalletMismatches">Of which at the cold wallet.</param>
/// <param name="OccurredAt">UTC time the round detected them.</param>
public record ReconciliationMismatchAdminAlertEvent(
    Guid EventId,
    string Network,
    int MismatchCount,
    int DepositAddressMismatches,
    int HotWalletMismatches,
    int ColdWalletMismatches,
    DateTime OccurredAt) : IDomainEvent;
