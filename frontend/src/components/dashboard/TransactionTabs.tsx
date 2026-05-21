"use client";

import { useTranslations } from "next-intl";
import { cn } from "@/lib/utils/cn";
import type { TransactionListTab } from "@/lib/api/transactions";

const TABS: TransactionListTab[] = ["active", "completed", "cancelled"];

export interface TransactionTabsProps {
  active: TransactionListTab;
  onChange: (tab: TransactionListTab) => void;
  className?: string;
}

export function TransactionTabs({ active, onChange, className }: TransactionTabsProps) {
  const t = useTranslations("dashboard.tabs");

  return (
    <div
      role="tablist"
      aria-label={t("ariaLabel")}
      className={cn("flex gap-1 border-b border-gray-200", className)}
    >
      {TABS.map((tab) => {
        const isActive = tab === active;
        return (
          <button
            key={tab}
            type="button"
            role="tab"
            aria-selected={isActive}
            aria-controls="transaction-list-panel"
            onClick={() => onChange(tab)}
            className={cn(
              "border-b-2 px-4 py-2 text-sm font-medium transition-colors",
              isActive
                ? "border-blue-600 text-blue-700"
                : "border-transparent text-gray-600 hover:border-gray-300 hover:text-gray-900",
            )}
          >
            {t(tab)}
          </button>
        );
      })}
    </div>
  );
}
