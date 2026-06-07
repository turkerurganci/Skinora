"use client";

import { useTranslations } from "next-intl";
import { cn } from "@/lib/utils/cn";
import type { SettingImpact } from "@/lib/admin/settingsCatalog";

/** Per-impact pill styling, shared by the info-box legend and each row badge. */
const IMPACT_STYLES: Record<SettingImpact, string> = {
  newTransaction: "border-blue-200 bg-blue-50 text-blue-700",
  runtime: "border-amber-200 bg-amber-50 text-amber-800",
  supportingSignal: "border-purple-200 bg-purple-50 text-purple-700",
};

export interface ImpactBadgeProps {
  impact: SettingImpact;
  className?: string;
}

/** Small impact-scope pill shown next to every setting (04 §8.6 per-row label). */
export function ImpactBadge({ impact, className }: ImpactBadgeProps) {
  const t = useTranslations("adminSettings.impact");
  return (
    <span
      className={cn(
        "inline-flex items-center rounded-full border px-2 py-0.5 text-[11px] font-medium",
        IMPACT_STYLES[impact],
        className,
      )}
    >
      {t(impact)}
    </span>
  );
}

const LEGEND: readonly SettingImpact[] = ["newTransaction", "runtime", "supportingSignal"];

/**
 * 04 §8.6 top-of-page info box documenting the three impact-scope classes
 * (new transaction / runtime / supporting signal). AD8 carries no impact field,
 * so each row's label is derived from its category (`impactForCategory`).
 */
export function ImpactScopeInfoBox() {
  const t = useTranslations("adminSettings.impact");
  return (
    <div className="rounded-lg border border-gray-200 bg-gray-50 p-4">
      <h2 className="mb-1 text-sm font-semibold text-gray-900">{t("title")}</h2>
      <p className="mb-3 text-sm text-gray-600">{t("intro")}</p>
      <ul className="flex flex-col gap-2">
        {LEGEND.map((impact) => (
          <li key={impact} className="flex items-start gap-2 text-sm">
            <ImpactBadge impact={impact} className="mt-0.5 shrink-0" />
            <span className="text-gray-600">{t(`${impact}Desc`)}</span>
          </li>
        ))}
      </ul>
    </div>
  );
}
