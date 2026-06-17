"use client";

import { useLocale, useTranslations } from "next-intl";
import { ResponsiveTable } from "@/components/common";
import type { ResponsiveTableColumn } from "@/components/common";
import { cn } from "@/lib/utils/cn";
import { formatDateTime } from "@/lib/utils/format";
import { DisputeStatus } from "@/types/enums";
import type { AdminDisputeListItem } from "@/lib/api/admin";
import { DisputeStatusBadge } from "./DisputeStatusBadge";

export interface DisputeQueueTableProps {
  disputes: readonly AdminDisputeListItem[];
  /** Opens the resolve modal for an ESCALATED row. */
  onResolve: (dispute: AdminDisputeListItem) => void;
  className?: string;
}

/**
 * WP5 — admin dispute queue table (AD27, 07 §9.x). The action column surfaces a
 * "Resolve" button only for ESCALATED rows (the dead-end disputes); resolved
 * rows show their terminal status badge.
 */
export function DisputeQueueTable({ disputes, onResolve, className }: DisputeQueueTableProps) {
  const t = useTranslations("adminDisputes");
  const tType = useTranslations("adminDisputes.type");
  const locale = useLocale();

  const columns: ReadonlyArray<ResponsiveTableColumn<AdminDisputeListItem>> = [
    {
      key: "id",
      header: t("columns.id"),
      cell: (row) => <span className="font-mono text-xs text-gray-600">{row.id.slice(0, 8)}</span>,
    },
    {
      key: "type",
      header: t("columns.type"),
      cell: (row) => <span className="text-sm text-gray-900">{tType(row.type)}</span>,
    },
    {
      key: "item",
      header: t("columns.item"),
      cell: (row) => <span className="text-sm text-gray-900">{row.itemName}</span>,
    },
    {
      key: "txStatus",
      header: t("columns.transactionStatus"),
      cell: (row) => <span className="text-sm text-gray-700">{row.transactionStatus}</span>,
    },
    {
      key: "openedBy",
      header: t("columns.openedBy"),
      cell: (row) => <span className="text-sm text-gray-900">{row.openedBy.displayName}</span>,
    },
    {
      key: "createdAt",
      header: t("columns.date"),
      cell: (row) => (
        <time dateTime={row.createdAt} className="text-sm tabular-nums text-gray-700">
          {formatDateTime(row.createdAt, locale)}
        </time>
      ),
    },
    {
      key: "status",
      header: t("columns.status"),
      cell: (row) => <DisputeStatusBadge status={row.status} />,
    },
    {
      key: "actions",
      header: t("columns.actions"),
      cell: (row) =>
        row.status === DisputeStatus.ESCALATED ? (
          <button
            type="button"
            onClick={() => onResolve(row)}
            className="rounded-md bg-blue-600 px-2.5 py-1 text-xs font-medium text-white hover:bg-blue-700"
          >
            {t("actions.resolve")}
          </button>
        ) : (
          <span className="text-xs text-gray-400">—</span>
        ),
    },
  ];

  return (
    <ResponsiveTable
      data={disputes}
      columns={columns}
      getRowKey={(row) => row.id}
      ariaLabel={t("tableAriaLabel")}
      emptyMessage={t("empty")}
      className={cn(className)}
    />
  );
}
