"use client";

import { useMemo } from "react";
import { useTranslations } from "next-intl";
import { groupSettings } from "@/lib/admin/settingsCatalog";
import type { AdminSettingItem } from "@/lib/api/admin";
import { ImpactScopeInfoBox } from "./ImpactScopeInfoBox";
import { SettingsGroupTable } from "./SettingsGroupTable";

export interface SettingsManagerProps {
  settings: AdminSettingItem[];
}

/**
 * S17 — Admin Parameter Management (04 §8.6). Renders the impact-scope info box,
 * then the documented 04 §8.6 parameter groups, then the operational categories
 * the spec omits (reputation / platform maintenance / retention) under a
 * separate heading so no real setting is hidden (T102 owner decision).
 */
export function SettingsManager({ settings }: SettingsManagerProps) {
  const t = useTranslations("adminSettings");
  const { documented, operational } = useMemo(() => {
    const groups = groupSettings(settings);
    return {
      documented: groups.filter((g) => g.section === "documented"),
      operational: groups.filter((g) => g.section === "operational"),
    };
  }, [settings]);

  return (
    <div className="flex flex-col gap-6">
      <ImpactScopeInfoBox />

      <div className="flex flex-col gap-4">
        {documented.map((g) => (
          <SettingsGroupTable key={g.key} group={g} />
        ))}
      </div>

      {operational.length > 0 && (
        <div className="flex flex-col gap-4">
          <div>
            <h2 className="text-lg font-semibold text-gray-900">{t("operationalHeading")}</h2>
            <p className="text-sm text-gray-500">{t("operationalNote")}</p>
          </div>
          {operational.map((g) => (
            <SettingsGroupTable key={g.key} group={g} />
          ))}
        </div>
      )}
    </div>
  );
}
