import { NotificationType } from "@/types/enums";

/**
 * Six S11 ikon kategorisi tanımlandığı 04 §7.7. The 28 backend
 * `NotificationType` values (07 §8.1 / 06 §2.13) are projected onto these
 * categories so the row UI renders the canonical icon set.
 *
 * Frontend-only mapping by design (T95 scope decision): keeps the API
 * contract untouched and centralises icon edits when the type list grows.
 */
export type NotificationIconCategory =
  | "transactionUpdate"
  | "payment"
  | "warning"
  | "completion"
  | "cancellation"
  | "flag";

const CATEGORY_BY_TYPE: Record<NotificationType, NotificationIconCategory> = {
  [NotificationType.TRANSACTION_INVITE]: "transactionUpdate",
  [NotificationType.BUYER_ACCEPTED]: "transactionUpdate",
  [NotificationType.ITEM_ESCROWED]: "transactionUpdate",
  [NotificationType.TRADE_OFFER_SENT_TO_BUYER]: "transactionUpdate",
  [NotificationType.ITEM_RETURNED]: "transactionUpdate",

  [NotificationType.PAYMENT_RECEIVED]: "payment",
  [NotificationType.SELLER_PAYMENT_SENT]: "payment",
  [NotificationType.PAYMENT_INCORRECT]: "payment",
  [NotificationType.LATE_PAYMENT_REFUNDED]: "payment",
  [NotificationType.PAYMENT_REFUNDED]: "payment",

  [NotificationType.INSUFFICIENT_PAYMENT]: "payment",
  [NotificationType.OVERPAYMENT_REFUNDED]: "payment",
  [NotificationType.WRONG_TOKEN_REFUND]: "payment",

  [NotificationType.TIMEOUT_WARNING]: "warning",
  [NotificationType.EMERGENCY_HOLD_APPLIED]: "warning",
  [NotificationType.ACCOUNT_SUSPENDED]: "warning",
  [NotificationType.ADMIN_PLATFORM_OUTAGE]: "warning",

  [NotificationType.TRANSACTION_COMPLETED]: "completion",

  [NotificationType.TRANSACTION_CANCELLED]: "cancellation",

  [NotificationType.EMERGENCY_HOLD_RELEASED]: "transactionUpdate",
  [NotificationType.ACCOUNT_UNSUSPENDED]: "transactionUpdate",

  [NotificationType.TRANSACTION_FLAGGED]: "flag",
  [NotificationType.FLAG_RESOLVED]: "flag",
  [NotificationType.DISPUTE_RESULT]: "flag",
  [NotificationType.ADMIN_FLAG_ALERT]: "flag",
  [NotificationType.ADMIN_ESCALATION]: "flag",
  [NotificationType.ADMIN_PAYMENT_FAILURE]: "flag",
  [NotificationType.ADMIN_STEAM_BOT_ISSUE]: "flag",
};

const ICON_BY_CATEGORY: Record<NotificationIconCategory, string> = {
  transactionUpdate: "🔄",
  payment: "💰",
  warning: "⚠",
  completion: "✅",
  cancellation: "❌",
  flag: "🔍",
};

export function categoryForType(type: NotificationType): NotificationIconCategory {
  return CATEGORY_BY_TYPE[type] ?? "transactionUpdate";
}

export function iconForType(type: NotificationType): string {
  return ICON_BY_CATEGORY[categoryForType(type)];
}
