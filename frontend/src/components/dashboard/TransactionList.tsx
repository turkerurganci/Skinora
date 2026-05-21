"use client";

import { useLocale, useTranslations } from "next-intl";
import Link from "next/link";
import {
  EmptyState,
  ErrorState,
  Pagination,
  Skeleton,
} from "@/components/common";
import { TransactionRow } from "./TransactionRow";
import type { TransactionListItem, TransactionListTab } from "@/lib/api/transactions";

export interface TransactionListProps {
  tab: TransactionListTab;
  items: TransactionListItem[] | undefined;
  page: number;
  totalPages: number;
  isLoading: boolean;
  isError: boolean;
  readOnly?: boolean;
  onPageChange: (page: number) => void;
  onRetry?: () => void;
}

function ListSkeleton() {
  return (
    <div className="space-y-3" aria-busy="true" aria-label="Loading transactions">
      {[0, 1, 2, 3].map((i) => (
        <Skeleton key={i} className="h-24" />
      ))}
    </div>
  );
}

export function TransactionList({
  tab,
  items,
  page,
  totalPages,
  isLoading,
  isError,
  readOnly,
  onPageChange,
  onRetry,
}: TransactionListProps) {
  const t = useTranslations("dashboard");
  const locale = useLocale();

  if (isLoading && !items) {
    return <ListSkeleton />;
  }

  if (isError) {
    return (
      <ErrorState
        title={t("error.title")}
        message={t("error.message")}
        onRetry={onRetry}
      />
    );
  }

  if (!items || items.length === 0) {
    if (tab === "active" && !readOnly) {
      return (
        <EmptyState
          title={t(`empty.${tab}.title`)}
          description={t(`empty.${tab}.description`)}
          action={
            <Link
              href={`/${locale}/transactions/new`}
              className="inline-flex items-center justify-center rounded-md bg-blue-600 px-4 py-2 text-sm font-semibold text-white shadow-sm hover:bg-blue-700"
            >
              {t("empty.active.cta")}
            </Link>
          }
        />
      );
    }
    return (
      <EmptyState
        title={t(`empty.${tab}.title`)}
        description={t(`empty.${tab}.description`)}
      />
    );
  }

  return (
    <div
      id="transaction-list-panel"
      role="tabpanel"
      aria-label={t(`tabs.${tab}`)}
      className="space-y-3"
    >
      {items.map((item) => (
        <TransactionRow key={item.id} item={item} readOnly={readOnly} />
      ))}
      {totalPages > 1 && (
        <div className="flex justify-center pt-2">
          <Pagination
            currentPage={page}
            totalPages={totalPages}
            onPageChange={onPageChange}
          />
        </div>
      )}
    </div>
  );
}
