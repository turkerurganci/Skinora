"use client";

import Link from "next/link";
import { useLocale, useTranslations } from "next-intl";
import { ResponsiveTable, Skeleton } from "@/components/common";
import type { ResponsiveTableColumn } from "@/components/common";
import { cn } from "@/lib/utils/cn";
import { formatDateTime } from "@/lib/utils/format";
import type { AdminDashboardRecentFlag } from "@/lib/api/admin";

export interface RecentFlagsTableProps {
  flags: readonly AdminDashboardRecentFlag[] | undefined;
  isLoading: boolean;
  isError: boolean;
  className?: string;
}

function shortenId(id: string): string {
  // Render only the first 8 chars of the GUID; admins recognize flags by the
  // detail page anyway, the prefix is enough for at-a-glance recall.
  return id.slice(0, 8);
}

/**
 * S12 "Last 5 flags" table (04 §8.1). Uses the shared ResponsiveTable so the
 * mobile breakpoint falls back to the 04 §9.4 card list automatically.
 */
export function RecentFlagsTable({ flags, isLoading, isError, className }: RecentFlagsTableProps) {
  const t = useTranslations("adminDashboard.recentFlags");
  const tType = useTranslations("adminDashboard.flagType");
  const tStatus = useTranslations("adminDashboard.flagStatus");
  const locale = useLocale();

  const wrapper = cn("rounded-lg border border-gray-200 bg-white p-4 shadow-sm", className);

  const header = (
    <div className="mb-3 flex items-center justify-between">
      <h2 className="text-sm font-semibold text-gray-900">{t("title")}</h2>
      <Link
        href={`/${locale}/admin/flags`}
        className="text-xs font-medium text-blue-600 hover:text-blue-700"
      >
        {t("viewAll")}
      </Link>
    </div>
  );

  if (isLoading) {
    return (
      <section className={wrapper} aria-busy="true" aria-label={t("title")}>
        {header}
        <div className="flex flex-col gap-2">
          {[0, 1, 2].map((i) => (
            <Skeleton key={i} className="h-10" />
          ))}
        </div>
      </section>
    );
  }

  if (isError || !flags) {
    return (
      <section className={wrapper} aria-label={t("title")}>
        {header}
        <p className="text-sm text-gray-500">{t("loadError")}</p>
      </section>
    );
  }

  const columns: ReadonlyArray<ResponsiveTableColumn<AdminDashboardRecentFlag>> = [
    {
      key: "id",
      header: t("columns.id"),
      cell: (row) => (
        <Link
          href={`/${locale}/admin/flags/${row.id}`}
          className="font-mono text-xs text-blue-600 hover:text-blue-700"
        >
          {shortenId(row.id)}
        </Link>
      ),
    },
    {
      key: "type",
      header: t("columns.type"),
      cell: (row) => <span className="text-sm text-gray-900">{tType(row.type)}</span>,
    },
    {
      key: "createdAt",
      header: t("columns.createdAt"),
      cell: (row) => (
        <time dateTime={row.createdAt} className="text-sm text-gray-700 tabular-nums">
          {formatDateTime(row.createdAt, locale)}
        </time>
      ),
    },
    {
      key: "status",
      header: t("columns.status"),
      cell: (row) => {
        const tone =
          row.reviewStatus === "PENDING"
            ? "bg-amber-50 text-amber-800"
            : row.reviewStatus === "APPROVED"
              ? "bg-emerald-50 text-emerald-800"
              : "bg-gray-100 text-gray-800";
        return (
          <span
            className={cn(
              "inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium",
              tone,
            )}
          >
            {tStatus(row.reviewStatus)}
          </span>
        );
      },
    },
  ];

  return (
    <section className={wrapper} aria-label={t("title")}>
      {header}
      <ResponsiveTable
        data={flags}
        columns={columns}
        getRowKey={(row) => row.id}
        ariaLabel={t("title")}
        emptyMessage={t("empty")}
      />
    </section>
  );
}
