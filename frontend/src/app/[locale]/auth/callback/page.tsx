"use client";

import { useEffect, useMemo, useState } from "react";
import { useRouter, useSearchParams } from "next/navigation";
import Link from "next/link";
import { useLocale, useTranslations } from "next-intl";
import { InfoScreen, TosModal } from "@/components/auth";

type CallbackStatus = "loading" | "success" | "new_user" | "error";
type CallbackErrorCode =
  | "auth_failed"
  | "steam_unavailable"
  | "temporarily_locked"
  | "account_banned"
  | "unknown";

const ERROR_CODES: CallbackErrorCode[] = [
  "auth_failed",
  "steam_unavailable",
  "temporarily_locked",
  "account_banned",
];

function asErrorCode(raw: string | null): CallbackErrorCode {
  if (raw && (ERROR_CODES as string[]).includes(raw)) {
    return raw as CallbackErrorCode;
  }
  return "unknown";
}

const RELATIVE_PATH_RE = /^\/(?!\/)[^?#]*(\?[^#]*)?(#.*)?$/;

function sanitizeReturnUrl(raw: string | null): string {
  if (!raw) return "/dashboard";
  if (!RELATIVE_PATH_RE.test(raw)) return "/dashboard";
  return raw;
}

const DEFAULT_TOS_VERSION = process.env.NEXT_PUBLIC_TOS_VERSION ?? "1.0";

export default function SteamCallbackPage() {
  const router = useRouter();
  const searchParams = useSearchParams();
  const locale = useLocale();
  const t = useTranslations("auth.callback");
  const tCommon = useTranslations("common");

  const rawStatus = searchParams.get("status");
  const rawError = searchParams.get("error");
  const retryAfterRaw = searchParams.get("retryAfter");
  const returnUrl = sanitizeReturnUrl(searchParams.get("returnUrl"));

  const localePath = (path: string) => `/${locale}${path.startsWith("/") ? path : `/${path}`}`;

  const status: CallbackStatus = useMemo(() => {
    if (rawError) return "error";
    if (rawStatus === "new_user") return "new_user";
    if (rawStatus === "success") return "success";
    return "loading";
  }, [rawStatus, rawError]);

  const [tosSubmitting, setTosSubmitting] = useState(false);
  const [tosError, setTosError] = useState<string | null>(null);

  // Success → redirect into the app's return URL.
  useEffect(() => {
    if (status !== "success") return;
    const target = localePath(returnUrl);
    router.replace(target);
  }, [status, returnUrl, router, locale]); // eslint-disable-line react-hooks/exhaustive-deps

  if (status === "loading") {
    return (
      <div
        role="status"
        aria-live="polite"
        className="mx-auto flex w-full max-w-md flex-col items-center gap-4 rounded-xl bg-white p-8 text-center shadow-sm ring-1 ring-gray-100"
      >
        <div
          aria-hidden="true"
          className="h-10 w-10 animate-spin rounded-full border-4 border-blue-200 border-t-blue-600"
        />
        <p className="text-sm text-gray-600">{t("loading")}</p>
      </div>
    );
  }

  if (status === "success") {
    return (
      <div
        role="status"
        aria-live="polite"
        className="mx-auto flex w-full max-w-md flex-col items-center gap-4 rounded-xl bg-white p-8 text-center shadow-sm ring-1 ring-green-100"
      >
        <div
          aria-hidden="true"
          className="h-10 w-10 animate-spin rounded-full border-4 border-green-200 border-t-green-600"
        />
        <p className="text-sm text-gray-600">{t("redirecting")}</p>
      </div>
    );
  }

  if (status === "new_user") {
    return (
      <>
        <div
          aria-hidden="true"
          className="mx-auto flex w-full max-w-md flex-col items-center gap-4 rounded-xl bg-white p-8 text-center shadow-sm ring-1 ring-blue-100"
        >
          <div className="h-10 w-10 animate-pulse rounded-full bg-blue-100" />
          <p className="text-sm text-gray-500">{t("preparing")}</p>
        </div>
        <TosModal
          open
          tosVersion={DEFAULT_TOS_VERSION}
          tosHref={localePath("/terms")}
          submitting={tosSubmitting}
          errorMessage={tosError}
          onAccept={() => {
            // Real POST /auth/tos/accept wire-up is deferred (T29/T34 integration).
            // For now we surface a UI-only acknowledgement path: navigate to dashboard.
            setTosSubmitting(true);
            setTosError(null);
            router.replace(localePath(returnUrl));
          }}
          onAgeRejected={() => router.replace(localePath("/auth/age-gate"))}
        />
      </>
    );
  }

  // status === "error"
  const code = asErrorCode(rawError);
  const errorTitle = t(`error.${code}.title`);
  const retryAfterMinutes = (() => {
    if (code !== "temporarily_locked") return null;
    const sec = Number(retryAfterRaw);
    if (!Number.isFinite(sec) || sec <= 0) return null;
    return Math.ceil(sec / 60);
  })();

  const errorBody =
    code === "temporarily_locked" && retryAfterMinutes !== null
      ? t("error.temporarily_locked.bodyWithMinutes", { minutes: retryAfterMinutes })
      : t(`error.${code}.body`);

  return (
    <InfoScreen
      tone="danger"
      icon="⚠"
      title={errorTitle}
      description={errorBody}
      actions={
        <>
          <Link
            href={localePath("/auth/login")}
            className="inline-flex flex-1 items-center justify-center rounded-md bg-blue-600 px-4 py-2 text-sm font-semibold text-white shadow-sm hover:bg-blue-700 focus:outline-none focus:ring-2 focus:ring-blue-500 focus:ring-offset-2"
          >
            {tCommon("retry")}
          </Link>
          <Link
            href={localePath("/")}
            className="inline-flex flex-1 items-center justify-center rounded-md border border-gray-300 bg-white px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50 focus:outline-none focus:ring-2 focus:ring-gray-400 focus:ring-offset-2"
          >
            {t("backToHome")}
          </Link>
        </>
      }
    />
  );
}
