"use client";

import { useState } from "react";
import { useTranslations } from "next-intl";
import { cn } from "@/lib/utils/cn";

export type MaintenanceVariant =
  | "plannedMaintenance"
  | "activeMaintenance"
  | "steamOutage"
  | "blockchainDegradation";

const VARIANT_STYLES: Record<MaintenanceVariant, string> = {
  plannedMaintenance: "bg-yellow-100 border-yellow-300 text-yellow-900",
  activeMaintenance: "bg-red-100 border-red-300 text-red-900",
  steamOutage: "bg-orange-100 border-orange-300 text-orange-900",
  blockchainDegradation: "bg-orange-100 border-orange-300 text-orange-900",
};

export interface MaintenanceBannerProps {
  variant: MaintenanceVariant;
  message?: string;
  scheduledAt?: string;
  className?: string;
}

export function MaintenanceBanner({
  variant,
  message,
  scheduledAt,
  className,
}: MaintenanceBannerProps) {
  const t = useTranslations("maintenanceBanner");
  const [dismissed, setDismissed] = useState(false);
  const canDismiss = variant === "plannedMaintenance";

  if (dismissed) return null;

  const text = message ?? (scheduledAt ? t(`${variant}WithSchedule`, { scheduledAt }) : t(variant));

  return (
    <div
      role="alert"
      className={cn(
        "flex items-center gap-3 border-b px-4 py-2 text-sm",
        VARIANT_STYLES[variant],
        className,
      )}
    >
      <svg
        className="h-5 w-5 flex-shrink-0"
        viewBox="0 0 20 20"
        fill="currentColor"
        aria-hidden="true"
      >
        <path
          fillRule="evenodd"
          d="M8.485 2.495c.673-1.167 2.357-1.167 3.03 0l6.28 10.875c.673 1.167-.17 2.625-1.515 2.625H3.72c-1.345 0-2.188-1.458-1.515-2.625l6.28-10.875zM10 6a1 1 0 011 1v3a1 1 0 11-2 0V7a1 1 0 011-1zm0 8a1 1 0 100-2 1 1 0 000 2z"
          clipRule="evenodd"
        />
      </svg>
      <p className="flex-1">{text}</p>
      {canDismiss && (
        <button
          type="button"
          onClick={() => setDismissed(true)}
          className="text-current opacity-60 hover:opacity-100"
          aria-label={t("dismiss")}
        >
          <svg className="h-4 w-4" viewBox="0 0 20 20" fill="currentColor">
            <path
              fillRule="evenodd"
              d="M4.293 4.293a1 1 0 011.414 0L10 8.586l4.293-4.293a1 1 0 111.414 1.414L11.414 10l4.293 4.293a1 1 0 01-1.414 1.414L10 11.414l-4.293 4.293a1 1 0 01-1.414-1.414L8.586 10 4.293 5.707a1 1 0 010-1.414z"
              clipRule="evenodd"
            />
          </svg>
        </button>
      )}
    </div>
  );
}
