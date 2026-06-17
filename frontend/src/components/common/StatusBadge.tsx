import { useTranslations } from "next-intl";
import { TransactionStatus } from "@/types/enums";
import { cn } from "@/lib/utils/cn";

export type ExtendedStatus = TransactionStatus | "EMERGENCY_HOLD";

const STATUS_COLOR_MAP: Record<ExtendedStatus, string> = {
  [TransactionStatus.CREATED]: "bg-blue-100 text-blue-800 ring-blue-200",
  [TransactionStatus.ACCEPTED]: "bg-blue-100 text-blue-800 ring-blue-200",
  [TransactionStatus.TRADE_OFFER_SENT_TO_SELLER]: "bg-yellow-100 text-yellow-800 ring-yellow-200",
  [TransactionStatus.ITEM_ESCROWED]: "bg-yellow-100 text-yellow-800 ring-yellow-200",
  [TransactionStatus.PAYMENT_RECEIVED]: "bg-green-50 text-green-700 ring-green-200",
  [TransactionStatus.TRADE_OFFER_SENT_TO_BUYER]: "bg-yellow-100 text-yellow-800 ring-yellow-200",
  [TransactionStatus.ITEM_DELIVERED]: "bg-green-50 text-green-700 ring-green-200",
  [TransactionStatus.COMPLETED]: "bg-green-100 text-green-800 ring-green-300",
  [TransactionStatus.CANCELLED_TIMEOUT]: "bg-red-100 text-red-800 ring-red-200",
  [TransactionStatus.CANCELLED_SELLER]: "bg-red-100 text-red-800 ring-red-200",
  [TransactionStatus.CANCELLED_BUYER]: "bg-red-100 text-red-800 ring-red-200",
  [TransactionStatus.CANCELLED_ADMIN]: "bg-orange-100 text-orange-900 ring-orange-300",
  [TransactionStatus.FLAGGED]: "bg-orange-100 text-orange-800 ring-orange-200",
  // WP5 — buyer-favor dispute refund terminal.
  [TransactionStatus.REFUNDED]: "bg-purple-100 text-purple-800 ring-purple-200",
  EMERGENCY_HOLD: "bg-red-200 text-orange-900 ring-orange-400",
};

export interface StatusBadgeProps {
  status: ExtendedStatus;
  className?: string;
}

export function StatusBadge({ status, className }: StatusBadgeProps) {
  const t = useTranslations("status");
  return (
    <span
      className={cn(
        "inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-medium ring-1 ring-inset whitespace-nowrap",
        STATUS_COLOR_MAP[status],
        className,
      )}
    >
      {t(status)}
    </span>
  );
}
