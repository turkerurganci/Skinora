"use client";

import { useCallback, useMemo } from "react";
import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { useTranslations } from "next-intl";
import { ErrorState, FilterBar, Pagination, Skeleton } from "@/components/common";
import type { FilterField } from "@/components/common";
import { FlagQueueTable } from "@/components/admin";
import { useAdminFlagList } from "@/lib/hooks/useAdminFlagList";
import type { AdminFlagListQuery } from "@/lib/api/admin";
import { parseTableSort, nextTableSort } from "@/lib/admin/tableSort";
import { toEndOfDay } from "@/lib/utils/date";

const PAGE_SIZE = 20;

// AD2 sort columns (07 §9.2 — server falls back to createdAt desc for others).
const SORT_KEYS = ["createdAt", "type", "reviewStatus"] as const;

const SCOPE_VALUES = ["ACCOUNT_LEVEL", "TRANSACTION_PRE_CREATE"] as const;
const TYPE_VALUES = [
  "PRICE_DEVIATION",
  "HIGH_VOLUME",
  "ABNORMAL_BEHAVIOR",
  "MULTI_ACCOUNT",
  "SANCTIONS_MATCH",
  "DELIVERY_REVERSED",
] as const;
const STATUS_VALUES = ["PENDING", "APPROVED", "REJECTED"] as const;

function parseEnum<T extends string>(value: string | null, allowed: readonly T[]): T | undefined {
  return value && (allowed as readonly string[]).includes(value) ? (value as T) : undefined;
}

/**
 * S13 — Admin Flag Queue (04 §8.2). Filters (category / type / status / date)
 * are synced to the URL so a queue view is shareable + survives refresh; the
 * page title surfaces the pending-flag backlog (07 §9.2 `pendingCount`).
 */
export default function AdminFlagsPage() {
  const t = useTranslations("adminFlags");
  const tType = useTranslations("adminFlags.type");
  const tScope = useTranslations("adminFlags.scope");
  const tStatus = useTranslations("adminFlags.reviewStatus");
  const router = useRouter();
  const pathname = usePathname();
  const searchParams = useSearchParams();

  const scope = parseEnum(searchParams.get("scope"), SCOPE_VALUES);
  const type = parseEnum(searchParams.get("type"), TYPE_VALUES);
  const reviewStatus = parseEnum(searchParams.get("reviewStatus"), STATUS_VALUES);
  const dateFrom = searchParams.get("dateFrom") ?? undefined;
  const dateTo = searchParams.get("dateTo") ?? undefined;
  const pageParam = Number(searchParams.get("page"));
  const page = Number.isFinite(pageParam) && pageParam > 0 ? pageParam : 1;
  const sort = parseTableSort(searchParams, SORT_KEYS);

  const query: AdminFlagListQuery = useMemo(
    () => ({
      scope,
      type,
      reviewStatus,
      dateFrom,
      dateTo: toEndOfDay(dateTo),
      sortBy: sort.by ?? undefined,
      sortOrder: sort.by ? sort.order : undefined,
      page,
      pageSize: PAGE_SIZE,
    }),
    [scope, type, reviewStatus, dateFrom, dateTo, sort.by, sort.order, page],
  );

  const { data, isLoading, isError, refetch } = useAdminFlagList(query);

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
      key: "scope",
      label: t("filters.category"),
      kind: "select",
      placeholder: t("filters.allCategories"),
      options: SCOPE_VALUES.map((v) => ({ value: v, label: tScope(v) })),
    },
    {
      key: "type",
      label: t("filters.type"),
      kind: "select",
      options: TYPE_VALUES.map((v) => ({ value: v, label: tType(v) })),
    },
    {
      key: "reviewStatus",
      label: t("filters.status"),
      kind: "select",
      options: STATUS_VALUES.map((v) => ({ value: v, label: tStatus(v) })),
    },
    { key: "dateFrom", label: t("filters.dateFrom"), kind: "date" },
    { key: "dateTo", label: t("filters.dateTo"), kind: "date" },
  ];

  const initialValues: Record<string, string> = {};
  if (scope) initialValues.scope = scope;
  if (type) initialValues.type = type;
  if (reviewStatus) initialValues.reviewStatus = reviewStatus;
  if (dateFrom) initialValues.dateFrom = dateFrom;
  if (dateTo) initialValues.dateTo = dateTo;

  function handleApply(values: Record<string, string>) {
    // Filter changes reset to page 1.
    pushParams({
      scope: values.scope,
      type: values.type,
      reviewStatus: values.reviewStatus,
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

  function handleSort(sortKey: string) {
    const next = nextTableSort(sort, sortKey);
    pushParams({ sortBy: next.by ?? undefined, sortOrder: next.order, page: undefined });
  }

  const totalPages = data ? Math.max(1, Math.ceil(data.totalCount / data.pageSize)) : 1;
  const pendingCount = data?.pendingCount ?? 0;

  return (
    <div className="mx-auto w-full max-w-6xl px-4 py-6">
      <h1 className="mb-4 text-2xl font-semibold text-gray-900">
        {pendingCount > 0 ? t("titleWithPending", { count: pendingCount }) : t("title")}
      </h1>

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
          <FlagQueueTable
            flags={data?.items ?? []}
            category={scope}
            sort={{ by: sort.by, order: sort.order, onSort: handleSort }}
          />
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
