"use client";

import { useCallback, useMemo } from "react";
import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { useTranslations } from "next-intl";
import { ErrorState, FilterBar, Pagination, Skeleton } from "@/components/common";
import type { FilterField } from "@/components/common";
import { TransactionListTable } from "@/components/admin";
import { useAdminTransactionList } from "@/lib/hooks/useAdminTransactionList";
import type { AdminTransactionListQuery, AdminTransactionStatusGroup } from "@/lib/api/admin";
import { parseTableSort, nextTableSort } from "@/lib/admin/tableSort";
import { toEndOfDay } from "@/lib/utils/date";
import { StablecoinType } from "@/types/enums";

const PAGE_SIZE = 20;

// AD6 sort columns (07 §9.6 — server falls back to createdAt desc for others).
const SORT_KEYS = ["createdAt", "price", "status"] as const;

const STATUS_GROUP_VALUES = ["ACTIVE", "COMPLETED", "CANCELLED", "FLAGGED"] as const;
const STABLECOIN_VALUES = [StablecoinType.USDT, StablecoinType.USDC] as const;

function parseEnum<T extends string>(value: string | null, allowed: readonly T[]): T | undefined {
  return value && (allowed as readonly string[]).includes(value) ? (value as T) : undefined;
}

function parseAmount(value: string | null): number | undefined {
  if (!value) return undefined;
  const n = Number(value);
  return Number.isFinite(n) && n >= 0 ? n : undefined;
}

/**
 * Resolve the effective status group, honouring the legacy S12 dashboard
 * deep-links (T99 K1): `?tab=active` → ACTIVE, `?range=daily|weekly` →
 * COMPLETED. The exact daily/weekly window is deferred — AD6 filters
 * `CreatedAt`, while those cards count `CompletedAt` (see K-note).
 */
function resolveStatusGroup(params: URLSearchParams): AdminTransactionStatusGroup | undefined {
  const explicit = parseEnum(params.get("statusGroup"), STATUS_GROUP_VALUES);
  if (explicit) return explicit;
  if (params.get("tab") === "active") return "ACTIVE";
  const range = params.get("range");
  if (range === "daily" || range === "weekly") return "COMPLETED";
  return undefined;
}

/**
 * S15 — Admin Transaction List & Search (04 §8.4). Filters (status group /
 * stablecoin / date range / amount range / user search) are synced to the URL
 * so a list view is shareable + survives refresh, and the S12 dashboard
 * deep-links resolve into the matching filter.
 */
export default function AdminTransactionsPage() {
  const t = useTranslations("adminTransactions");
  const tGroup = useTranslations("adminTransactions.statusGroup");
  const router = useRouter();
  const pathname = usePathname();
  const searchParams = useSearchParams();

  const statusGroup = resolveStatusGroup(searchParams);
  const stablecoin = parseEnum(searchParams.get("stablecoin"), STABLECOIN_VALUES);
  const dateFrom = searchParams.get("dateFrom") ?? undefined;
  const dateTo = searchParams.get("dateTo") ?? undefined;
  const minAmount = parseAmount(searchParams.get("minAmount"));
  const maxAmount = parseAmount(searchParams.get("maxAmount"));
  const search = searchParams.get("search") ?? undefined;
  const pageParam = Number(searchParams.get("page"));
  const page = Number.isFinite(pageParam) && pageParam > 0 ? pageParam : 1;
  const sort = parseTableSort(searchParams, SORT_KEYS);

  const query: AdminTransactionListQuery = useMemo(
    () => ({
      statusGroup,
      stablecoin,
      dateFrom,
      dateTo: toEndOfDay(dateTo),
      minAmount,
      maxAmount,
      search,
      sortBy: sort.by ?? undefined,
      sortOrder: sort.by ? sort.order : undefined,
      page,
      pageSize: PAGE_SIZE,
    }),
    [
      statusGroup,
      stablecoin,
      dateFrom,
      dateTo,
      minAmount,
      maxAmount,
      search,
      sort.by,
      sort.order,
      page,
    ],
  );

  const { data, isLoading, isError, refetch } = useAdminTransactionList(query);

  const pushParams = useCallback(
    (next: Record<string, string | undefined>) => {
      const params = new URLSearchParams(searchParams.toString());
      // Filter changes drop the legacy deep-link params so the URL is canonical.
      params.delete("tab");
      params.delete("range");
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
      key: "statusGroup",
      label: t("filters.status"),
      kind: "select",
      placeholder: t("filters.allStatuses"),
      options: STATUS_GROUP_VALUES.map((v) => ({ value: v, label: tGroup(v) })),
    },
    {
      key: "stablecoin",
      label: t("filters.stablecoin"),
      kind: "select",
      placeholder: t("filters.allStablecoins"),
      options: STABLECOIN_VALUES.map((v) => ({ value: v, label: v })),
    },
    {
      key: "search",
      label: t("filters.user"),
      kind: "text",
      placeholder: t("filters.userPlaceholder"),
    },
    { key: "minAmount", label: t("filters.minAmount"), kind: "text" },
    { key: "maxAmount", label: t("filters.maxAmount"), kind: "text" },
    { key: "dateFrom", label: t("filters.dateFrom"), kind: "date" },
    { key: "dateTo", label: t("filters.dateTo"), kind: "date" },
  ];

  const initialValues: Record<string, string> = {};
  if (statusGroup) initialValues.statusGroup = statusGroup;
  if (stablecoin) initialValues.stablecoin = stablecoin;
  if (search) initialValues.search = search;
  if (minAmount !== undefined) initialValues.minAmount = String(minAmount);
  if (maxAmount !== undefined) initialValues.maxAmount = String(maxAmount);
  if (dateFrom) initialValues.dateFrom = dateFrom;
  if (dateTo) initialValues.dateTo = dateTo;

  function handleApply(values: Record<string, string>) {
    // Filter changes reset to page 1.
    pushParams({
      statusGroup: values.statusGroup,
      stablecoin: values.stablecoin,
      search: values.search,
      minAmount: values.minAmount,
      maxAmount: values.maxAmount,
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
    // Sorting changes reset to page 1 so the first slice of the new order shows.
    const next = nextTableSort(sort, sortKey);
    pushParams({ sortBy: next.by ?? undefined, sortOrder: next.order, page: undefined });
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
          <TransactionListTable
            transactions={data?.items ?? []}
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
