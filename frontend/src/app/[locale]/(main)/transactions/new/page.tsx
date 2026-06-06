"use client";

import Link from "next/link";
import { useLocale, useTranslations } from "next-intl";
import { ApiError } from "@/lib/api/client";
import { useAuthStore } from "@/lib/stores/auth-store";
import { useEligibility } from "@/lib/hooks/useEligibility";
import { useTransactionParams } from "@/lib/hooks/useTransactionParams";
import { ErrorState, Skeleton } from "@/components/common";
import { SuspendedBanner } from "@/components/dashboard";
import { NewTransactionForm } from "@/components/transactions/new";

export default function NewTransactionPage() {
  const t = useTranslations("newTransaction");
  const locale = useLocale();

  const isAuthenticated = useAuthStore((s) => s.isAuthenticated);
  const isSuspended = useAuthStore((s) => s.isSuspended);

  const eligibility = useEligibility(isAuthenticated);
  const params = useTransactionParams(isAuthenticated);

  const eligibilityAuthError =
    eligibility.error instanceof ApiError && eligibility.error.status === 401;
  const paramsAuthError = params.error instanceof ApiError && params.error.status === 401;

  if (!isAuthenticated || eligibilityAuthError || paramsAuthError) {
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

  // T105a AC4 / 04 §6.7 — a suspended user cannot start a new transaction
  // (a blocked fund-flow action). Show the restriction banner instead of the
  // interactive form; the backend create guard (TransactionCreationService) is
  // the defense-in-depth. Consistent with the dashboard hiding the start button.
  if (isSuspended) {
    return (
      <div className="mx-auto w-full max-w-3xl px-4 py-6">
        <header className="mb-6 space-y-1">
          <h1 className="text-2xl font-semibold text-gray-900">{t("title")}</h1>
          <p className="text-sm text-gray-600">{t("subtitle")}</p>
        </header>
        <SuspendedBanner />
      </div>
    );
  }

  return (
    <div className="mx-auto w-full max-w-3xl px-4 py-6">
      <header className="mb-6 space-y-1">
        <h1 className="text-2xl font-semibold text-gray-900">{t("title")}</h1>
        <p className="text-sm text-gray-600">{t("subtitle")}</p>
      </header>

      {eligibility.isLoading || params.isLoading ? (
        <div className="space-y-4">
          <Skeleton className="h-12 w-full" />
          <Skeleton className="h-64 w-full" />
        </div>
      ) : eligibility.isError || params.isError || !eligibility.data || !params.data ? (
        <ErrorState
          title={t("loadError.title")}
          message={t("loadError.message")}
          onRetry={() => {
            eligibility.refetch();
            params.refetch();
          }}
        />
      ) : (
        <NewTransactionForm eligibility={eligibility.data} params={params.data} />
      )}
    </div>
  );
}
