"use client";

import { useState, type ReactNode } from "react";
import Link from "next/link";
import { useLocale, useTranslations } from "next-intl";
import { Pagination, ResponsiveTable, Skeleton } from "@/components/common";
import type { ResponsiveTableColumn } from "@/components/common";
import { TransactionListTable } from "./TransactionListTable";
import { UserProfileCard } from "./UserProfileCard";
import { UserStatsCard } from "./UserStatsCard";
import { useAdminUserTransactions } from "@/lib/hooks/useAdminUserTransactions";
import { cn } from "@/lib/utils/cn";
import { formatDate } from "@/lib/utils/format";
import type {
  AdminUserCounterparty,
  AdminUserDetail,
  AdminUserDisputeEntry,
  AdminUserFlagEntry,
  AdminUserWalletEntry,
} from "@/lib/api/admin";

function SectionCard({
  title,
  action,
  children,
}: {
  title: string;
  action?: ReactNode;
  children: ReactNode;
}) {
  return (
    <section className="rounded-lg border border-gray-200 bg-white p-5">
      <div className="mb-4 flex items-center justify-between gap-2">
        <h2 className="text-base font-semibold text-gray-900">{title}</h2>
        {action}
      </div>
      {children}
    </section>
  );
}

function PillBadge({ tone, children }: { tone: string; children: ReactNode }) {
  return (
    <span
      className={cn(
        "inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium ring-1 ring-inset",
        tone,
      )}
    >
      {children}
    </span>
  );
}

const FLAG_STATUS_TONE: Record<string, string> = {
  PENDING: "bg-yellow-100 text-yellow-800 ring-yellow-200",
  APPROVED: "bg-green-100 text-green-800 ring-green-300",
  REJECTED: "bg-gray-100 text-gray-700 ring-gray-300",
};

const DISPUTE_STATUS_TONE: Record<string, string> = {
  OPEN: "bg-blue-100 text-blue-800 ring-blue-200",
  ESCALATED: "bg-orange-100 text-orange-800 ring-orange-200",
  CLOSED: "bg-gray-100 text-gray-700 ring-gray-300",
};

function TxLink({ id, locale }: { id: string; locale: string }) {
  return (
    <Link
      href={`/${locale}/admin/transactions/${id}`}
      className="font-mono text-xs text-blue-600 hover:text-blue-700"
    >
      {id.slice(0, 8)}
    </Link>
  );
}

/** 04 §8.9.4 — per-user transaction history (AD16b), paginated. */
function UserTransactionsSection({ steamId }: { steamId: string }) {
  const t = useTranslations("adminUserDetail");
  const locale = useLocale();
  const [page, setPage] = useState(1);
  const { data, isLoading, isError } = useAdminUserTransactions(steamId, page);

  const totalPages = data ? Math.max(1, Math.ceil(data.totalCount / data.pageSize)) : 1;

  return (
    <SectionCard
      title={t("transactions.heading")}
      action={
        <Link
          href={`/${locale}/admin/transactions?search=${encodeURIComponent(steamId)}`}
          className="text-sm text-blue-600 hover:text-blue-700"
        >
          {t("transactions.viewAll")}
        </Link>
      }
    >
      {isLoading ? (
        <Skeleton className="h-24" />
      ) : isError ? (
        <p className="text-sm text-red-600">{t("loadError")}</p>
      ) : !data || data.items.length === 0 ? (
        <p className="rounded-lg border border-gray-200 bg-white p-6 text-center text-sm text-gray-500">
          {t("transactions.empty")}
        </p>
      ) : (
        <div className="flex flex-col gap-4">
          <TransactionListTable transactions={data.items} />
          {totalPages > 1 ? (
            <Pagination currentPage={page} totalPages={totalPages} onPageChange={setPage} />
          ) : null}
        </div>
      )}
    </SectionCard>
  );
}

export interface UserDetailViewProps {
  steamId: string;
  detail: AdminUserDetail;
}

