"use client";

import Link from "next/link";
import { useLocale, useTranslations } from "next-intl";
import { InfoScreen } from "@/components/auth";

const SUPPORT_HREF = process.env.NEXT_PUBLIC_SUPPORT_URL ?? "mailto:support@skinora.app";

export default function SuspendedPage() {
  const t = useTranslations("auth.suspended");
  const locale = useLocale();

  return (
    <InfoScreen
      tone="warning"
      icon="🚷"
      title={t("title")}
      description={t("description")}
      actions={
        <>
          <Link
            href={`/${locale}/dashboard`}
            className="inline-flex flex-1 items-center justify-center rounded-md bg-blue-600 px-4 py-2 text-sm font-semibold text-white shadow-sm hover:bg-blue-700 focus:outline-none focus:ring-2 focus:ring-blue-500 focus:ring-offset-2"
          >
            {t("goToDashboard")}
          </Link>
          <a
            href={SUPPORT_HREF}
            className="inline-flex flex-1 items-center justify-center rounded-md border border-gray-300 bg-white px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50 focus:outline-none focus:ring-2 focus:ring-gray-400 focus:ring-offset-2"
          >
            {t("support")}
          </a>
        </>
      }
    >
      <p className="rounded-md bg-amber-50 p-3 text-xs text-amber-800">
        {t("activeTransactionsNote")}
      </p>
    </InfoScreen>
  );
}
