"use client";

import Link from "next/link";
import { useLocale, useTranslations } from "next-intl";

/**
 * 04 §7.4 hızlı linkler bölümü. S10 (hesap ayarları) ve S05 (dashboard /
 * işlem geçmişi) navigasyon kısayolları. Header'daki paterni izler —
 * locale prefix manuel olarak href'e eklenir (next-intl routing API'si
 * şu an Link wrapper export etmiyor).
 */
export function QuickLinks() {
  const t = useTranslations("profile.quickLinks");
  const locale = useLocale();
  const href = (path: string) => `/${locale}${path}`;

  return (
    <section className="rounded-lg border border-gray-200 bg-white p-6">
      <h2 className="mb-3 text-lg font-semibold text-gray-900">{t("title")}</h2>
      <ul className="flex flex-col gap-2 text-sm">
        <li>
          <Link
            href={href("/settings")}
            className="text-blue-600 hover:text-blue-700 hover:underline"
          >
            {t("settings")}
          </Link>
        </li>
        <li>
          <Link
            href={href("/dashboard")}
            className="text-blue-600 hover:text-blue-700 hover:underline"
          >
            {t("transactions")}
          </Link>
        </li>
      </ul>
    </section>
  );
}
