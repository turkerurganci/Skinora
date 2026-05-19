import { useTranslations } from "next-intl";
import { TransactionStatus } from "@/types/enums";
import { cn } from "@/lib/utils/cn";

export interface TransactionTimelineProps {
  status: TransactionStatus;
  cancelled?: boolean;
  flagged?: boolean;
  className?: string;
}

const STEPS = [
  "CREATED",
  "ACCEPTED",
  "ITEM_ESCROWED",
  "PAYMENT_RECEIVED",
  "PAYMENT_VERIFIED",
  "ITEM_DELIVERED",
  "DELIVERY_VERIFIED",
  "COMPLETED",
] as const;

function indexForStatus(status: TransactionStatus): number {
  switch (status) {
    case TransactionStatus.CREATED:
      return 0;
    case TransactionStatus.ACCEPTED:
      return 1;
    case TransactionStatus.TRADE_OFFER_SENT_TO_SELLER:
    case TransactionStatus.ITEM_ESCROWED:
      return 2;
    case TransactionStatus.PAYMENT_RECEIVED:
      return 4;
    case TransactionStatus.TRADE_OFFER_SENT_TO_BUYER:
      return 5;
    case TransactionStatus.ITEM_DELIVERED:
      return 6;
    case TransactionStatus.COMPLETED:
      return 7;
    case TransactionStatus.FLAGGED:
    case TransactionStatus.CANCELLED_TIMEOUT:
    case TransactionStatus.CANCELLED_SELLER:
    case TransactionStatus.CANCELLED_BUYER:
    case TransactionStatus.CANCELLED_ADMIN:
      return -1;
    default:
      return 0;
  }
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
    status === TransactionStatus.CANCELLED_ADMIN;
  const isFlagged = flagged || status === TransactionStatus.FLAGGED;
  const activeIndex = indexForStatus(status);
  const effectiveIndex = isCancelled || isFlagged ? Math.max(0, activeIndex) : activeIndex;

  return (
    <ol
      className={cn("flex flex-col gap-2 md:flex-row md:items-center md:gap-0", className)}
      aria-label={t("ariaLabel")}
    >
      {STEPS.map((step, idx) => {
        const completed = idx < effectiveIndex;
        const active = idx === effectiveIndex;
        const pending = idx > effectiveIndex;
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
              {idx < STEPS.length - 1 && (
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
            {idx < STEPS.length - 1 && (
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
