"use client";

import { useTranslations } from "next-intl";
import { ResponsiveTable } from "@/components/common";
import type { ResponsiveTableColumn } from "@/components/common";
import { cn } from "@/lib/utils/cn";

/**
 * Forward-declared shape of one S18 recovery-queue row (04 §8.7). NO endpoint
 * populates this yet: AD10 returns only an aggregate `recoveryTransactionCount`
 * (always 0 — deferred to the T69 bot-health / failover pipeline). Under the
 * owner-approved T103 Option A the panel renders structurally (columns visible
 * for spec traceability) but always receives an empty list. When a future task
 * wires the recovery pipeline into AD10 (or a dedicated endpoint), this type
 * moves to `lib/api/admin.ts` and the table populates with no UI change.
 */
export interface RecoveryQueueRow {
  transactionId: string;
  itemName: string;
  seller: string;
  buyer: string;
  transactionState: string;
  recoveryStatus: string;
  responsibleAdmin: string | null;
  adminNote: string | null;
}

export interface RecoveryQueuePanelProps {
  rows: readonly RecoveryQueueRow[];
  className?: string;
}

/**
 * S18 recovery queue (04 §8.7) — active transactions tied to items escrowed on
 * a restricted/banned bot. Columns: İşlem ID (→ S16), Item, Satıcı/Alıcı,
 * İşlem State, Recovery Durumu, Sorumlu Admin, Admin Notu. The row data and the
 * MANAGE_STEAM_RECOVERY actions (Manual Recovery / not ekle / sorumlu admin
 * atama) are deferred to T69 (see {@link RecoveryQueueRow}); the panel shows
 * the empty state today and a footnote explaining when it activates.
 */
export function RecoveryQueuePanel({ rows, className }: RecoveryQueuePanelProps) {
  const t = useTranslations("adminSteamAccounts.recovery");

  const columns: ReadonlyArray<ResponsiveTableColumn<RecoveryQueueRow>> = [
    { key: "transactionId", header: t("columns.transactionId"), cell: (r) => r.transactionId },
    { key: "item", header: t("columns.item"), cell: (r) => r.itemName },
    { key: "parties", header: t("columns.parties"), cell: (r) => `${r.seller} / ${r.buyer}` },
    { key: "state", header: t("columns.state"), cell: (r) => r.transactionState },
    { key: "recoveryStatus", header: t("columns.recoveryStatus"), cell: (r) => r.recoveryStatus },
    {
      key: "responsibleAdmin",
      header: t("columns.responsibleAdmin"),
      cell: (r) => r.responsibleAdmin ?? "—",
    },
    { key: "note", header: t("columns.note"), cell: (r) => r.adminNote ?? "—" },
  ];

  return (
    <section className={cn("rounded-lg border border-gray-200 bg-white p-4 shadow-sm", className)}>
      <h2 className="text-sm font-semibold text-gray-900">{t("title")}</h2>
      <p className="mt-1 text-xs text-gray-500">{t("description")}</p>

      <ResponsiveTable
        data={rows}
        columns={columns}
        getRowKey={(r) => r.transactionId}
        ariaLabel={t("ariaLabel")}
        emptyMessage={t("empty")}
        className="mt-3"
      />

      <p className="mt-2 text-[11px] text-gray-400">{t("deferred")}</p>
    </section>
  );
}
