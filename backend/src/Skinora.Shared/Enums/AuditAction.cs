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
    WALLET_ADDRESS_CHANGED
}
