"use client";

import { useTranslations } from "next-intl";
import type { SettingGroup } from "@/lib/admin/settingsCatalog";
import { SettingRow } from "./SettingRow";
import { tDynamicOrKey } from "@/lib/i18n/dynamicKey";

export interface SettingsGroupTableProps {
  group: SettingGroup;
}

/** One 04 §8.6 parameter group: a titled card with its inline-editable rows. */
export function SettingsGroupTable({ group }: SettingsGroupTableProps) {
  const tg = useTranslations("adminSettings.groups");
  return (
    <section className="rounded-lg border border-gray-200 bg-white shadow-sm">
      <h3 className="border-b border-gray-200 px-4 py-3 text-sm font-semibold text-gray-900">
        {tDynamicOrKey(tg, group.key)}
      </h3>
      <div className="divide-y divide-gray-100 px-4">
        {group.settings.map((s) => (
          <SettingRow key={s.key} setting={s} />
        ))}
      </div>
    </section>
  );
}
