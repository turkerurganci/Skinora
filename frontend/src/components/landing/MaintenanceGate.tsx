"use client";

import { useLocale } from "next-intl";
import { MaintenanceBanner, type MaintenanceVariant } from "@/components/common/MaintenanceBanner";
import { usePlatformMaintenance } from "@/lib/hooks/usePlatformMaintenance";
import type { MaintenanceType } from "@/lib/api/platform";

const VARIANT_MAP: Record<MaintenanceType, MaintenanceVariant> = {
  PLANNED_MAINTENANCE: "plannedMaintenance",
  PLATFORM_MAINTENANCE: "activeMaintenance",
  STEAM_OUTAGE: "steamOutage",
  BLOCKCHAIN_DEGRADATION: "blockchainDegradation",
};

export interface MaintenanceGateRenderProps {
  ctaDisabled: boolean;
}

export interface MaintenanceGateProps {
  children: (state: MaintenanceGateRenderProps) => React.ReactNode;
}

export function MaintenanceGate({ children }: MaintenanceGateProps) {
  const locale = useLocale();
  const { data } = usePlatformMaintenance();

  const showBanner = Boolean(data?.active && data.type);
  const variant = data?.type ? VARIANT_MAP[data.type] : null;
  const ctaDisabled = data?.active === true && data.type === "PLATFORM_MAINTENANCE";

  const scheduledAt = data?.plannedEnd
    ? new Date(data.plannedEnd).toLocaleString(locale, {
        dateStyle: "medium",
        timeStyle: "short",
      })
    : undefined;

  return (
    <>
      {showBanner && variant && (
        <MaintenanceBanner
          variant={variant}
          message={data?.message ?? undefined}
          scheduledAt={scheduledAt}
        />
      )}
      {children({ ctaDisabled })}
    </>
  );
}
