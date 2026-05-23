"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { useTranslations } from "next-intl";
import { routing } from "@/i18n/routing";
import { AccountSettings, SupportedLanguage, updateLanguage } from "@/lib/api/settings";
import { cn } from "@/lib/utils/cn";

const LANGUAGE_LABELS: Record<SupportedLanguage, { code: string; label: string }> = {
  en: { code: "EN", label: "English" },
  zh: { code: "中文", label: "中文" },
  es: { code: "ES", label: "Español" },
  tr: { code: "TR", label: "Türkçe" },
};

const SUPPORTED: readonly SupportedLanguage[] = ["en", "zh", "es", "tr"];

export interface LanguagePreferenceSectionProps {
  settings: AccountSettings;
}

/**
 * 04 §7.6 Dil Tercihi. Dropdown'dan seçim → U8 backend persist → locale
 * route'u değiştirip URL'i yeni dil prefix'i ile yenile (header'daki
 * LanguageSelector'ün davranışı ile birebir).
 *
 * Backend yalnızca `language` alanını saklar (07 §5.10); next-intl
 * routing layer URL prefix'ten dili okur — bu yüzden sadece persist
 * etmek yetmez, kullanıcıyı yeni locale path'ine yönlendirmemiz gerekir.
 */
export function LanguagePreferenceSection({ settings }: LanguagePreferenceSectionProps) {
  const t = useTranslations("settings.language");
  const router = useRouter();
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const current = SUPPORTED.includes(settings.language as SupportedLanguage)
    ? (settings.language as SupportedLanguage)
    : "tr";

  async function handleChange(next: SupportedLanguage) {
    if (next === current) return;
    setSaving(true);
    setError(null);
    try {
      await updateLanguage(next);
      if (typeof window !== "undefined") {
        window.localStorage.setItem("preferredLocale", next);
        const segments = window.location.pathname.split("/");
        const hasLocaleSegment =
          segments.length > 1 && (routing.locales as readonly string[]).includes(segments[1]);
        const targetPath = hasLocaleSegment
          ? [segments[0], next, ...segments.slice(2)].join("/")
          : `/${next}${window.location.pathname}`;
        router.replace(targetPath);
      }
    } catch {
      setError(t("error"));
    } finally {
      setSaving(false);
    }
  }

  return (
    <section className="rounded-lg border border-gray-200 bg-white p-6">
      <h2 className="mb-2 text-lg font-semibold text-gray-900">{t("title")}</h2>
      <p className="mb-4 text-sm text-gray-600">{t("description")}</p>

      <label className="block max-w-xs">
        <span className="sr-only">{t("title")}</span>
        <select
          value={current}
          onChange={(e) => void handleChange(e.target.value as SupportedLanguage)}
          disabled={saving}
          className={cn(
            "w-full rounded-md border border-gray-300 bg-white px-3 py-2 text-sm",
            "focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500",
            saving && "opacity-50",
          )}
        >
          {SUPPORTED.map((lang) => (
            <option key={lang} value={lang}>
              {LANGUAGE_LABELS[lang].label} ({LANGUAGE_LABELS[lang].code})
            </option>
          ))}
        </select>
      </label>

      {error && <p className="mt-2 text-xs text-red-600">{error}</p>}
    </section>
  );
}
