"use client";

import Link from "next/link";
import { useLocale, useTranslations } from "next-intl";
import { cn } from "@/lib/utils/cn";

export interface HeroSectionProps {
  ctaDisabled?: boolean;
  className?: string;
}

export function HeroSection({ ctaDisabled = false, className }: HeroSectionProps) {
  const t = useTranslations("landing.hero");
  const locale = useLocale();

  const ctaLabel = t("cta");
  const ctaHref = `/${locale}/auth/login`;

  return (
    <section
      className={cn("flex flex-col items-center gap-6 px-4 py-16 text-center sm:py-24", className)}
    >
      <h1 className="max-w-3xl text-3xl font-bold tracking-tight text-gray-900 sm:text-5xl">
        {t("title")}
      </h1>
      <p className="max-w-2xl text-base text-gray-600 sm:text-lg">{t("subtitle")}</p>
      {ctaDisabled ? (
        <button
          type="button"
          disabled
          aria-disabled="true"
          className="inline-flex cursor-not-allowed items-center justify-center rounded-md bg-gray-300 px-6 py-3 text-base font-semibold text-gray-500 shadow-sm"
        >
          {ctaLabel}
        </button>
      ) : (
        <Link
          href={ctaHref}
          className="inline-flex items-center justify-center rounded-md bg-blue-600 px-6 py-3 text-base font-semibold text-white shadow-sm hover:bg-blue-700 focus:outline-none focus:ring-2 focus:ring-blue-500 focus:ring-offset-2"
        >
          {ctaLabel}
        </Link>
      )}
      {ctaDisabled && (
        <p className="text-sm text-gray-500" role="status">
          {t("ctaDisabledHint")}
        </p>
      )}
    </section>
  );
}
