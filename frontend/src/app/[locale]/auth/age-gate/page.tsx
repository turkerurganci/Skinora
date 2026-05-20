"use client";

import Link from "next/link";
import { useLocale, useTranslations } from "next-intl";
import { InfoScreen } from "@/components/auth";

export default function AgeGatePage() {
  const t = useTranslations("auth.ageGate");
  const locale = useLocale();

  return (
    <InfoScreen
      tone="danger"
      icon="🔞"
      title={t("title")}
      description={t("description")}
      actions={
        <Link
          href={`/${locale}`}
          className="inline-flex flex-1 items-center justify-center rounded-md border border-gray-300 bg-white px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50 focus:outline-none focus:ring-2 focus:ring-gray-400 focus:ring-offset-2"
        >
          {t("backToHome")}
        </Link>
      }
    />
  );
}
