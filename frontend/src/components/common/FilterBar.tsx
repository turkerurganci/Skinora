"use client";

import { ReactNode, useState } from "react";
import { useTranslations } from "next-intl";
import { cn } from "@/lib/utils/cn";

export type FilterValue = string | number | null;

export interface FilterFieldOption {
  value: string;
  label: string;
}

export interface FilterField {
  key: string;
  label: string;
  kind: "select" | "text" | "date";
  options?: FilterFieldOption[];
  placeholder?: string;
}

export interface ActiveFilter {
  key: string;
  label: string;
  value: string;
}

export interface FilterBarProps {
  fields: FilterField[];
  initialValues?: Record<string, string>;
  onApply: (values: Record<string, string>) => void;
  onClear?: () => void;
  className?: string;
  extra?: ReactNode;
}

export function FilterBar({
  fields,
  initialValues = {},
  onApply,
  onClear,
  className,
  extra,
}: FilterBarProps) {
  const t = useTranslations("filterBar");
  const [values, setValues] = useState<Record<string, string>>(initialValues);
  const [applied, setApplied] = useState<Record<string, string>>(initialValues);

  function handleApply() {
    onApply(values);
    setApplied(values);
  }

  function handleClear() {
    setValues({});
    setApplied({});
    onClear?.();
  }

  function handleRemoveChip(key: string) {
    const next = { ...values };
    delete next[key];
    setValues(next);
    setApplied(next);
    onApply(next);
  }

  const activeChips = Object.entries(applied)
    .filter(([, v]) => v && v.length > 0)
    .map(([key, value]) => {
      const field = fields.find((f) => f.key === key);
      const label = field?.options?.find((o) => o.value === value)?.label ?? value;
      return { key, fieldLabel: field?.label ?? key, value: label };
    });

  return (
    <div
      className={cn(
        "flex flex-col gap-3 rounded-lg border border-gray-200 bg-white p-3",
        className,
      )}
    >
      <div className="flex flex-wrap items-end gap-3">
        {fields.map((field) => (
          <label key={field.key} className="flex min-w-[150px] flex-1 flex-col gap-1 text-xs">
            <span className="font-medium text-gray-700">{field.label}</span>
            {field.kind === "select" ? (
              <select
                value={values[field.key] ?? ""}
                onChange={(e) => setValues((v) => ({ ...v, [field.key]: e.target.value }))}
                className="rounded-md border border-gray-300 bg-white px-2 py-1.5 text-sm focus:outline-none focus:ring-2 focus:ring-blue-200"
              >
                <option value="">{field.placeholder ?? t("any")}</option>
                {field.options?.map((opt) => (
                  <option key={opt.value} value={opt.value}>
                    {opt.label}
                  </option>
                ))}
              </select>
            ) : (
              <input
                type={field.kind === "date" ? "date" : "text"}
                value={values[field.key] ?? ""}
                placeholder={field.placeholder}
                onChange={(e) => setValues((v) => ({ ...v, [field.key]: e.target.value }))}
                className="rounded-md border border-gray-300 bg-white px-2 py-1.5 text-sm focus:outline-none focus:ring-2 focus:ring-blue-200"
              />
            )}
          </label>
        ))}
        <div className="flex items-end gap-2">
          <button
            type="button"
            onClick={handleApply}
            className="rounded-md bg-blue-600 px-3 py-2 text-sm font-medium text-white hover:bg-blue-700"
          >
            {t("apply")}
          </button>
          <button
            type="button"
            onClick={handleClear}
            className="rounded-md px-3 py-2 text-sm text-gray-600 hover:text-gray-900 underline"
          >
            {t("clear")}
          </button>
        </div>
        {extra}
      </div>
      {activeChips.length > 0 && (
        <div className="flex flex-wrap gap-2">
          {activeChips.map((chip) => (
            <span
              key={chip.key}
              className="inline-flex items-center gap-1 rounded-full bg-blue-100 px-2.5 py-0.5 text-xs text-blue-800"
            >
              <span className="font-medium">{chip.fieldLabel}:</span>
              <span>{chip.value}</span>
              <button
                type="button"
                onClick={() => handleRemoveChip(chip.key)}
                className="ml-1 text-blue-600 hover:text-blue-900"
                aria-label={t("removeFilter", { label: chip.fieldLabel })}
              >
                ×
              </button>
            </span>
          ))}
        </div>
      )}
    </div>
  );
}
