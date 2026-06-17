"use client";

import { useCallback, useMemo, useState } from "react";
import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { useTranslations } from "next-intl";
import { ErrorState, FilterBar, Pagination, Skeleton } from "@/components/common";
import type { FilterField } from "@/components/common";
import { DisputeQueueTable, DisputeResolveModal } from "@/components/admin";
import { useAdminDisputeList } from "@/lib/hooks/useAdminDisputeList";
import type { AdminDisputeListItem, AdminDisputeListQuery } from "@/lib/api/admin";
import { DisputeStatus, DisputeType } from "@/types/enums";

const PAGE_SIZE = 20;

const STATUS_VALUES = [
  "OPEN",
  "ESCALATED",
  "CLOSED",
  "RESOLVED_FOR_SELLER",
  "RESOLVED_FOR_BUYER",
] as const;
const TYPE_VALUES = ["PAYMENT", "DELIVERY", "WRONG_ITEM"] as const;

function parseEnum<T extends string>(value: string | null, allowed: readonly T[]): T | undefined {
  return value && (allowed as readonly string[]).includes(value) ? (value as T) : undefined;
}

/**
 * WP5 — Admin Dispute Queue (AD27, 07 §9.x). Defaults to the ESCALATED queue
 * (the dead-end disputes); the status filter lets the admin inspect resolved
 * disputes too. Resolution opens a modal (seller-favor / buyer-favor).
 */
export default function AdminDisputesPage() {
  const t = useTranslations("adminDisputes");
  const tStatus = useTranslations("adminDisputes.status");
  const tType = useTranslations("adminDisputes.type");
  const router = useRouter();
  const pathname = usePathname();
  const searchParams = useSearchParams();

  const status = parseEnum(searchParams.get("status"), STATUS_VALUES);
  const type = parseEnum(searchParams.get("type"), TYPE_VALUES);
  const pageParam = Number(searchParams.get("page"));
  const page = Number.isFinite(pageParam) && pageParam > 0 ? pageParam : 1;

  const [resolving, setResolving] = useState<AdminDisputeListItem | null>(null);

  const query: AdminDisputeListQuery = useMemo(
    () => ({
      status: status as DisputeStatus | undefined,
      type: type as DisputeType | undefined,
      page,
      pageSize: PAGE_SIZE,
    }),
    [status, type, page],
  );

  const { data, isLoading, isError, refetch } = useAdminDisputeList(query);

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
      key: "status",
      label: t("filters.status"),
      kind: "select",
      placeholder: t("filters.statusDefault"),
      options: STATUS_VALUES.map((v) => ({ value: v, label: tStatus(v) })),
    },
    {
      key: "type",
      label: t("filters.type"),
      kind: "select",
      placeholder: t("filters.allTypes"),
      options: TYPE_VALUES.map((v) => ({ value: v, label: tType(v) })),
    },
  ];

  const initialValues: Record<string, string> = {};
  if (status) initialValues.status = status;
  if (type) initialValues.type = type;

  function handleApply(values: Record<string, string>) {
    pushParams({ status: values.status, type: values.type, page: undefined });
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
          <DisputeQueueTable disputes={data?.items ?? []} onResolve={setResolving} />
          <Pagination
            currentPage={data?.page ?? 1}
            totalPages={totalPages}
            onPageChange={handlePageChange}
            className="mt-4 justify-center"
          />
        </>
      )}

      <DisputeResolveModal dispute={resolving} onClose={() => setResolving(null)} />
    </div>
  );
}
