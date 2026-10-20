"use client";

import { useTranslations } from "next-intl";
import { WalletsOverview } from "@/components/admin";

/**
 * T157 — Admin wallets (AD38 overview, AD20 hot→cold transfer, outbound-row
 * closure tool). Each network is shown in its own section.
 */
export default function AdminWalletsPage() {
  const t = useTranslations("adminWallets");
  return (
    <div className="mx-auto w-full max-w-5xl px-4 py-6">
      <h1 className="mb-1 text-2xl font-semibold text-gray-900">{t("title")}</h1>
      <p className="mb-4 text-sm text-gray-500">{t("intro")}</p>
      <WalletsOverview />
    </div>
  );
}
