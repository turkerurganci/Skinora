"use client";

import Link from "next/link";
import { useLocale, useTranslations } from "next-intl";
import { ResponsiveTable, StatusBadge } from "@/components/common";
import type { ResponsiveTableColumn, ResponsiveTableSort } from "@/components/common";
import { cn } from "@/lib/utils/cn";
import { formatDateTime, formatStablecoin } from "@/lib/utils/format";
import type { AdminTransactionListItem, AdminTransactionParty } from "@/lib/api/admin";

function shortId(id: string): string {
  return id.slice(0, 8);
}

/**
 * Seller / buyer cell — avatar + name, linking to the S20 user detail
 * (04 §8.4 "tıklanabilir → S20"). S20 ships with T105, so the link 404s until
 * then (same forward-link pattern as the S12 dashboard deep-links, T99 K1).
 */
function TxPartyCell({ party, locale }: { party: AdminTransactionParty | null; locale: string }) {
  if (!party) return <span className="text-sm text-gray-400">—</span>;
  return (
    <Link
      href={`/${locale}/admin/users/${encodeURIComponent(party.steamId)}`}
      className="inline-flex items-center gap-2 hover:underline"
    >
      {party.avatarUrl ? (
        // eslint-disable-next-line @next/next/no-img-element
        <img src={party.avatarUrl} alt="" className="h-6 w-6 rounded-full bg-gray-100" />
      ) : (
        <span className="h-6 w-6 rounded-full bg-gray-200" aria-hidden="true" />
      )}
      <span className="text-sm text-gray-900">{party.displayName}</span>
    </Link>
  );
}

export interface TransactionListTableProps {
  transactions: readonly AdminTransactionListItem[];
  className?: string;
  /** Optional click-to-sort wiring (AD6 supports createdAt / price / status). */
  sort?: ResponsiveTableSort;
}

/**
 * S15 admin transaction table (04 §8.4). Eight columns — ID (→ S16), item,
 * price, seller (→ S20), buyer (→ S20), status, created, completed/cancelled.
 * Desktop renders a semantic table; mobile collapses to cards via
 * {@link ResponsiveTable} (04 §9.4).
 */
export function TransactionListTable({ transactions, className, sort }: TransactionListTableProps) {
  const t = useTranslations("adminTransactions");
  const locale = useLocale();

  const columns: ReadonlyArray<ResponsiveTableColumn<AdminTransactionListItem>> = [
    {
      key: "id",
      header: t("columns.id"),
      cell: (row) => (
        <Link
          href={`/${locale}/admin/transactions/${row.id}`}
          className="font-mono text-xs text-blue-600 hover:text-blue-700"
        >
          {shortId(row.id)}
        </Link>
      ),
    },
    {
      key: "item",
      header: t("columns.item"),
      cell: (row) => (
        <span className="inline-flex items-center gap-2">
          {row.itemImageUrl ? (
            // eslint-disable-next-line @next/next/no-img-element
            <img
              src={row.itemImageUrl}
              alt=""
              className="h-8 w-12 rounded bg-gray-100 object-cover"
            />
          ) : (
            <span className="h-8 w-12 rounded bg-gray-200" aria-hidden="true" />
          )}
          <span className="text-sm text-gray-900">{row.itemName}</span>
        </span>
      ),
    },
    {
      key: "price",
      header: t("columns.price"),
      sortKey: "price",
      cell: (row) => (
        <span className="text-sm tabular-nums text-gray-900">
          {formatStablecoin(row.price, row.stablecoin)}
        </span>
      ),
    },
    {
      key: "seller",
      header: t("columns.seller"),
      cell: (row) => <TxPartyCell party={row.seller} locale={locale} />,
    },
    {
      key: "buyer",
      header: t("columns.buyer"),
      cell: (row) => <TxPartyCell party={row.buyer} locale={locale} />,
    },
    {
      key: "status",
      header: t("columns.status"),
      sortKey: "status",
      cell: (row) => <StatusBadge status={row.status} />,
    },
    {
      key: "createdAt",
      header: t("columns.createdAt"),
      sortKey: "createdAt",
      cell: (row) => (
        <time dateTime={row.createdAt} className="text-sm tabular-nums text-gray-700">
          {formatDateTime(row.createdAt, locale)}
        </time>
      ),
    },
    {
      key: "completedAt",
      header: t("columns.completedAt"),
      cell: (row) =>
        row.completedAt ? (
          <time dateTime={row.completedAt} className="text-sm tabular-nums text-gray-700">
            {formatDateTime(row.completedAt, locale)}
          </time>
        ) : (
          <span className="text-sm text-gray-400">—</span>
        ),
    },
  ];

  return (
    <ResponsiveTable
      data={transactions}
      columns={columns}
      getRowKey={(row) => row.id}
      ariaLabel={t("tableAriaLabel")}
      emptyMessage={t("empty")}
      className={cn(className)}
      sort={sort}
    />
  );
}