/** S20 — Admin User Detail (04 §8.9). */
export function UserDetailView({ steamId, detail }: UserDetailViewProps) {
  const t = useTranslations("adminUserDetail");
  const locale = useLocale();

  const walletColumns: ReadonlyArray<ResponsiveTableColumn<AdminUserWalletEntry>> = [
    { key: "type", header: t("wallet.columns.type"), cell: (r) => t(`wallet.type.${r.type}`) },
    {
      key: "address",
      header: t("wallet.columns.address"),
      cell: (r) => (
        <span className="flex items-center gap-2">
          <span className="font-mono text-xs break-all text-gray-900">{r.address}</span>
          {r.current ? (
            <PillBadge tone="bg-emerald-100 text-emerald-800 ring-emerald-200">
              {t("wallet.current")}
            </PillBadge>
          ) : null}
        </span>
      ),
    },
    {
      key: "setAt",
      header: t("wallet.columns.setAt"),
      cell: (r) => (r.setAt ? formatDate(r.setAt, locale) : "—"),
    },
  ];

  const flagColumns: ReadonlyArray<ResponsiveTableColumn<AdminUserFlagEntry>> = [
    { key: "type", header: t("flags.columns.type"), cell: (r) => t(`flags.type.${r.type}`) },
    {
      key: "transaction",
      header: t("flags.columns.transaction"),
      cell: (r) =>
        r.transactionId ? (
          <TxLink id={r.transactionId} locale={locale} />
        ) : (
          <span className="text-xs text-gray-400">{t("flags.accountLevel")}</span>
        ),
    },
    {
      key: "status",
      header: t("flags.columns.status"),
      cell: (r) => (
        <PillBadge tone={FLAG_STATUS_TONE[r.reviewStatus]}>
          {t(`flags.status.${r.reviewStatus}`)}
        </PillBadge>
      ),
    },
    {
      key: "date",
      header: t("flags.columns.date"),
      cell: (r) => formatDate(r.createdAt, locale),
    },
  ];

  const disputeColumns: ReadonlyArray<ResponsiveTableColumn<AdminUserDisputeEntry>> = [
    {
      key: "type",
      header: t("disputes.columns.type"),
      cell: (r) => t(`disputes.type.${r.type}`),
    },
    {
      key: "transaction",
      header: t("disputes.columns.transaction"),
      cell: (r) => <TxLink id={r.transactionId} locale={locale} />,
    },
    {
      key: "status",
      header: t("disputes.columns.status"),
      cell: (r) => (
        <PillBadge tone={DISPUTE_STATUS_TONE[r.status]}>
          {t(`disputes.status.${r.status}`)}
        </PillBadge>
      ),
    },
    {
      key: "date",
      header: t("disputes.columns.date"),
      cell: (r) => formatDate(r.createdAt, locale),
    },
  ];

  const counterpartyColumns: ReadonlyArray<ResponsiveTableColumn<AdminUserCounterparty>> = [
    {
      key: "user",
      header: t("counterparties.columns.user"),
      cell: (r) =>
        r.steamId ? (
          <Link
            href={`/${locale}/admin/users/${encodeURIComponent(r.steamId)}`}
            className="text-sm text-blue-600 hover:underline"
          >
            {r.displayName || r.steamId}
          </Link>
        ) : (
          // Anonymized/deleted counterparty (02 §19): no profile to link to, so
          // render the "Deleted User" placeholder as plain text instead of a
          // blank, broken link — same treatment as the sibling AD7 deleted party.
          <span className="text-sm text-gray-500">{r.displayName}</span>
        ),
    },
    {
      key: "count",
      header: t("counterparties.columns.count"),
      cell: (r) => String(r.transactionCount),
    },
    {
      key: "lastTransaction",
      header: t("counterparties.columns.lastTransaction"),
      cell: (r) => (r.lastTransactionAt ? formatDate(r.lastTransactionAt, locale) : "—"),
    },
  ];

  return (
    <div className="flex flex-col gap-6">
      <UserProfileCard profile={detail.profile} />
      <UserStatsCard stats={detail.stats} />

      <SectionCard title={t("wallet.heading")}>
        <ResponsiveTable
          data={detail.walletHistory}
          columns={walletColumns}
          getRowKey={(r) => `${r.type}-${r.address}`}
          ariaLabel={t("wallet.heading")}
          emptyMessage={t("wallet.empty")}
        />
        <p className="mt-3 text-xs text-gray-500">{t("wallet.historyNote")}</p>
      </SectionCard>

      <UserTransactionsSection steamId={steamId} />

      <SectionCard title={t("flags.heading")}>
        <ResponsiveTable
          data={detail.flagHistory}
          columns={flagColumns}
          getRowKey={(r) => r.id}
          ariaLabel={t("flags.heading")}
          emptyMessage={t("flags.empty")}
        />
      </SectionCard>

      <SectionCard title={t("disputes.heading")}>
        <ResponsiveTable
          data={detail.disputeHistory}
          columns={disputeColumns}
          getRowKey={(r) => r.id}
          ariaLabel={t("disputes.heading")}
          emptyMessage={t("disputes.empty")}
        />
      </SectionCard>

      <SectionCard title={t("counterparties.heading")}>
        <ResponsiveTable
          data={detail.frequentCounterparties}
          columns={counterpartyColumns}
          getRowKey={(r) => r.steamId || `deleted-${detail.frequentCounterparties.indexOf(r)}`}
          ariaLabel={t("counterparties.heading")}
          emptyMessage={t("counterparties.empty")}
        />
      </SectionCard>
    </div>
  );
}
