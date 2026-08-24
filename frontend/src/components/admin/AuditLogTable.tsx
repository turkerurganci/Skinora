"use client";

import Link from "next/link";
import { useLocale, useTranslations } from "next-intl";
import { ResponsiveTable } from "@/components/common";
import type { ResponsiveTableColumn } from "@/components/common";
import { cn } from "@/lib/utils/cn";
import { formatDateTime } from "@/lib/utils/format";
import type { AdminAuditLogItem, AuditLogParticipant } from "@/lib/api/admin";
import { AuditCategoryBadge } from "./AuditCategoryBadge";
import { tDynamic } from "@/lib/i18n/dynamicKey";

function shortId(id: string): string {
  return id.slice(0, 8);
}

/**
 * Audit participant (actor or subject). Links to S20 user detail when the
 * participant is a real user (04 §8.10 "Kullanıcı ... tıklanabilir → S20");
 * the SYSTEM account has no Steam ID, so it renders as plain text.
 */
function ParticipantName({
  participant,
  locale,
}: {
  participant: AuditLogParticipant;
  locale: string;
}) {
  if (!participant.steamId) {
    return <span className="text-sm text-gray-700">{participant.displayName}</span>;
  }
  return (
    <Link
      href={`/${locale}/admin/users/${encodeURIComponent(participant.steamId)}`}
      className="text-sm text-blue-600 hover:underline"
    >
      {participant.displayName}
    </Link>
  );
}

/**
 * Renders the opaque `detail` JSON (07 §9.19) read-only and compact: an object
 * becomes a key:value list, anything else (e.g. the non-JSON string fallback the
 * backend wraps) prints verbatim. No shared component exists for this shape.
 */
function DetailCell({ detail }: { detail: unknown }) {
  if (detail === null || detail === undefined) {
    return <span className="text-sm text-gray-400">—</span>;
  }
  if (typeof detail === "object" && !Array.isArray(detail)) {
    const entries = Object.entries(detail as Record<string, unknown>);
    if (entries.length === 0) return <span className="text-sm text-gray-400">—</span>;
    return (
      <dl className="grid grid-cols-[auto,1fr] gap-x-2 gap-y-0.5 text-xs">
        {entries.map(([k, v]) => (
          <div key={k} className="contents">
            <dt className="font-medium text-gray-500">{k}</dt>
            <dd className="break-all font-mono text-gray-800">
              {typeof v === "object" ? JSON.stringify(v) : String(v)}
            </dd>
          </div>
        ))}
      </dl>
    );
  }
  return <span className="break-all font-mono text-xs text-gray-800">{String(detail)}</span>;
}

export interface AuditLogTableProps {
  entries: readonly AdminAuditLogItem[];
  className?: string;
}

/**
 * S21 audit-log table (04 §8.10). Six columns — date/time, category, action,
 * user (actor + distinct subject, both → S20), transaction id (→ S16) and the
 * detail payload. The action enum is localized client-side by key (the backend
 * returns the raw enum name); an unmapped/future action falls back to its raw
 * name (T104 permission-label precedent). Desktop renders a semantic table;
 * mobile collapses to cards via {@link ResponsiveTable} (04 §9.4).
 */
export function AuditLogTable({ entries, className }: AuditLogTableProps) {
  const t = useTranslations("adminAuditLog");
  const tAction = useTranslations("adminAuditLog.action");
  const locale = useLocale();

  const columns: ReadonlyArray<ResponsiveTableColumn<AdminAuditLogItem>> = [
    {
      key: "createdAt",
      header: t("columns.createdAt"),
      cell: (row) => (
        <time dateTime={row.createdAt} className="text-sm tabular-nums text-gray-700">
          {formatDateTime(row.createdAt, locale)}
        </time>
      ),
    },
    {
      key: "category",
      header: t("columns.category"),
      cell: (row) => <AuditCategoryBadge category={row.category} />,
    },
    {
      key: "action",
      header: t("columns.action"),
      cell: (row) => (
        <span className="text-sm text-gray-900">{tDynamic(tAction, row.action, row.action)}</span>
      ),
    },
    {
      key: "user",
      header: t("columns.user"),
      cell: (row) => (
        <div className="flex flex-col gap-0.5">
          <ParticipantName participant={row.actor} locale={locale} />
          {row.subject && (
            <span className="text-xs text-gray-500">
              {t("subjectLabel")}: <ParticipantName participant={row.subject} locale={locale} />
            </span>
          )}
        </div>
      ),
    },
    {
      key: "transactionId",
      header: t("columns.transactionId"),
      cell: (row) =>
        row.transactionId ? (
          <Link
            href={`/${locale}/admin/transactions/${row.transactionId}`}
            className="font-mono text-xs text-blue-600 hover:text-blue-700"
          >
            {shortId(row.transactionId)}
          </Link>
        ) : (
          <span className="text-sm text-gray-400">—</span>
        ),
    },
    {
      key: "detail",
      header: t("columns.detail"),
      cell: (row) => <DetailCell detail={row.detail} />,
    },
  ];

  return (
    <ResponsiveTable
      data={entries}
      columns={columns}
      getRowKey={(row) => row.id}
      ariaLabel={t("tableAriaLabel")}
      emptyMessage={t("empty")}
      className={cn(className)}
    />
  );
}
