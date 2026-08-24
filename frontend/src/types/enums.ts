/**
 * TypeScript enum definitions matching C# backend (06 §2).
 * Values are string literals to match EF Core string storage (HasConversion).
 */

// §2.1 — 12 values
export enum TransactionStatus {
  CREATED = "CREATED",
  ACCEPTED = "ACCEPTED",
  // v3.0 — the seller confirmed readiness; the deposit address is revealed to
  // the buyer from this state onwards (02 §2.2 step 3). Renamed from
  // TRADE_OFFER_SENT_TO_SELLER — the platform sends no trade offer.
  SELLER_CONFIRMED = "SELLER_CONFIRMED",
  // v3.0 — payment is escrowed and the SELLER must now send the item directly
  // to the buyer. ITEM_ESCROWED / TRADE_OFFER_SENT_TO_BUYER were removed: the
  // platform never holds the item (02 §2.1, 05 §4.1).
  PAYMENT_RECEIVED = "PAYMENT_RECEIVED",
  ITEM_DELIVERED = "ITEM_DELIVERED",
  COMPLETED = "COMPLETED",
  CANCELLED_TIMEOUT = "CANCELLED_TIMEOUT",
  CANCELLED_SELLER = "CANCELLED_SELLER",
  CANCELLED_BUYER = "CANCELLED_BUYER",
  CANCELLED_ADMIN = "CANCELLED_ADMIN",
  FLAGGED = "FLAGGED",
  // WP5 — buyer-favor admin dispute resolution terminal.
  REFUNDED = "REFUNDED",
}

// §2.2 — 2 values
export enum StablecoinType {
  USDT = "USDT",
  USDC = "USDC",
}

// §2.3 — 2 values
export enum BuyerIdentificationMethod {
  STEAM_ID = "STEAM_ID",
  OPEN_LINK = "OPEN_LINK",
}

// §2.4 — 4 values
export enum CancelledByType {
  TIMEOUT = "TIMEOUT",
  SELLER = "SELLER",
  BUYER = "BUYER",
  ADMIN = "ADMIN",
}

// §2.5 — 10 values
export enum BlockchainTransactionType {
  BUYER_PAYMENT = "BUYER_PAYMENT",
  SELLER_PAYOUT = "SELLER_PAYOUT",
  BUYER_REFUND = "BUYER_REFUND",
  EXCESS_REFUND = "EXCESS_REFUND",
  WRONG_TOKEN_INCOMING = "WRONG_TOKEN_INCOMING",
  WRONG_TOKEN_REFUND = "WRONG_TOKEN_REFUND",
  SPAM_TOKEN_INCOMING = "SPAM_TOKEN_INCOMING",
  LATE_PAYMENT_REFUND = "LATE_PAYMENT_REFUND",
  INCORRECT_AMOUNT_REFUND = "INCORRECT_AMOUNT_REFUND",
  // WP3 — deposit address → hot wallet sweep ledger entry.
  SWEEP = "SWEEP",
}

// §2.6 — 4 values
export enum BlockchainTransactionStatus {
  DETECTED = "DETECTED",
  PENDING = "PENDING",
  CONFIRMED = "CONFIRMED",
  FAILED = "FAILED",
}

// §2.7 TradeOfferDirection / §2.8 TradeOfferStatus — removed in v3.0 (P2P).
// The platform creates no trade offer, so there is neither a direction nor an
// offer lifecycle to track; delivery state is carried by DeliveryEvidence
// (06 §2.24). Section numbers are deliberately skipped, mirroring 06 §2, so
// the §2.9+ references do not shift.

// §2.9 — 3 values
export enum DisputeType {
  PAYMENT = "PAYMENT",
  DELIVERY = "DELIVERY",
  WRONG_ITEM = "WRONG_ITEM",
}

// §2.10 — 5 values
export enum DisputeStatus {
  OPEN = "OPEN",
  ESCALATED = "ESCALATED",
  CLOSED = "CLOSED",
  // WP5 — admin resolution terminals.
  RESOLVED_FOR_SELLER = "RESOLVED_FOR_SELLER",
  RESOLVED_FOR_BUYER = "RESOLVED_FOR_BUYER",
}

// WP5 — admin dispute resolution decision (resolve request).
export enum DisputeResolutionOutcome {
  SELLER_FAVOR = "SELLER_FAVOR",
  BUYER_FAVOR = "BUYER_FAVOR",
}

// §2.11 — 6 values
export enum FraudFlagType {
  PRICE_DEVIATION = "PRICE_DEVIATION",
  HIGH_VOLUME = "HIGH_VOLUME",
  ABNORMAL_BEHAVIOR = "ABNORMAL_BEHAVIOR",
  MULTI_ACCOUNT = "MULTI_ACCOUNT",
  // T82 — sanctioned wallet address match (02 §21.1).
  SANCTIONS_MATCH = "SANCTIONS_MATCH",
  // T129 — seller pulled the delivered item back inside the settlement window (02 §4.5.1).
  DELIVERY_REVERSED = "DELIVERY_REVERSED",
}

