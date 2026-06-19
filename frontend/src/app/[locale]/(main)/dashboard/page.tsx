"use client";

import { useCallback, useMemo } from "react";
import Link from "next/link";
import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { useLocale, useTranslations } from "next-intl";
import { ApiError } from "@/lib/api/client";
import { useAuthStore } from "@/lib/stores/auth-store";
import { useUserStats } from "@/lib/hooks/useUserStats";
import { useTransactionList } from "@/lib/hooks/useTransactionList";
import {
  StatsCards,
  SuspendedBanner,
  TransactionList,
  TransactionTabs,
} from "@/components/dashboard";
import type { TransactionListTab } from "@/lib/api/transactions";

const PAGE_SIZE = 20;

const TAB_VALUES: readonly TransactionListTab[] = ["active", "completed", "cancelled"];

export default function DashboardPage() {
  const t = useTranslations("dashboard");
  const locale = useLocale();
  const router = useRouter();
  const pathname = usePathname();
  const searchParams = useSearchParams();

  const isAuthenticated = useAuthStore((s) => s.isAuthenticated);
  const isSuspended = useAuthStore((s) => s.isSuspended);

  // T1 list state is synced to the URL (WP13 url-state-sync) so a tab/page view
  // is shareable, survives refresh, and consumes deep-links (e.g. ?tab=completed).
  const tabParam = searchParams.get("tab");
  const tab: TransactionListTab = (TAB_VALUES as readonly string[]).includes(tabParam ?? "")
    ? (tabParam as TransactionListTab)
    : "active";
  const pageParam = Number(searchParams.get("page"));
  const page = Number.isFinite(pageParam) && pageParam > 0 ? pageParam : 1;

  const statsQuery = useUserStats(isAuthenticated);
  const listQuery = useTransactionList({ tab, page, pageSize: PAGE_SIZE }, isAuthenticated);

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

  const onTabChange = (next: TransactionListTab) => {
    // Tab change resets to page 1; "active" is the canonical default (drop param).
    pushParams({ tab: next === "active" ? undefined : next, page: undefined });
  };

  const onPageChange = (next: number) => {
    pushParams({ page: next > 1 ? String(next) : undefined });
  };

  const statsIsAuthError = useMemo(
    () => statsQuery.error instanceof ApiError && statsQuery.error.status === 401,
    [statsQuery.error],
  );
  const listIsAuthError = useMemo(
    () => listQuery.error instanceof ApiError && listQuery.error.status === 401,
    [listQuery.error],
  );

  // Both endpoints are Authenticated; if the client has no token or the
  // server rejects it, surface a single login CTA instead of two error
  // panels stacked on top of each other.
  if (!isAuthenticated || statsIsAuthError || listIsAuthError) {
    return (
      <div className="mx-auto flex w-full max-w-2xl flex-col items-center gap-4 px-4 py-12 text-center">
        <h1 className="text-xl font-semibold text-gray-900">{t("authRequired.title")}</h1>
        <p className="text-sm text-gray-600">{t("authRequired.description")}</p>
        <Link
          href={`/${locale}/auth/login`}
          className="inline-flex items-center justify-center rounded-md bg-blue-600 px-4 py-2 text-sm font-semibold text-white shadow-sm hover:bg-blue-700"
        >
          {t("authRequired.cta")}
        </Link>
      </div>
    );
  }

  const totalPages = listQuery.data?.totalPages ?? 0;

  return (
    <div className="mx-auto w-full max-w-6xl px-4 py-6">
      {isSuspended && (
        <div className="mb-4">
          <SuspendedBanner />
        </div>
      )}

      <div className="mb-4 flex flex-col items-start justify-between gap-3 sm:flex-row sm:items-center">
        <h1 className="text-2xl font-semibold text-gray-900">{t("title")}</h1>
        {!isSuspended && (
          <Link
            href={`/${locale}/transactions/new`}
            className="inline-flex items-center justify-center rounded-md bg-blue-600 px-4 py-2 text-sm font-semibold text-white shadow-sm hover:bg-blue-700"
          >
            {t("newTransaction")}
          </Link>
        )}
      </div>

      {/* Mobile / tablet: stats on top. Desktop (lg+): stats in right rail. */}
      <div className="mb-4 lg:hidden">
        <StatsCards
          stats={statsQuery.data}
          isLoading={statsQuery.isLoading}
          isError={statsQuery.isError}
        />
      </div>

      <div className="grid grid-cols-1 gap-6 lg:grid-cols-[1fr_18rem]">
        <div>
          <TransactionTabs active={tab} onChange={onTabChange} className="mb-4" />
          <TransactionList
            tab={tab}
            items={listQuery.data?.items}
            page={page}
            totalPages={totalPages}
            isLoading={listQuery.isLoading || listQuery.isFetching}
            isError={listQuery.isError}
            readOnly={isSuspended}
            onPageChange={onPageChange}
            onRetry={() => listQuery.refetch()}
          />
        </div>

        <aside className="hidden lg:block">
          <StatsCards
            stats={statsQuery.data}
            isLoading={statsQuery.isLoading}
            isError={statsQuery.isError}
          />
        </aside>
      </div>
    </div>
  );
}
