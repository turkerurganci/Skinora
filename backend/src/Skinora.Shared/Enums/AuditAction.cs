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

    // Steam bot session failure (WP8 — 02 §15, 05 §3.2, 08 §3.3). Written by the
    // Steam webhook handler for the bot.session_failed / bot.removed_from_pool
    // lifecycle events, capturing the sidecar incident (event + reason) that took
    // the bot out of the active pool. Distinct from BOT_STATUS_CHANGED (the terse
    // status transition X→Y record kept for the T69 contract): this row is the
    // incident record paired 1:1 with the ADMIN_STEAM_BOT_ISSUE admin
    // notification. EntityType = "PlatformSteamBot"; EntityId = PlatformSteamBot.Id;
    // OldValue = previous status; NewValue is a JSON envelope {event, reason,
    // status}. ActorType = SYSTEM. SECURITY_EVENT category — sits beside
    // BOT_STATUS_CHANGED in the admin security queue.
    BOT_SESSION_FAILED,

    // Bot recovery queue (T103b-2 — 02 §15, 03 §11.2a, 04 §8.7). Written
    // when a stuck-escrow BotRecoveryItem is materialised after a bot is
    // restricted/banned. EntityType = "BotRecoveryItem"; EntityId =
    // BotRecoveryItem.Id; NewValue is a JSON envelope {botId, transactionId,
    // statusAtRestriction, autoHeld}. ActorType = SYSTEM.
    BOT_RECOVERY_ITEM_CREATED,

    // Bot recovery queue (T103b-2 — 04 §8.7). Written when an admin updates a
    // recovery item (note / responsible admin / status — Manual Recovery
    // Başlat → IN_REVIEW, Çözüldü → RESOLVED) via PATCH AD26. EntityType =
    // "BotRecoveryItem"; EntityId = BotRecoveryItem.Id; Old/NewValue capture
    // the changed fields. ActorType = ADMIN.
    BOT_RECOVERY_UPDATED,

    // Blockchain reconciliation (T76 — 05 §3.3). The daily reconciliation
    // job emits one row per (scope, token) mismatch — scope ∈ {DepositAddress,
    // HotWallet, ColdWallet}. EntityType encodes the scope; EntityId carries
    // the address; NewValue is a JSON envelope {token, expected, actual,
    // delta, blockNumber}. Append-only; the audit trail is the durable
    // record even when the realtime SignalR push to admins is lost.
    RECONCILIATION_MISMATCH,

    // Hot wallet management (T77 — 05 §3.3). Admin-initiated hot→cold
    // operational consolidation. EntityType = "ColdWalletTransfer";
    // EntityId = ColdWalletTransfer.Id; NewValue is a JSON envelope
    // {token, amount, fromAddress, toAddress, txHash}. Logged from
    // HotWalletService alongside the ColdWalletTransfer ledger row so the
    // FUND_MOVEMENT queue mirrors customer-facing wallet activity.
    COLD_WALLET_TRANSFER_INITIATED,

    // Hot wallet management (T77 — 05 §3.3). Periodic monitor detected a
    // balance crossing an admin-configured threshold. EntityType =
    // "HotWallet"; EntityId = hot wallet address; NewValue is a JSON
    // envelope {token, direction (Upper|Lower), threshold, actual,
    // blockNumber}. SECURITY_EVENT category — sits alongside reconciliation
    // mismatches on the admin dashboard.
    HOT_WALLET_THRESHOLD_BREACHED,

    // Sanctions list management (T82 — 02 §21.1, 03 §11a.3, 07 §9.24).
    // Admin AD23 POST /admin/sanctions/addresses ile yeni adres eklediğinde
    // yazılır. EntityType = "SanctionedAddress"; EntityId = SanctionedAddress.Id
    // (Guid); NewValue is a JSON envelope {address, network, source, reason}.
    // SECURITY_EVENT category — wallet-address-changed / reconciliation-mismatch
    // ile aynı admin güvenlik kuyruğunda görünür.
    SANCTIONS_LIST_ADDRESS_ADDED,

    // Sanctions list management (T82 — 07 §9.25). Admin AD24
    // DELETE /admin/sanctions/addresses/:id ile satırı deaktive ettiğinde
    // yazılır. EntityType = "SanctionedAddress"; EntityId = SanctionedAddress.Id;
    // NewValue is a JSON envelope {address, source}. SECURITY_EVENT category.
    SANCTIONS_LIST_ADDRESS_REMOVED,

    // Platform maintenance / outage toggle (WP7 — 02 §3.3, 05 §4.4, 07 §10.2).
    // Written when an admin enters or leaves maintenance mode via
    // POST /admin/maintenance/freeze|resume. EntityType = "Maintenance";
    // EntityId = the new maintenance type ("PLATFORM_MAINTENANCE",
    // "STEAM_OUTAGE", "BLOCKCHAIN_DEGRADATION", "PLANNED_MAINTENANCE" or
    // "NONE" on resume); Old/NewValue capture the four platform.maintenance.*
    // settings plus the number of transactions frozen/resumed. ActorType = ADMIN.
    MAINTENANCE_MODE_CHANGED,

    // Restart-recovery auto timeout extension (WP16 — 05 §4.4:533-536). Written
    // once per restart-recovery pass when the detected outage window crosses the
    // RecoveryThresholdSeconds gate and active timeouts are extended. EntityType =
    // "SystemHeartbeat"; EntityId = "1" (the singleton that drove the outage
    // calculation); NewValue is a JSON envelope {outageSeconds, extendedCount,
    // rescheduledPaymentJobs}. ActorType = SYSTEM. ADMIN_ACTION category — sits
    // with MAINTENANCE_MODE_CHANGED so operators see automatic + manual downtime
    // handling on the same queue.
    TIMEOUT_AUTO_EXTENDED,

    // Platform health probe outage / recovery (WP16 — 05 §4.4, 02 §3.3). Written
    // once per state transition by the periodic health probe when a Steam /
    // blockchain sidecar crosses the consecutive-failure threshold (DEGRADED) or
    // recovers (RECOVERED). EntityType = "PlatformHealth"; EntityId = the
    // component ("STEAM" / "BLOCKCHAIN"); NewValue is a JSON envelope
    // {component, status, consecutiveFailures}. ActorType = SYSTEM. SECURITY_EVENT
    // category — sits beside the bot-status / reconciliation operational alarms;
    // pairs 1:1 with the ADMIN_PLATFORM_OUTAGE admin notification.
    PLATFORM_OUTAGE_DETECTED
}
