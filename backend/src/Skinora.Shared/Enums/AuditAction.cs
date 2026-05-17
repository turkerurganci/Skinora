namespace Skinora.Shared.Enums;

public enum AuditAction
{
    // Fon operasyonları
    WALLET_DEPOSIT,
    WALLET_WITHDRAW,
    WALLET_ESCROW_LOCK,
    WALLET_ESCROW_RELEASE,
    WALLET_REFUND,

    // Admin operasyonları
    DISPUTE_RESOLVED,
    MANUAL_REFUND,
    REFUND_BLOCKED,
    USER_BANNED,
    USER_UNBANNED,
    ROLE_CHANGED,
    SYSTEM_SETTING_CHANGED,

    // Güvenlik operasyonları
    WALLET_ADDRESS_CHANGED,

    // Fraud flag operasyonları (T54 — 02 §14.0, 03 §7-§8.2)
    FRAUD_FLAG_CREATED,
    FRAUD_FLAG_APPROVED,
    FRAUD_FLAG_REJECTED,
    FRAUD_FLAG_AUTO_HOLD,

    // Admin transaction lifecycle (T59 — 02 §7, 07 §9.20-§9.22, 03 §8.8)
    TRANSACTION_CANCELLED_ADMIN,
    EMERGENCY_HOLD_APPLIED,
    EMERGENCY_HOLD_RELEASED,

    // Steam bot lifecycle (T69 — 02 §15, 05 §3.2). Sidecar reports bot
    // restriction / ban / pool removal via signed webhook; backend mirrors
    // it onto PlatformSteamBot.Status and records the transition here so
    // the SECURITY_EVENT queue surfaces it alongside wallet-address events.
    BOT_STATUS_CHANGED,

    // Blockchain reconciliation (T76 — 05 §3.3). The daily reconciliation
    // job emits one row per (scope, token) mismatch — scope ∈ {DepositAddress,
    // HotWallet, ColdWallet}. EntityType encodes the scope; EntityId carries
    // the address; NewValue is a JSON envelope {token, expected, actual,
    // delta, blockNumber}. Append-only; the audit trail is the durable
    // record even when the realtime SignalR push to admins is lost.
    RECONCILIATION_MISMATCH
}
