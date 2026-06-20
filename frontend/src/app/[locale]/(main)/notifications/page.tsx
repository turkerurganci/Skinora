"use client";

import { useSearchParams } from "next/navigation";
import { usePathname, useRouter } from "next/navigation";
import { useTranslations } from "next-intl";
import { ApiError } from "@/lib/api/client";
import { useAuthStore } from "@/lib/stores/auth-store";
import { useNotificationList } from "@/lib/hooks/useNotificationList";
import { EmptyState, ErrorState, Pagination, Skeleton } from "@/components/common";
import { MarkAllReadButton, NotificationList } from "@/components/notifications";

const PAGE_SIZE = 20;

/**
 * Parses `?page=N` into a positive integer; falls back to 1 for missing /
 * invalid / non-positive values so deep links always resolve to a valid
 * pagination state.
 */
function parsePage(raw: string | null): number {
  if (!raw) return 1;
  const parsed = Number.parseInt(raw, 10);
  if (!Number.isFinite(parsed) || parsed < 1) return 1;
  return parsed;
}

/**
 * S11 — Bildirimler (04 §7.7). Authenticated kullanıcının tüm in-app
 * bildirimlerini paginated olarak listeler ve "Tümünü okundu işaretle"
 * aksiyonunu sunar (07 §8.1, §8.3, §8.4 — N1/N3/N4 client-side wiring).
 *
 * Pagination state URL'de `?page=N` query'si olarak tutulur — refresh ve
 * browser back/forward'ı koruyan deep-link friendly davranış.
 *
 * Known limitations (T-future devir):
 *   K1 — SignalR realtime push yok; yeni bildirim sayfada otomatik gözükmez
 *        → T96 devir (manuel refresh / React Query invalidate ile gelir).
 *   K2 — Backend `message` Türkçe verbatim; locale-aware mesajlar T97 devir.
 *   K3 — Göreli zaman `useFormatter().relativeTime` ile lokal hesaplanır
 *        (i18n-friendly).
 */
export default function NotificationsPage() {
  const t = useTranslations("notificationsInbox");
  const router = useRouter();
  const pathname = usePathname();
  const searchParams = useSearchParams();

  const isAuthenticated = useAuthStore((s) => s.isAuthenticated);
  const currentPage = parsePage(searchParams.get("page"));
  const list = useNotificationList({ page: currentPage, pageSize: PAGE_SIZE }, isAuthenticated);

  function handlePageChange(nextPage: number) {
    const params = new URLSearchParams(searchParams.toString());
    if (nextPage === 1) {
      params.delete("page");
    } else {
      params.set("page", String(nextPage));
    }
    const query = params.toString();
    router.push(query ? `${pathname}?${query}` : pathname);
  }

  if (!isAuthenticated) {
    return (
      <div className="mx-auto w-full max-w-3xl px-4 py-6">
        <ErrorState title={t("errors.forbidden.title")} message={t("errors.forbidden.message")} />
      </div>
    );
  }

  // Skeleton (C14): initial load only — `keepPreviousData` keeps the rows
  // visible across page changes, so subsequent fetches don't flash.
  if (list.isLoading) {
    return (
      <div className="mx-auto w-full max-w-3xl space-y-4 px-4 py-6">
        <div className="flex items-center justify-between">
          <Skeleton className="h-7 w-40" />
          <Skeleton className="h-5 w-32" />
        </div>
        <div className="space-y-2">
          {Array.from({ length: 5 }).map((_, i) => (
            <Skeleton key={i} className="h-16 w-full" />
          ))}
        </div>
      </div>
    );
  }

  if (list.error instanceof ApiError && list.error.status === 401) {
    return (
      <div className="mx-auto w-full max-w-3xl px-4 py-6">
        <ErrorState title={t("errors.forbidden.title")} message={t("errors.forbidden.message")} />
      </div>
    );
  }

  if (list.isError || !list.data) {
    return (
      <div className="mx-auto w-full max-w-3xl px-4 py-6">
        <ErrorState
          title={t("errors.generic.title")}
          message={t("errors.generic.message")}
          onRetry={() => list.refetch()}
        />
      </div>
    );
  }

  const { items, totalCount, totalPages } = list.data;
  const hasUnread = items.some((item) => !item.isRead);

  return (
    <div className="mx-auto w-full max-w-3xl space-y-4 px-4 py-6">
      <div className="flex items-center justify-between gap-4">
        <h1 className="text-xl font-semibold text-gray-900">{t("title")}</h1>
        <MarkAllReadButton enabled={hasUnread} />
      </div>

      {totalCount === 0 ? (
        <EmptyState
          icon={<span className="text-4xl">🔔</span>}
          title={t("empty.title")}
          description={t("empty.description")}
        />
      ) : (
        <>
          <NotificationList items={items} />
          <Pagination
            currentPage={currentPage}
            totalPages={totalPages}
            onPageChange={handlePageChange}
            className="justify-center pt-2"
          />
        </>
      )}
    </div>
  );
}
