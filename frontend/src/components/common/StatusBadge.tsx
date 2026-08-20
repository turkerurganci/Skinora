import { useTranslations } from "next-intl";
import { TransactionStatus } from "@/types/enums";
import { cn } from "@/lib/utils/cn";

export type ExtendedStatus = TransactionStatus | "EMERGENCY_HOLD";

/**
 * 04 §C01 — colour tone per status. The spec names a tone ("Mavi", "Sarı",
 * "Kırmızı"); the Tailwind shade inside that tone is ours, and it is used to
 * keep same-tone states apart (REFUNDED vs the CANCELLED_* family).
 */
const STATUS_COLOR_MAP: Record<ExtendedStatus, string> = {
  [TransactionStatus.CREATED]: "bg-blue-100 text-blue-800 ring-blue-200",
  [TransactionStatus.ACCEPTED]: "bg-blue-100 text-blue-800 ring-blue-200",
  [TransactionStatus.SELLER_CONFIRMED]: "bg-yellow-100 text-yellow-800 ring-yellow-200",
  // 04 §C01 v3.0 — "Sarı": PAYMENT_RECEIVED is no longer a green "money is in"
  // state, it is a pending one — the seller still has to deliver (02 §2.2).
  [TransactionStatus.PAYMENT_RECEIVED]: "bg-yellow-100 text-yellow-800 ring-yellow-200",
  [TransactionStatus.ITEM_DELIVERED]: "bg-green-50 text-green-700 ring-green-200",
  [TransactionStatus.COMPLETED]: "bg-green-100 text-green-800 ring-green-300",
  [TransactionStatus.CANCELLED_TIMEOUT]: "bg-red-100 text-red-800 ring-red-200",
  [TransactionStatus.CANCELLED_SELLER]: "bg-red-100 text-red-800 ring-red-200",
  [TransactionStatus.CANCELLED_BUYER]: "bg-red-100 text-red-800 ring-red-200",
  [TransactionStatus.CANCELLED_ADMIN]: "bg-orange-100 text-orange-900 ring-orange-300",
  [TransactionStatus.FLAGGED]: "bg-orange-100 text-orange-800 ring-orange-200",
  // WP5 — buyer-favor dispute refund terminal. 04 §C01 puts it in the red tone
  // (it was purple before T133a completed the table); the lighter shade keeps
  // it distinguishable from the CANCELLED_* rows, which share that tone.
  [TransactionStatus.REFUNDED]: "bg-red-50 text-red-800 ring-red-200",
  EMERGENCY_HOLD: "bg-red-200 text-orange-900 ring-orange-400",
};

export interface StatusBadgeProps {
  status: ExtendedStatus;
  className?: string;
  /** Optional test hook (T107 E2E). `data-status` carries the raw enum value
   *  regardless; pass `testId` where a stable selector is needed (detail header). */
  testId?: string;
}

export function StatusBadge({ status, className, testId }: StatusBadgeProps) {
  const t = useTranslations("status");
  return (
    <span
      data-testid={testId}
      data-status={status}
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
