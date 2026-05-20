"use client";

import { useLocale, useTranslations } from "next-intl";
import { InfoScreen } from "@/components/auth";

const SUPPORT_HREF = process.env.NEXT_PUBLIC_SUPPORT_URL ?? "mailto:support@skinora.app";

export default function GeoBlockPage() {
  const t = useTranslations("auth.geoBlock");
  const locale = useLocale();

  return (
    <InfoScreen
      tone="danger"
      icon="🚫"
      title={t("title")}
      description={t("description")}
      actions={
        <a
          href={SUPPORT_HREF}
          className="inline-flex flex-1 items-center justify-center rounded-md border border-gray-300 bg-white px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50 focus:outline-none focus:ring-2 focus:ring-gray-400 focus:ring-offset-2"
        >
          {t("support")}
        </a>
      }
    >
      <p data-locale={locale}>{t("info")}</p>
    </InfoScreen>
  );
}
