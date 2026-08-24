"use client";

import { useState, useRef, useEffect } from "react";
import { useLocale, useTranslations } from "next-intl";
import { useSearchParams } from "next/navigation";
import { routing } from "@/i18n/routing";
import { usePathname, useRouter, setLocaleCookie } from "@/i18n/navigation";
import { cn } from "@/lib/utils/cn";
import type { Locale } from "next-intl";

const LOCALE_LABELS: Record<string, { code: string; label: string }> = {
  en: { code: "EN", label: "English" },
  zh: { code: "中文", label: "中文" },
  es: { code: "ES", label: "Español" },
  tr: { code: "TR", label: "Türkçe" },
};

export interface LanguageSelectorProps {
  className?: string;
  onSelect?: (locale: Locale) => void;
}

export function LanguageSelector({ className, onSelect }: LanguageSelectorProps) {
  const t = useTranslations("languageSelector");
  const currentLocale = useLocale();
  const router = useRouter();
  const pathname = usePathname();
  const searchParams = useSearchParams();
  const [open, setOpen] = useState(false);
  const containerRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!open) return;
    const handler = (e: MouseEvent) => {
      if (containerRef.current && !containerRef.current.contains(e.target as Node)) {
        setOpen(false);
      }
    };
    document.addEventListener("mousedown", handler);
    return () => document.removeEventListener("mousedown", handler);
  }, [open]);

  function handleSelect(locale: Locale) {
    setOpen(false);
    if (locale === currentLocale) return;
    if (onSelect) {
      onSelect(locale);
      return;
    }
    // WP13 — persist the choice in the NEXT_LOCALE cookie and switch locale with
    // a next-intl soft navigation (preserving the current path + query) instead
    // of the legacy localStorage + manual path-splice + full reload.
    setLocaleCookie(locale);
    const qs = searchParams.toString();
    router.replace(qs ? `${pathname}?${qs}` : pathname, { locale });
  }

  const current = LOCALE_LABELS[currentLocale] ?? LOCALE_LABELS.en;

  return (
    <div ref={containerRef} className={cn("relative inline-block", className)}>
      <button
        type="button"
        onClick={() => setOpen((o) => !o)}
        aria-haspopup="listbox"
        aria-expanded={open}
        aria-label={t("ariaLabel")}
        className="inline-flex items-center gap-1 rounded-md border border-gray-300 bg-white px-2 py-1 text-sm text-gray-700 hover:bg-gray-50"
      >
        <span>{current.code}</span>
        <svg
          className={cn("h-4 w-4 transition-transform", open && "rotate-180")}
          viewBox="0 0 20 20"
          fill="currentColor"
          aria-hidden="true"
        >
          <path
            fillRule="evenodd"
            d="M5.293 7.293a1 1 0 011.414 0L10 10.586l3.293-3.293a1 1 0 111.414 1.414l-4 4a1 1 0 01-1.414 0l-4-4a1 1 0 010-1.414z"
            clipRule="evenodd"
          />
        </svg>
      </button>
      {open && (
        <ul
          role="listbox"
          className="absolute right-0 z-10 mt-1 min-w-[140px] rounded-md border border-gray-200 bg-white py-1 shadow-lg"
        >
          {routing.locales.map((locale) => {
            const info = LOCALE_LABELS[locale];
            const selected = locale === currentLocale;
            return (
              <li key={locale} role="option" aria-selected={selected}>
                <button
                  type="button"
                  onClick={() => handleSelect(locale)}
                  className={cn(
                    "flex w-full items-center justify-between px-3 py-2 text-sm hover:bg-gray-50",
                    selected ? "font-semibold text-blue-600" : "text-gray-700",
                  )}
                >
                  <span>{info.label}</span>
                  <span className="text-xs text-gray-400">{info.code}</span>
                </button>
              </li>
            );
          })}
        </ul>
      )}
    </div>
  );
}
