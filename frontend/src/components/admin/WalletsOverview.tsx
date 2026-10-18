"use client";

import { useTranslations } from "next-intl";
import { ErrorState, Skeleton } from "@/components/common";
import { useAdminWallets } from "@/lib/hooks/useAdminWallets";
import { NetworkWalletsCard } from "./NetworkWalletsCard";

/**
 * S-wallets (T157) — per-network platform wallet overview. One card per
 * network, in the order the API returns them (enum order: TRON, ETHEREUM).
 */
export function WalletsOverview() {
  const t = useTranslations("adminWallets");
  const { data, isLoading, isError, refetch } = useAdminWallets();

  if (isError) return <ErrorState message={t("loadError")} onRetry={() => refetch()} />;
  if (isLoading || !data) {
    return (
      <div className="flex flex-col gap-2">
        {[0, 1, 2].map((i) => (
          <Skeleton key={i} className="h-24" />
        ))}
      </div>
    );
  }
  return (
    <div className="flex flex-col gap-6">
      {data.networks.map((n) => (
        <NetworkWalletsCard key={n.network} data={n} />
      ))}
    </div>
  );
}
