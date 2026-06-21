"use client";

import { useCallback, useMemo } from "react";
import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { useTranslations } from "next-intl";
import { ErrorState, FilterBar, Pagination, Skeleton } from "@/components/common";
import type { FilterField } from "@/components/common";
import { AuditLogTable } from "@/components/admin";
import { useAdminAuditLogList } from "@/lib/hooks/useAdminAuditLogList";
import type { AdminAuditCategory, AdminAuditLogQuery } from "@/lib/api/admin";
import { toEndOfDay } from "@/lib/utils/date";

const PAGE_SIZE = 20;

const CATEGORY_VALUES = ["FUND_MOVEMENT", "ADMIN_ACTION", "SECURITY_EVENT"] as const;

function parseEnum<T extends string>(value: string | null, allowed: readonly T[]): T | undefined {
  return value && (allowed as readonly string[]).includes(value) ? (value as T) : undefined;
}

/**
 * S21 — Admin Audit Log (04 §8.10, AD18). Filters (category / user search /
 * transaction id / date range) are synced to the URL so a filtered view is
 * shareable and survives refresh. Backend enforces VIEW_AUDIT_LOG (no client
 * guard — a 403 surfaces as the error state, T103 K5 precedent).
 */
export default function AdminAuditLogsPage() {
  const t = useTranslations("adminAuditLog");
  const tCategory = useTranslations("adminAuditLog.category");
  const router = useRouter();
  const pathname = usePathname();
  const searchParams = useSearchParams();

  const category = parseEnum<AdminAuditCategory>(searchParams.get("category"), CATEGORY_VALUES);
  const search = searchParams.get("search") ?? undefined;
  const transactionId = searchParams.get("transactionId") ?? undefined;
  const dateFrom = searchParams.get("dateFrom") ?? undefined;
  const dateTo = searchParams.get("dateTo") ?? undefined;
  const pageParam = Number(searchParams.get("page"));
  const page = Number.isFinite(pageParam) && pageParam > 0 ? pageParam : 1;

  const query: AdminAuditLogQuery = useMemo(
    () => ({
      category,
      search,
      transactionId,
      dateFrom,
      dateTo: toEndOfDay(dateTo),
      page,
      pageSize: PAGE_SIZE,
    }),
    [category, search, transactionId, dateFrom, dateTo, page],
  );

  const { data, isLoading, isError, refetch } = useAdminAuditLogList(query);

  const pushParams = useCallback(
    (next: Record<string, string | undefined>) => {
      const params = new URLSearchParams(searchParams.toString());
      for (const [k, v] of Object.entries(next)) {
        if (v && v.length > 0) params.set(k, v);
        else params.delete(k);
      }
      const qs = params.toString();
      router.replace(qs ? `${pathname}?${qs}` : pathname);
    },
    [router, pathname, searchParams],
  );

  const fields: FilterField[] = [
    {
      key: "category",
      label: t("filters.category"),
      kind: "select",
      placeholder: t("filters.allCategories"),
      options: CATEGORY_VALUES.map((v) => ({ value: v, label: tCategory(v) })),
    },
    {
      key: "search",
      label: t("filters.search"),
      kind: "text",
      placeholder: t("filters.searchPlaceholder"),
    },
    {
      key: "transactionId",
      label: t("filters.transactionId"),
      kind: "text",
      placeholder: t("filters.transactionIdPlaceholder"),
    },
    { key: "dateFrom", label: t("filters.dateFrom"), kind: "date" },
    { key: "dateTo", label: t("filters.dateTo"), kind: "date" },
  ];

  const initialValues: Record<string, string> = {};
  if (category) initialValues.category = category;
  if (search) initialValues.search = search;
  if (transactionId) initialValues.transactionId = transactionId;
  if (dateFrom) initialValues.dateFrom = dateFrom;
  if (dateTo) initialValues.dateTo = dateTo;

  function handleApply(values: Record<string, string>) {
    // Filter changes reset to page 1.
    pushParams({
      category: values.category,
      search: values.search,
      transactionId: values.transactionId,
      dateFrom: values.dateFrom,
      dateTo: values.dateTo,
      page: undefined,
    });
  }

  function handleClear() {
    router.replace(pathname);
  }

  function handlePageChange(next: number) {
    pushParams({ page: next > 1 ? String(next) : undefined });
  }

  const totalPages = data ? Math.max(1, Math.ceil(data.totalCount / data.pageSize)) : 1;

  return (
    <div className="mx-auto w-full max-w-6xl px-4 py-6">
      <h1 className="mb-4 text-2xl font-semibold text-gray-900">{t("title")}</h1>

      <FilterBar
        fields={fields}
        initialValues={initialValues}
        onApply={handleApply}
        onClear={handleClear}
        className="mb-4"
      />

      {isError ? (
        <ErrorState message={t("loadError")} onRetry={() => refetch()} />
      ) : isLoading ? (
        <div className="flex flex-col gap-2">
          {[0, 1, 2, 3, 4].map((i) => (
            <Skeleton key={i} className="h-12" />
          ))}
        </div>
      ) : (
        <>
          <AuditLogTable entries={data?.items ?? []} />
          <Pagination
            currentPage={data?.page ?? 1}
            totalPages={totalPages}
            onPageChange={handlePageChange}
            className="mt-4 justify-center"
          />
        </>
      )}
    </div>
  );
}
