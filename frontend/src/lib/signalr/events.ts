/**
 * SignalR server→client event payloads pushed by the backend hubs (07 §11.1
 * RT1 `/hubs/transactions`, 07 §11.2 RT2 `/hubs/notifications`).
 *
 * Field shape mirrors the backend records in
 * `Skinora.Realtime.Application.Contracts.{TransactionRealtimePayloads,
 * NotificationRealtimePayloads}`. Enum payload fields are PascalCase or
 * UPPER_CASE strings because the SignalR JSON protocol is configured with
 * `JsonStringEnumConverter` on the backend (Program.cs:266-268) and System
 * Text JSON emits enum names verbatim.
 *
 * The transaction status / dispute status / review status enum values reuse
 * the existing `@/types/enums` definitions which already mirror 06 §2.
 */
import type {
  DisputeStatus,
  ReviewStatus,
  TimeoutFreezeReason,
  TransactionStatus,
} from "@/types/enums";

// ---------- RT1 — /hubs/transactions (07 §11.1) ----------

/**
 * Timeout phase emitted in `CountdownSync.timeoutType`. Mirrors backend
 * `Skinora.Shared.Enums.TimeoutPhase` (Accept / TradeOfferToSeller / Payment
 * / Delivery — PascalCase on the wire because the enum members are PascalCase
 * and `JsonStringEnumConverter` preserves member casing).
 */
export type TimeoutPhase = "Accept" | "TradeOfferToSeller" | "Payment" | "Delivery";

/**
 * Emergency hold release action. Mirrors backend
 * `Skinora.Shared.Enums.EmergencyHoldReleaseAction` (RESUME / CANCEL — admin
 * EMERGENCY_HOLD release decision).
 */
export type EmergencyHoldReleaseAction = "RESUME" | "CANCEL";

export interface TransactionStatusChangedPayload {
  transactionId: string;
  fromStatus: TransactionStatus;
  toStatus: TransactionStatus;
  timestamp: string;
}

export interface CountdownSyncPayload {
  transactionId: string;
  timeoutType: TimeoutPhase;
  remainingSeconds: number;
  frozen: boolean;
  frozenReason: TimeoutFreezeReason | null;
}

export interface PaymentDetectedPayload {
  transactionId: string;
  amount: number;
  txHash: string;
  status: string;
}

export interface PaymentConfirmedPayload {
  transactionId: string;
  amount: number;
  txHash: string;
  confirmations: number;
}

export interface DisputeUpdatePayload {
  transactionId: string;
  disputeId: string;
  status: DisputeStatus;
  autoCheckResult: string | null;
}

export interface FlagResolvedPayload {
  transactionId: string;
  reviewStatus: ReviewStatus;
}

export interface EmergencyHoldAppliedPayload {
  transactionId: string;
  message: string;
}

export interface EmergencyHoldReleasedPayload {
  transactionId: string;
  action: EmergencyHoldReleaseAction;
  resumedStatus: TransactionStatus;
}

// ---------- RT2 — /hubs/notifications (07 §11.2) ----------

/**
 * Spec field `targetType` is `"transaction" | "flag" | null` (07 §8.1 GAP-8).
 */
export interface NewNotificationPayload {
  id: string;
  type: string;
  message: string;
  targetType: "transaction" | "flag" | null;
  targetId: string | null;
  createdAt: string;
}

export interface UnreadCountChangedPayload {
  unreadCount: number;
}

export interface TelegramConnectedPayload {
  username: string;
}

export interface DiscordConnectedPayload {
  username: string;
}

/**
 * Spec field set (07 §11.2). When `active` flips to `true` the global C08
 * maintenance banner is shown and active-transaction timeouts freeze; when it
 * flips back to `false` the banner clears and countdowns resume.
 */
export interface MaintenanceStatusChangedPayload {
  active: boolean;
  type: string | null;
  message: string | null;
  plannedEnd: string | null;
}

// ---------- Event name constants ----------

/**
 * Method names invoked by the backend hubs. Centralized as constants so the
 * hub clients and any future test harness stay in sync without typos.
 */
export const TransactionHubEvents = {
  TransactionStatusChanged: "TransactionStatusChanged",
  CountdownSync: "CountdownSync",
  PaymentDetected: "PaymentDetected",
  PaymentConfirmed: "PaymentConfirmed",
  DisputeUpdate: "DisputeUpdate",
  FlagResolved: "FlagResolved",
  EmergencyHoldApplied: "EmergencyHoldApplied",
  EmergencyHoldReleased: "EmergencyHoldReleased",
} as const;

export const NotificationHubEvents = {
  NewNotification: "NewNotification",
  UnreadCountChanged: "UnreadCountChanged",
  TelegramConnected: "TelegramConnected",
  DiscordConnected: "DiscordConnected",
  MaintenanceStatusChanged: "MaintenanceStatusChanged",
} as const;

export const TransactionHubMethods = {
  JoinTransaction: "JoinTransaction",
  LeaveTransaction: "LeaveTransaction",
} as const;
