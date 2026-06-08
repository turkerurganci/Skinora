"use client";

import { useParams } from "next/navigation";
import { useTranslations } from "next-intl";
import { ErrorState, Skeleton } from "@/components/common";
import { UserDetailView } from "@/components/admin";
import { useAdminUserDetail } from "@/lib/hooks/useAdminUserDetail";

/** S20 — Admin User Detail (04 §8.9). Reached via deep-links from S15/S16
 * transaction parties, the flag queue, and the audit log. */
export default function AdminUserDetailPage() {
  const t = useTranslations("adminUserDetail");
  const params = useParams<{ steamId: string }>();
  const steamId = typeof params.steamId === "string" ? params.steamId : "";

  const { data, isLoading, isError, refetch } = useAdminUserDetail(steamId);

  return (
    <div className="mx-auto w-full max-w-6xl px-4 py-6">
      {isLoading ? (
        <div className="flex flex-col gap-4">
          <Skeleton className="h-28" />
          <Skeleton className="h-32" />
          <Skeleton className="h-40" />
        </div>
      ) : isError || !data ? (
        <ErrorState message={t("loadError")} onRetry={() => refetch()} />
      ) : (
        <UserDetailView steamId={steamId} detail={data} />
      )}
    </div>
  );
}
