"use client";

import Link from "next/link";
import { useLocale, useTranslations } from "next-intl";
import { ResponsiveTable } from "@/components/common";
import type { ResponsiveTableColumn } from "@/components/common";
import { cn } from "@/lib/utils/cn";
import { formatDateTime, formatStablecoin } from "@/lib/utils/format";
import type { AdminFlagListItem, AdminFlagParty, AdminFlagScope } from "@/lib/api/admin";
import { FlagReviewStatusBadge } from "./FlagReviewStatusBadge";

function shortId(id: string): string {
  return id.slice(0, 8);
}

function FlagPartyCell({ party }: { party: AdminFlagParty | null }) {
  if (!party) return <span className="text-sm text-gray-400">—</span>;
  return (
    <span className="inline-flex items-center gap-2">
      {party.avatarUrl ? (
        // eslint-disable-next-line @next/next/no-img-element
        <img src={party.avatarUrl} alt="" className="h-6 w-6 rounded-full bg-gray-100" />
      ) : (
        <span className="h-6 w-6 rounded-full bg-gray-200" aria-hidden="true" />
      )}
      <span className="text-sm text-gray-900">{party.displayName}</span>
    </span>
  );
}

export interface FlagQueueTableProps {
  flags: readonly AdminFlagListItem[];
  /** Selected category filter — drives the column set (undefined = "Tümü"). */
  category?: AdminFlagScope;
  className?: string;
}

/**
 * S13 flag-queue table (04 §8.2). Column set adapts to the selected category:
 * transaction flags surface item / amount / market-price; account flags drop
 * those tx-only columns; "Tümü" adds a category column. The account-flag
 * signal columns (Sinyal Detayı / İlişkili Hesaplar / Aktif İşlem Sayısı) are
 * not carried by the AD2 list projection (07 §9.2). The S14 detail surfaces
 * the multi-account signal + linked accounts only; the per-user active-tx
 * count/list and the IP/device signal are not yet projected by AD2/AD3 and
 * are deferred to a backend DTO-expansion task (see T100 report K2/K9/K10).
 */
export function FlagQueueTable({ flags, category, className }: FlagQueueTableProps) {
  const t = useTranslations("adminFlags");
  const tType = useTranslations("adminFlags.type");
  const tScope = useTranslations("adminFlags.scope");
  const locale = useLocale();

  const idColumn: ResponsiveTableColumn<AdminFlagListItem> = {
    key: "id",
    header: t("columns.id"),
    cell: (row) => (
      <Link
        href={`/${locale}/admin/flags/${row.id}`}
        className="font-mono text-xs text-blue-600 hover:text-blue-700"
      >
        {shortId(row.id)}
      </Link>
    ),
  };
  const scopeColumn: ResponsiveTableColumn<AdminFlagListItem> = {
    key: "scope",
    header: t("columns.category"),
    cell: (row) => <span className="text-sm text-gray-700">{tScope(row.scope)}</span>,
  };
  const typeColumn: ResponsiveTableColumn<AdminFlagListItem> = {
    key: "type",
    header: t("columns.type"),
    cell: (row) => <span className="text-sm text-gray-900">{tType(row.type)}</span>,
  };
  const userColumn: ResponsiveTableColumn<AdminFlagListItem> = {
    key: "user",
    header: t("columns.user"),
    cell: (row) => <FlagPartyCell party={row.seller} />,
  };
  const itemColumn: ResponsiveTableColumn<AdminFlagListItem> = {
    key: "item",
    header: t("columns.item"),
    cell: (row) => <span className="text-sm text-gray-900">{row.itemName ?? "—"}</span>,
  };
  const amountColumn: ResponsiveTableColumn<AdminFlagListItem> = {
    key: "amount",
    header: t("columns.amount"),
    cell: (row) => (
      <span className="text-sm tabular-nums text-gray-900">
        {row.price !== null && row.stablecoin ? formatStablecoin(row.price, row.stablecoin) : "—"}
      </span>
    ),
  };
  const marketColumn: ResponsiveTableColumn<AdminFlagListItem> = {
    key: "market",
    header: t("columns.marketPrice"),
    cell: (row) => (
      <span className="text-sm tabular-nums text-gray-700">
        {row.marketPrice !== null && row.stablecoin
          ? formatStablecoin(row.marketPrice, row.stablecoin)
          : "—"}
      </span>
    ),
  };
  const dateColumn: ResponsiveTableColumn<AdminFlagListItem> = {
    key: "createdAt",
    header: t("columns.date"),
    cell: (row) => (
      <time dateTime={row.createdAt} className="text-sm tabular-nums text-gray-700">
        {formatDateTime(row.createdAt, locale)}
      </time>
    ),
  };
  const statusColumn: ResponsiveTableColumn<AdminFlagListItem> = {
    key: "status",
    header: t("columns.status"),
    cell: (row) => <FlagReviewStatusBadge status={row.reviewStatus} />,
  };

  let columns: ReadonlyArray<ResponsiveTableColumn<AdminFlagListItem>>;
  if (category === "TRANSACTION_PRE_CREATE") {
    columns = [
      idColumn,
      typeColumn,
      userColumn,
      itemColumn,
      amountColumn,
      marketColumn,
      dateColumn,
      statusColumn,
    ];
  } else if (category === "ACCOUNT_LEVEL") {
    columns = [idColumn, userColumn, typeColumn, dateColumn, statusColumn];
  } else {
    columns = [idColumn, scopeColumn, typeColumn, userColumn, dateColumn, statusColumn];
  }

  return (
    <ResponsiveTable
      data={flags}
      columns={columns}
      getRowKey={(row) => row.id}
      ariaLabel={t("tableAriaLabel")}
      emptyMessage={t("empty")}
      className={cn(className)}
    />
  );
}
