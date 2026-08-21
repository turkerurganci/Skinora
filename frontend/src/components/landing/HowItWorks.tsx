"use client";

import { useTranslations } from "next-intl";
import { cn } from "@/lib/utils/cn";

export interface HowItWorksProps {
  className?: string;
}

// 04 §S01 — the four-step P2P narrative. `paymentEscrowed` replaced the
// custodial `itemEscrowed` slot in T136: what the platform holds is the
// money, never the item (02 §2.1).
const STEP_KEYS = ["sellerStarts", "paymentEscrowed", "sellerDelivers", "autoSettle"] as const;
const STEP_ICONS: Record<(typeof STEP_KEYS)[number], string> = {
  sellerStarts: "📝",
  paymentEscrowed: "🛡️",
  sellerDelivers: "📦",
  autoSettle: "✅",
};

export function HowItWorks({ className }: HowItWorksProps) {
  const t = useTranslations("landing.howItWorks");

  return (
    <section className={cn("bg-white px-4 py-16", className)} aria-labelledby="how-it-works-title">
      <div className="mx-auto max-w-5xl">
        <h2
          id="how-it-works-title"
          className="text-center text-2xl font-bold tracking-tight text-gray-900 sm:text-3xl"
        >
          {t("title")}
        </h2>
        <ol className="mt-10 grid grid-cols-1 gap-6 sm:grid-cols-2 lg:grid-cols-4">
          {STEP_KEYS.map((key, index) => (
            <li
              key={key}
              className="flex flex-col items-center gap-3 rounded-lg border border-gray-200 bg-gray-50 p-6 text-center"
            >
              <span
                className="inline-flex h-12 w-12 items-center justify-center rounded-full bg-blue-100 text-2xl"
                aria-hidden="true"
              >
                {STEP_ICONS[key]}
              </span>
              <div className="text-sm font-semibold text-blue-700">{index + 1}</div>
              <h3 className="text-base font-semibold text-gray-900">{t(`steps.${key}.title`)}</h3>
              <p className="text-sm text-gray-600">{t(`steps.${key}.description`)}</p>
            </li>
          ))}
        </ol>
      </div>
    </section>
  );
}
