"use client";

import { useParams } from "next/navigation";
import { useTranslations } from "next-intl";
import { ErrorState, Skeleton } from "@/components/common";
import { FlagDetailView } from "@/components/admin";
import { useAdminFlagDetail } from "@/lib/hooks/useAdminFlagDetail";

/** S14 — Admin Flag Detail / Review (04 §8.3). */
export default function AdminFlagDetailPage() {
  const t = useTranslations("adminFlags");
  const params = useParams<{ id: string }>();
  const id = typeof params.id === "string" ? params.id : "";

  const { data, isLoading, isError, refetch } = useAdminFlagDetail(id);

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
        <FlagDetailView flag={data} />
      )}
    </div>
  );
}
