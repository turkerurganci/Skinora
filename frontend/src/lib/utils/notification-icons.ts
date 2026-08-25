import { NotificationType } from "@/types/enums";

/**
 * Six S11 ikon kategorisi tanımlandığı 04 §7.7. The 27 backend
 * `NotificationType` values (07 §8.1 / 06 §2.13) are projected onto these
 * categories so the row UI renders the canonical icon set.
 *
 * Frontend-only mapping by design (T95 scope decision): keeps the API
 * contract untouched and centralises icon edits when the type list grows.
 *
 * `Record<NotificationType, …>` is the guard: adding a type to the enum without
 * classifying it here is a compile error, so the mapping cannot silently lag
 * behind the catalogue.
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
  // v3.0 — the seller is now expected to send the item directly to the buyer.
  // Replaces TRADE_OFFER_SENT_TO_BUYER and keeps its 🔄 flow-update icon.
  [NotificationType.DELIVERY_EXPECTED]: "transactionUpdate",

  // v3.0 — replaces ITEM_ESCROWED, but not its icon: what opens here is the
  // payment window (the deposit address is revealed), so this is a 💰 row.
  [NotificationType.PAYMENT_WINDOW_OPEN]: "payment",
  [NotificationType.PAYMENT_RECEIVED]: "payment",
  [NotificationType.SELLER_PAYMENT_SENT]: "payment",
  [NotificationType.PAYMENT_INCORRECT]: "payment",
  [NotificationType.LATE_PAYMENT_REFUNDED]: "payment",
  [NotificationType.PAYMENT_REFUNDED]: "payment",

  // Backlog F7Gate-EventsWithoutConsumer — a payout outcome on the seller's own
  // sale, so it reads as a 💰 row next to SELLER_PAYMENT_SENT.
  [NotificationType.PAYOUT_ISSUE_RESOLVED]: "payment",

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
