"use client";

import { useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useLocale, useTranslations } from "next-intl";
import { useQuery } from "@tanstack/react-query";
import { getMe } from "@/lib/api/auth";
import { useAuthStore } from "@/lib/stores/auth-store";

export default function MobileAuthenticatorPage() {
  const t = useTranslations("auth.mobileAuthenticator");
  const locale = useLocale();
  const router = useRouter();
  const accessToken = useAuthStore((s) => s.accessToken);
  const [rechecked, setRechecked] = useState(false);

  const localePath = (path: string) => `/${locale}${path.startsWith("/") ? path : `/${path}`}`;

  // MA verification itself runs at trade-URL save (U17 → A7, 03 §2.1 / 07 §4.8),
  // not at login — so "recheck" just re-reads the latest verified state from
  // /auth/me (WP11 decision). Shared ["auth","me"] query (no extra request).
  const { data, refetch, isFetching } = useQuery({
    queryKey: ["auth", "me"],
    queryFn: getMe,
    enabled: !!accessToken,
    staleTime: 60_000,
  });

  const active = data?.mobileAuthenticatorActive ?? false;
  const stillInactive = rechecked && !active;

  const handleRecheck = async () => {
    setRechecked(false);
    const result = await refetch();
    if (result.data?.mobileAuthenticatorActive) {
      // Active now → forward to the app (S03 → continue, 04 §6.3).
      router.replace(localePath("/dashboard"));
    } else {
      setRechecked(true);
    }
  };

  return (
    <section
      role="region"
      aria-labelledby="ma-title"
      className="mx-auto w-full max-w-md rounded-xl bg-white p-6 shadow-sm ring-1 ring-amber-100"
    >
      <div
        aria-hidden="true"
        className="mb-4 inline-flex h-12 w-12 items-center justify-center rounded-full bg-amber-50 text-2xl text-amber-600"
      >
        📱
      </div>
      <h1 id="ma-title" className="text-xl font-semibold text-gray-900">
        {t("title")}
      </h1>
      <p className="mt-2 text-sm text-gray-600">{t("description")}</p>

      <div className="mt-5 rounded-md bg-gray-50 p-4">
        <p className="text-xs font-semibold uppercase tracking-wide text-gray-500">
          {t("stepsTitle")}
        </p>
        <ol className="mt-3 list-decimal space-y-2 pl-5 text-sm text-gray-700">
          <li>{t("step1")}</li>
          <li>{t("step2")}</li>
          <li>{t("step3")}</li>
          <li>{t("step4")}</li>
        </ol>
        <a
          href="https://store.steampowered.com/mobile"
          target="_blank"
          rel="noopener noreferrer"
          className="mt-3 inline-flex items-center gap-1 text-sm font-medium text-blue-600 hover:underline"
        >
          {t("steamMobileLink")}
          <span aria-hidden="true">↗</span>
        </a>
      </div>

      <div className="mt-6 flex flex-col gap-2 sm:flex-row">
        <button
          type="button"
          onClick={() => void handleRecheck()}
          disabled={isFetching}
          aria-busy={isFetching || undefined}
          data-testid="ma-recheck"
          className="inline-flex flex-1 items-center justify-center rounded-md bg-blue-600 px-4 py-2 text-sm font-semibold text-white shadow-sm hover:bg-blue-700 focus:outline-none focus:ring-2 focus:ring-blue-500 focus:ring-offset-2 disabled:cursor-not-allowed disabled:bg-blue-300"
        >
          {isFetching ? t("rechecking") : t("recheck")}
        </button>
        <button
          type="button"
          onClick={() => router.replace(localePath("/dashboard"))}
          className="inline-flex flex-1 items-center justify-center rounded-md border border-gray-300 bg-white px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50 focus:outline-none focus:ring-2 focus:ring-gray-400 focus:ring-offset-2"
        >
          {t("goToDashboard")}
        </button>
      </div>

      {stillInactive && (
        <p className="mt-4 text-sm text-amber-700" role="status" aria-live="polite">
          {t("stillInactive")}
        </p>
      )}

      <p className="mt-4 text-xs text-gray-500">{t("dashboardNote")}</p>

      <div className="mt-6 border-t border-gray-200 pt-4 text-center">
        <Link
          href={localePath("/")}
          className="text-xs text-gray-500 hover:text-gray-700 hover:underline"
        >
          {t("backToHome")}
        </Link>
      </div>
    </section>
  );
}