// §2.12 — 3 values
export enum ReviewStatus {
  PENDING = "PENDING",
  APPROVED = "APPROVED",
  REJECTED = "REJECTED",
}

// §2.13 — 26 values
export enum NotificationType {
  TRANSACTION_INVITE = "TRANSACTION_INVITE",
  BUYER_ACCEPTED = "BUYER_ACCEPTED",
  // v3.0 — the seller confirmed readiness, so the deposit address is now open
  // to the buyer (02 §2.2 step 3). Replaces ITEM_ESCROWED: nothing is escrowed
  // at this point except, shortly, the money.
  PAYMENT_WINDOW_OPEN = "PAYMENT_WINDOW_OPEN",
  PAYMENT_RECEIVED = "PAYMENT_RECEIVED",
  // v3.0 — payment is in escrow and the SELLER must now send the item directly
  // to the buyer. Replaces TRADE_OFFER_SENT_TO_BUYER, which targeted the buyer;
  // the recipient of this notification flipped sides.
  DELIVERY_EXPECTED = "DELIVERY_EXPECTED",
  TRANSACTION_COMPLETED = "TRANSACTION_COMPLETED",
  SELLER_PAYMENT_SENT = "SELLER_PAYMENT_SENT",
  TIMEOUT_WARNING = "TIMEOUT_WARNING",
  TRANSACTION_CANCELLED = "TRANSACTION_CANCELLED",
  TRANSACTION_FLAGGED = "TRANSACTION_FLAGGED",
  PAYMENT_INCORRECT = "PAYMENT_INCORRECT",
  LATE_PAYMENT_REFUNDED = "LATE_PAYMENT_REFUNDED",
  // ITEM_RETURNED removed in v3.0 — the platform never holds the item, so it
  // can never return one (02 §9).
  PAYMENT_REFUNDED = "PAYMENT_REFUNDED",
  DISPUTE_RESULT = "DISPUTE_RESULT",
  FLAG_RESOLVED = "FLAG_RESOLVED",
  ADMIN_FLAG_ALERT = "ADMIN_FLAG_ALERT",
  ADMIN_ESCALATION = "ADMIN_ESCALATION",
  ADMIN_PAYMENT_FAILURE = "ADMIN_PAYMENT_FAILURE",
  // ADMIN_STEAM_BOT_ISSUE removed in v3.0 — the platform runs no Steam bots
  // (02 §15, 05 §3.2).
  // T59 — emergency hold lifecycle.
  EMERGENCY_HOLD_APPLIED = "EMERGENCY_HOLD_APPLIED",
  EMERGENCY_HOLD_RELEASED = "EMERGENCY_HOLD_RELEASED",
  // T72 — blockchain amount validation outcomes.
  INSUFFICIENT_PAYMENT = "INSUFFICIENT_PAYMENT",
  OVERPAYMENT_REFUNDED = "OVERPAYMENT_REFUNDED",
  WRONG_TOKEN_REFUND = "WRONG_TOKEN_REFUND",
  // T105a — account suspension lifecycle.
  ACCOUNT_SUSPENDED = "ACCOUNT_SUSPENDED",
  ACCOUNT_UNSUSPENDED = "ACCOUNT_UNSUSPENDED",
  // WP16 — platform health probe outage alert (admin-only).
  ADMIN_PLATFORM_OUTAGE = "ADMIN_PLATFORM_OUTAGE",
}

// §2.14 — 3 values
export enum NotificationChannel {
  EMAIL = "EMAIL",
  TELEGRAM = "TELEGRAM",
  DISCORD = "DISCORD",
}

// §2.15 PlatformSteamBotStatus — removed in v3.0 (P2P). The platform operates
// no Steam account, so there is no bot whose status could be tracked
// (02 §15, 05 §3.2). Section number deliberately skipped, mirroring 06 §2.

// §2.16 — 5 values
export enum MonitoringStatus {
  ACTIVE = "ACTIVE",
  POST_CANCEL_24H = "POST_CANCEL_24H",
  POST_CANCEL_7D = "POST_CANCEL_7D",
  POST_CANCEL_30D = "POST_CANCEL_30D",
  STOPPED = "STOPPED",
}

// §2.17 — 4 values
export enum OutboxMessageStatus {
  PENDING = "PENDING",
  PROCESSED = "PROCESSED",
  DEFERRED = "DEFERRED",
  FAILED = "FAILED",
}

// §2.18 — 3 values
export enum ActorType {
  USER = "USER",
  SYSTEM = "SYSTEM",
  ADMIN = "ADMIN",
}

