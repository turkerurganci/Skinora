"use client";

import { useState } from "react";
import Link from "next/link";
import { useLocale, useTranslations } from "next-intl";
import { ResponsiveTable, StatusBadge } from "@/components/common";
import type { ResponsiveTableColumn } from "@/components/common";
import { cn } from "@/lib/utils/cn";
import type {
  BotRecoveryQueueItem,
  BotRecoveryStatus,
  UpdateBotRecoveryRequest,
} from "@/lib/api/admin";

function shortId(id: string): string {
  return id.slice(0, 8);
}

const RECOVERY_TONE: Record<BotRecoveryStatus, string> = {
  PENDING: "bg-amber-100 text-amber-800",
  IN_REVIEW: "bg-blue-100 text-blue-800",
  RESOLVED: "bg-emerald-100 text-emerald-700",
};

export interface RecoveryQueuePanelProps {
  botName: string;
  items: readonly BotRecoveryQueueItem[];
  onUpdate: (id: string, body: UpdateBotRecoveryRequest) => void;
  pendingId: string | null;
  className?: string;
}

/**
 * S18 recovery queue for one restricted/banned bot (04 §8.7, T103b-2). Lists the
 * items still escrowed on the bot (each row = one stuck transaction) with the
 * MANAGE_STEAM_RECOVERY triage actions: İncele (→ S16 link), Manual Recovery
 * (→ IN_REVIEW), Çözüldü (→ RESOLVED) and Not Ekle (inline note editor).
 * EMERGENCY_HOLD / İptal are reached via the S16 transaction detail (the ID link).
 */
export function RecoveryQueuePanel({
  botName,
  items,
  onUpdate,
  pendingId,
  className,
}: RecoveryQueuePanelProps) {
  const t = useTranslations("adminSteamAccounts.recovery");
  const locale = useLocale();
  const [editingId, setEditingId] = useState<string | null>(null);
  const [draftNote, setDraftNote] = useState("");

  function startEdit(item: BotRecoveryQueueItem) {
    setEditingId(item.id);
    setDraftNote(item.adminNote ?? "");
  }

  function cancelEdit() {
    setEditingId(null);
    setDraftNote("");
  }

  function saveNote(item: BotRecoveryQueueItem) {
    onUpdate(item.id, { adminNote: draftNote });
    setEditingId(null);
  }

  function partyLabel(displayName: string | null, steamId: string | null): string {
    return displayName ?? steamId ?? "—";
  }

  const columns: ReadonlyArray<ResponsiveTableColumn<BotRecoveryQueueItem>> = [
    {
      key: "transactionId",
      header: t("columns.transactionId"),
      cell: (r) => (
        <Link
          href={`/${locale}/admin/transactions/${r.transactionId}`}
          className="font-mono text-xs text-blue-600 hover:text-blue-700"
        >
          {shortId(r.transactionId)}
        </Link>
      ),
    },
    { key: "item", header: t("columns.item"), cell: (r) => r.itemName },
    {
      key: "parties",
      header: t("columns.parties"),
      cell: (r) =>
        `${partyLabel(r.sellerDisplayName, r.sellerSteamId)} / ${partyLabel(
          r.buyerDisplayName,
          r.buyerSteamId,
        )}`,
    },
    {
      key: "state",
      header: t("columns.state"),
      cell: (r) => (
        <span className="inline-flex items-center gap-1">
          <StatusBadge status={r.statusAtRestriction} />
          {r.isOnHold && (
            <span className="rounded bg-gray-100 px-1 text-[10px] font-medium text-gray-600">
              {t("onHold")}
            </span>
          )}
        </span>
      ),
    },
    {
      key: "recoveryStatus",
      header: t("columns.recoveryStatus"),
      cell: (r) => (
        <span
          className={cn(
            "inline-flex rounded-full px-2 py-0.5 text-xs font-medium",
            RECOVERY_TONE[r.recoveryStatus],
          )}
        >
          {t(`statusValue.${r.recoveryStatus}`)}
        </span>
      ),
    },
    {
      key: "responsibleAdmin",
      header: t("columns.responsibleAdmin"),
      cell: (r) => r.responsibleAdminName ?? "—",
    },
    {
      key: "note",
      header: t("columns.note"),
      cell: (r) =>
        editingId === r.id ? (
          <div className="flex flex-col gap-1">
            <textarea
              value={draftNote}
              onChange={(e) => setDraftNote(e.target.value)}
              rows={2}
              maxLength={2000}
              className="w-full rounded border border-gray-300 p-1 text-xs"
              aria-label={t("columns.note")}
            />
            <div className="flex gap-2">
              <button
                type="button"
                onClick={() => saveNote(r)}
                disabled={pendingId === r.id}
                className="text-xs font-medium text-blue-600 hover:text-blue-700 disabled:opacity-50"
              >
                {t("actions.save")}
              </button>
              <button
                type="button"
                onClick={cancelEdit}
                className="text-xs text-gray-500 hover:text-gray-700"
              >
                {t("actions.cancel")}
              </button>
            </div>
          </div>
        ) : (
          <div className="flex items-start gap-1">
            <span className="text-gray-700">{r.adminNote ?? "—"}</span>
            {r.recoveryStatus !== "RESOLVED" && (
              <button
                type="button"
                onClick={() => startEdit(r)}
                className="shrink-0 text-[11px] text-blue-600 hover:text-blue-700"
              >
                {t("actions.editNote")}
              </button>
            )}
          </div>
        ),
    },
    {
      key: "actions",
      header: t("columns.actions"),
      cell: (r) => (
        <div className="flex flex-col gap-1">
          {r.recoveryStatus === "PENDING" && (
            <button
              type="button"
              onClick={() => onUpdate(r.id, { recoveryStatus: "IN_REVIEW" })}
              disabled={pendingId === r.id}
              className="text-left text-xs font-medium text-blue-600 hover:text-blue-700 disabled:opacity-50"
            >
              {t("actions.manualRecovery")}
            </button>
          )}
          {r.recoveryStatus !== "RESOLVED" && (
            <button
              type="button"
              onClick={() => onUpdate(r.id, { recoveryStatus: "RESOLVED" })}
              disabled={pendingId === r.id}
              className="text-left text-xs font-medium text-emerald-700 hover:text-emerald-800 disabled:opacity-50"
            >
              {t("actions.resolve")}
            </button>
          )}
          {r.recoveryStatus === "RESOLVED" && (
            <span className="text-xs text-gray-400">{t("actions.done")}</span>
          )}
        </div>
      ),
    },
  ];

  return (
    <section className={cn("rounded-lg border border-gray-200 bg-white p-4 shadow-sm", className)}>
      <h3 className="text-sm font-semibold text-gray-900">{t("titleFor", { bot: botName })}</h3>
      <p className="mt-1 text-xs text-gray-500">{t("description")}</p>

      <ResponsiveTable
        data={items}
        columns={columns}
        getRowKey={(r) => r.id}
        ariaLabel={t("ariaLabel")}
        emptyMessage={t("empty")}
        className="mt-3"
      />
    </section>
  );
}
