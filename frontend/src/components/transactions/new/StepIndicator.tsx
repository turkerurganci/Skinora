"use client";

import { useTranslations } from "next-intl";
import { cn } from "@/lib/utils/cn";

export interface StepIndicatorProps {
  current: 1 | 2 | 3 | 4;
  className?: string;
}

const STEPS = [1, 2, 3, 4] as const;

export function StepIndicator({ current, className }: StepIndicatorProps) {
  const t = useTranslations("newTransaction.steps");

  return (
    <ol
      className={cn(
        "flex w-full items-center justify-between gap-2 text-xs sm:text-sm",
        className,
      )}
      aria-label={t("indicatorLabel")}
    >
      {STEPS.map((step, index) => {
        const isActive = step === current;
        const isCompleted = step < current;
        const isLast = index === STEPS.length - 1;
        return (
          <li key={step} className="flex flex-1 items-center gap-2">
            <div className="flex items-center gap-2">
              <span
                aria-current={isActive ? "step" : undefined}
                className={cn(
                  "flex h-7 w-7 flex-shrink-0 items-center justify-center rounded-full border-2 font-semibold",
                  isCompleted && "border-blue-600 bg-blue-600 text-white",
                  isActive && "border-blue-600 bg-white text-blue-600",
                  !isActive && !isCompleted && "border-gray-300 bg-white text-gray-500",
                )}
              >
                {isCompleted ? "✓" : step}
              </span>
              <span
                className={cn(
                  "hidden font-medium sm:inline",
                  isActive ? "text-blue-700" : "text-gray-600",
                )}
              >
                {t(`step${step}` as const)}
              </span>
            </div>
            {!isLast && (
              <div
                aria-hidden="true"
                className={cn(
                  "h-0.5 flex-1",
                  isCompleted ? "bg-blue-600" : "bg-gray-300",
                )}
              />
            )}
          </li>
        );
      })}
    </ol>
  );
}
