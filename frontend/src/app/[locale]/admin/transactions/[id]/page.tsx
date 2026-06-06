"use client";

import { useParams } from "next/navigation";
import { useTranslations } from "next-intl";
import { ErrorState, Skeleton } from "@/components/common";
import { TransactionDetailView } from "@/components/admin";
import { useAdminTransactionDetail } from "@/lib/hooks/useAdminTransactionDetail";

/** S16 — Admin Transaction Detail (04 §8.5). */
export default function AdminTransactionDetailPage() {
  const t = useTranslations("adminTransactions");
  const params = useParams<{ id: string }>();
  const id = typeof params.id === "string" ? params.id : "";

  const { data, isLoading, isError, refetch } = useAdminTransactionDetail(id);

  return (
    <div className="mx-auto w-full max-w-6xl px-4 py-6">
      {isLoading ? (
        <div className="flex flex-col gap-4">
          <Skeleton className="h-8 w-48" />
          <Skeleton className="h-40" />
          <Skeleton className="h-40" />
        </div>
      ) : isError || !data ? (
        <ErrorState message={t("detail.loadError")} onRetry={() => refetch()} />
      ) : (
        <TransactionDetailView transaction={data} onRefetch={() => refetch()} />
      )}
    </div>
  );
}
