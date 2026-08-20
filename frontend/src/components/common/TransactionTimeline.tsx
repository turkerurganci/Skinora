import { useTranslations } from "next-intl";
import { TransactionStatus } from "@/types/enums";
import { cn } from "@/lib/utils/cn";

export interface TransactionTimelineProps {
  status: TransactionStatus;
  cancelled?: boolean;
  flagged?: boolean;
  className?: string;
}

/**
 * 04 §C05 (v3.0) — six steps, one per non-terminal status. "Item Emanet" is
 * gone (the platform never holds the item) and the two verification steps were
 * dropped: verification is the *condition* of the next transition, not a step
 * of its own.
 *
 * The list is also the index source — a status maps to its own position — so
 * there is no second lookup table that can drift away from it.
 */
export const TIMELINE_STEPS = [
  TransactionStatus.CREATED,
  TransactionStatus.ACCEPTED,
  TransactionStatus.SELLER_CONFIRMED,
  TransactionStatus.PAYMENT_RECEIVED,
  TransactionStatus.ITEM_DELIVERED,
  TransactionStatus.COMPLETED,
] as const;

/**
 * Statuses that end the flow off-timeline. REFUNDED (WP5 buyer-favor dispute
 * unwind, T129 settlement reversal) belongs here like the CANCELLED_* family —
 * without it the finished transaction rendered as a pulsing "step 1 in progress".
 */
const OFF_TIMELINE: ReadonlySet<TransactionStatus> = new Set([
  TransactionStatus.FLAGGED,
  TransactionStatus.CANCELLED_TIMEOUT,
  TransactionStatus.CANCELLED_SELLER,
  TransactionStatus.CANCELLED_BUYER,
  TransactionStatus.CANCELLED_ADMIN,
  TransactionStatus.REFUNDED,
]);

function indexForStatus(status: TransactionStatus): number {
  if (OFF_TIMELINE.has(status)) return -1;
  const index = (TIMELINE_STEPS as readonly TransactionStatus[]).indexOf(status);
  return index === -1 ? 0 : index;
}

export function TransactionTimeline({
  status,
  cancelled,
  flagged,
  className,
}: TransactionTimelineProps) {
  const t = useTranslations("timeline");
  const isCancelled =
    cancelled ||
    status === TransactionStatus.CANCELLED_TIMEOUT ||
    status === TransactionStatus.CANCELLED_SELLER ||
    status === TransactionStatus.CANCELLED_BUYER ||
    status === TransactionStatus.CANCELLED_ADMIN ||
    status === TransactionStatus.REFUNDED;
  const isFlagged = flagged || status === TransactionStatus.FLAGGED;
  const activeIndex = indexForStatus(status);
  const effectiveIndex = isCancelled || isFlagged ? Math.max(0, activeIndex) : activeIndex;
  // 04 §C05 — COMPLETED is the last step AND a finished one. Without this flag
  // `completed = idx < effectiveIndex` never covers the final step, so the
  // terminal state rendered as the blue pulsing "active" step instead of a
  // green check.
  const isFinished = !isCancelled && !isFlagged && status === TransactionStatus.COMPLETED;

  return (
    <ol
      className={cn("flex flex-col gap-2 md:flex-row md:items-center md:gap-0", className)}
      aria-label={t("ariaLabel")}
    >
      {TIMELINE_STEPS.map((step, idx) => {
        const completed = isFinished || idx < effectiveIndex;
        const active = !isFinished && idx === effectiveIndex;
        const pending = !isFinished && idx > effectiveIndex;
        return (
          <li
            key={step}
            className="flex items-center gap-2 md:flex-1 md:flex-col md:items-stretch md:gap-1"
          >
            <div className="flex items-center md:flex-row md:items-center">
              <span
                className={cn(
                  "flex h-7 w-7 items-center justify-center rounded-full text-xs font-semibold ring-2 ring-white",
                  completed && "bg-green-500 text-white",
                  active && !isCancelled && !isFlagged && "bg-blue-500 text-white animate-pulse",
                  active && isCancelled && "bg-red-500 text-white",
                  active && isFlagged && "bg-orange-500 text-white",
                  pending && "bg-gray-200 text-gray-500",
                )}
                aria-current={active ? "step" : undefined}
              >
                {completed && (
                  <svg
                    className="h-4 w-4"
                    viewBox="0 0 20 20"
                    fill="currentColor"
                    aria-hidden="true"
                  >
                    <path
                      fillRule="evenodd"
                      d="M16.704 5.29a1 1 0 010 1.42l-7.5 7.5a1 1 0 01-1.42 0l-3.5-3.5a1 1 0 011.42-1.42L8.5 12.08l6.79-6.79a1 1 0 011.42 0z"
                      clipRule="evenodd"
                    />
                  </svg>
                )}
                {active && isCancelled && (
                  <svg
                    className="h-4 w-4"
                    viewBox="0 0 20 20"
                    fill="currentColor"
                    aria-hidden="true"
                  >
                    <path
                      fillRule="evenodd"
                      d="M4.293 4.293a1 1 0 011.414 0L10 8.586l4.293-4.293a1 1 0 111.414 1.414L11.414 10l4.293 4.293a1 1 0 01-1.414 1.414L10 11.414l-4.293 4.293a1 1 0 01-1.414-1.414L8.586 10 4.293 5.707a1 1 0 010-1.414z"
                      clipRule="evenodd"
                    />
                  </svg>
                )}
                {active && isFlagged && (
                  <svg
                    className="h-4 w-4"
                    viewBox="0 0 20 20"
                    fill="currentColor"
                    aria-hidden="true"
                  >
                    <path
                      fillRule="evenodd"
                      d="M5 4a1 1 0 011 1v10a1 1 0 11-2 0V5a1 1 0 011-1zm10 0a1 1 0 011 1v10a1 1 0 11-2 0V5a1 1 0 011-1z"
                      clipRule="evenodd"
                    />
                  </svg>
                )}
                {active && !isCancelled && !isFlagged && <span>{idx + 1}</span>}
                {pending && <span>{idx + 1}</span>}
              </span>
              {idx < TIMELINE_STEPS.length - 1 && (
                <span
                  className={cn(
                    "ml-2 h-px flex-1 md:ml-0 md:mt-0 md:hidden",
                    completed ? "bg-green-500" : "bg-gray-200",
                  )}
                  aria-hidden="true"
                />
              )}
            </div>
            <span className="text-xs text-gray-600 md:text-center">{t(`step.${step}`)}</span>
            {idx < TIMELINE_STEPS.length - 1 && (
              <span
                className={cn(
                  "hidden h-px flex-1 md:block",
                  completed ? "bg-green-500" : "bg-gray-200",
                )}
                aria-hidden="true"
              />
            )}
          </li>
        );
      })}
    </ol>
  );
}