// §2.19 — 29 values
export enum AuditAction {
  // Fund operations
  WALLET_DEPOSIT = "WALLET_DEPOSIT",
  WALLET_WITHDRAW = "WALLET_WITHDRAW",
  WALLET_ESCROW_LOCK = "WALLET_ESCROW_LOCK",
  WALLET_ESCROW_RELEASE = "WALLET_ESCROW_RELEASE",
  WALLET_REFUND = "WALLET_REFUND",
  // Admin operations
  DISPUTE_RESOLVED = "DISPUTE_RESOLVED",
  MANUAL_REFUND = "MANUAL_REFUND",
  REFUND_BLOCKED = "REFUND_BLOCKED",
  USER_BANNED = "USER_BANNED",
  USER_UNBANNED = "USER_UNBANNED",
  ROLE_CHANGED = "ROLE_CHANGED",
  SYSTEM_SETTING_CHANGED = "SYSTEM_SETTING_CHANGED",
  // Security operations
  WALLET_ADDRESS_CHANGED = "WALLET_ADDRESS_CHANGED",
  // T54 — fraud flag operations
  FRAUD_FLAG_CREATED = "FRAUD_FLAG_CREATED",
  FRAUD_FLAG_APPROVED = "FRAUD_FLAG_APPROVED",
  FRAUD_FLAG_REJECTED = "FRAUD_FLAG_REJECTED",
  FRAUD_FLAG_AUTO_HOLD = "FRAUD_FLAG_AUTO_HOLD",
  // T59 — admin transaction lifecycle
  TRANSACTION_CANCELLED_ADMIN = "TRANSACTION_CANCELLED_ADMIN",
  EMERGENCY_HOLD_APPLIED = "EMERGENCY_HOLD_APPLIED",
  EMERGENCY_HOLD_RELEASED = "EMERGENCY_HOLD_RELEASED",
  // T129 — admin clears an ESCALATED settlement in the seller's favour (AD32).
  SETTLEMENT_CLEARED_ADMIN = "SETTLEMENT_CLEARED_ADMIN",
  // BOT_STATUS_CHANGED / BOT_SESSION_FAILED / BOT_RECOVERY_ITEM_CREATED /
  // BOT_RECOVERY_UPDATED removed in v3.0 — the bot lifecycle and its recovery
  // queue were retired with the custody layer (T117 / T132).
  // T76 / T77 — reconciliation & hot wallet management
  RECONCILIATION_MISMATCH = "RECONCILIATION_MISMATCH",
  COLD_WALLET_TRANSFER_INITIATED = "COLD_WALLET_TRANSFER_INITIATED",
  HOT_WALLET_THRESHOLD_BREACHED = "HOT_WALLET_THRESHOLD_BREACHED",
  // T82 — sanctions list management
  SANCTIONS_LIST_ADDRESS_ADDED = "SANCTIONS_LIST_ADDRESS_ADDED",
  SANCTIONS_LIST_ADDRESS_REMOVED = "SANCTIONS_LIST_ADDRESS_REMOVED",
  // WP7 — platform maintenance / outage toggle
  MAINTENANCE_MODE_CHANGED = "MAINTENANCE_MODE_CHANGED",
  // WP16 — restart-recovery auto timeout extension & health probe outage
  TIMEOUT_AUTO_EXTENDED = "TIMEOUT_AUTO_EXTENDED",
  PLATFORM_OUTAGE_DETECTED = "PLATFORM_OUTAGE_DETECTED",
}

// §2.20 — 4 values
export enum TimeoutFreezeReason {
  MAINTENANCE = "MAINTENANCE",
  STEAM_OUTAGE = "STEAM_OUTAGE",
  BLOCKCHAIN_DEGRADATION = "BLOCKCHAIN_DEGRADATION",
  EMERGENCY_HOLD = "EMERGENCY_HOLD",
}

// §2.21 — 2 values
export enum FraudFlagScope {
  ACCOUNT_LEVEL = "ACCOUNT_LEVEL",
  TRANSACTION_PRE_CREATE = "TRANSACTION_PRE_CREATE",
}

// §2.22 — 5 values
export enum PayoutIssueStatus {
  REPORTED = "REPORTED",
  VERIFYING = "VERIFYING",
  RETRY_SCHEDULED = "RETRY_SCHEDULED",
  ESCALATED = "ESCALATED",
  RESOLVED = "RESOLVED",
}

// §2.23 — 4 values
export enum DeliveryStatus {
  PENDING = "PENDING",
  SENT = "SENT",
  // Quiet-hours / maintenance deferral (DeferredNotificationDeliveryJob). Present
  // in the C# enum since the notification delivery work; 06 §2.23 still lists
  // three values — doc-side gap tracked in DEFERRED_BACKLOG (T134-Doc06DeliveryDeferred).
  DEFERRED = "DEFERRED",
  FAILED = "FAILED",
}

/**
 * Action chosen by the admin when releasing an emergency hold (07 §9.22 AD19c).
 *
 * WP6a (T134-FeEnumUnionDup) — moved here from a bare string union in
 * `lib/api/admin.ts`. It mirrors a real C# enum
 * (`Skinora.Shared.Enums.EmergencyHoldReleaseAction`), and living outside this
 * file meant `enums.parity.test.ts` — which only reads this file — never
 * compared it against the backend. Declaring it here puts it under that guard.
 */
export enum EmergencyHoldReleaseAction {
  RESUME = "RESUME",
  CANCEL = "CANCEL",
}
