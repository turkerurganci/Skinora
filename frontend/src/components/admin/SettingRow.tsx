"use client";

import { useState } from "react";
import { useTranslations } from "next-intl";
import { useToast } from "@/components/common";
import { ApiError } from "@/lib/api/client";
import { useUpdateSetting } from "@/lib/hooks/useUpdateSetting";
import { impactForCategory } from "@/lib/admin/settingsCatalog";
import type { AdminSettingItem, AdminSettingValueType } from "@/lib/api/admin";
import { ImpactBadge } from "./ImpactScopeInfoBox";
import { tDynamic } from "@/lib/i18n/dynamicKey";

interface SettingInputProps {
  valueType: AdminSettingValueType;
  value: string;
  unit: string | null;
  disabled: boolean;
  ariaLabel: string;
  booleanLabels: { yes: string; no: string };
  onChange: (value: string) => void;
}

/** Value-type-aware editor: select for booleans, numeric/text input otherwise. */
function SettingInput({
  valueType,
  value,
  unit,
  disabled,
  ariaLabel,
  booleanLabels,
  onChange,
}: SettingInputProps) {
  if (valueType === "boolean") {
    const normalized = value.toLowerCase();
    const selected = normalized === "true" ? "true" : normalized === "false" ? "false" : "";
    return (
      <select
        aria-label={ariaLabel}
        value={selected}
        disabled={disabled}
        onChange={(e) => onChange(e.target.value)}
        className="w-full rounded-md border border-gray-300 px-2 py-1.5 text-sm sm:w-44"
      >
        <option value="" disabled>
          —
        </option>
        <option value="true">{booleanLabels.yes}</option>
        <option value="false">{booleanLabels.no}</option>
      </select>
    );
  }
  return (
    <div className="flex items-center gap-1">
      <input
        type={valueType === "number" ? "number" : "text"}
        step={valueType === "number" ? "any" : undefined}
        aria-label={ariaLabel}
        value={value}
        disabled={disabled}
        onChange={(e) => onChange(e.target.value)}
        className="w-full rounded-md border border-gray-300 px-2 py-1.5 text-sm sm:w-56"
      />
      {unit && <span className="shrink-0 text-xs text-gray-500">{unit}</span>}
    </div>
  );
}

export interface SettingRowProps {
  setting: AdminSettingItem;
}

/**
 * One inline-editable setting (04 §8.6): label + description + impact badge on
 * the left, current value + edit controls on the right. "Düzenle" reveals a
 * value-type-aware input with Kaydet / İptal; a successful AD9 update fires the
 * "Parametre güncellendi" toast and refreshes via the mutation's cache
 * invalidation. Backend validation errors (VALIDATION_ERROR) surface inline and
 * keep the row in edit mode so the admin can correct the value.
 */
export function SettingRow({ setting }: SettingRowProps) {
  const t = useTranslations("adminSettings");
  const { push } = useToast();
  const update = useUpdateSetting();
  const [editing, setEditing] = useState(false);
  const [draft, setDraft] = useState("");

  const impact = impactForCategory(setting.category);

  function startEdit() {
    setDraft(setting.value ?? "");
    update.reset();
    setEditing(true);
  }

  function cancel() {
    update.reset();
    setEditing(false);
  }

  function save() {
    update.mutate(
      { key: setting.key, value: draft },
      {
        onSuccess: () => {
          setEditing(false);
          push({ variant: "success", message: t("saved") });
        },
      },
    );
  }

  const displayValue = (() => {
    if (setting.value === null || setting.value === "") return t("unconfigured");
    if (setting.valueType === "boolean") {
      return setting.value.toLowerCase() === "true" ? t("boolean.yes") : t("boolean.no");
    }
    return setting.unit ? `${setting.value} ${setting.unit}` : setting.value;
  })();

  const errorMessage = update.isError
    ? update.error instanceof ApiError && update.error.message
      ? update.error.message
      : t("saveError")
    : null;

  // WP17 — localize the field label client-side. The backend setting key may
  // contain dots; next-intl treats dots as path separators, so sanitize them
  // to underscores for the lookup. Fall back to the backend-provided label
  // (Turkish) for any key not yet present in the i18n catalog.
  const labelKey = `labels.${setting.key.replaceAll(".", "_")}`;
  const localizedLabel = tDynamic(t, labelKey, setting.label);

  return (
    <div className="flex flex-col gap-2 py-3 sm:flex-row sm:items-start sm:justify-between sm:gap-6">
      {/* Label + description + impact */}
      <div className="min-w-0 sm:max-w-[55%]">
        <div className="flex flex-wrap items-center gap-2">
          <span className="text-sm font-medium text-gray-900">{localizedLabel}</span>
          <ImpactBadge impact={impact} />
        </div>
        {setting.description && (
          <p className="mt-0.5 text-xs text-gray-500">{setting.description}</p>
        )}
        <p className="mt-0.5 font-mono text-[11px] text-gray-400">{setting.key}</p>
      </div>

      {/* Value + edit controls */}
      <div className="flex shrink-0 flex-col gap-1 sm:items-end">
        {editing ? (
          <>
            <SettingInput
              valueType={setting.valueType}
              value={draft}
              unit={setting.unit}
              disabled={update.isPending}
              ariaLabel={setting.label}
              booleanLabels={{ yes: t("boolean.yes"), no: t("boolean.no") }}
              onChange={setDraft}
            />
            <div className="flex gap-2">
              <button
                type="button"
                onClick={save}
                disabled={update.isPending}
                className="rounded-md bg-blue-600 px-3 py-1.5 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-50"
              >
                {t("save")}
              </button>
              <button
                type="button"
                onClick={cancel}
                disabled={update.isPending}
                className="rounded-md border border-gray-300 px-3 py-1.5 text-sm font-medium text-gray-700 hover:bg-gray-50 disabled:opacity-50"
              >
                {t("cancel")}
              </button>
            </div>
            {errorMessage && <p className="text-xs text-red-600 sm:text-right">{errorMessage}</p>}
          </>
        ) : (
          <div className="flex items-center gap-3 sm:justify-end">
            <span className="text-sm font-semibold text-gray-900">{displayValue}</span>
            <button
              type="button"
              onClick={startEdit}
              className="rounded-md border border-gray-300 px-3 py-1.5 text-sm font-medium text-gray-700 hover:bg-gray-50"
            >
              {t("edit")}
            </button>
          </div>
        )}
      </div>
    </div>
  );
}
