"use client";

import { useEffect, useMemo, useState } from "react";
import { useRouter, useSearchParams } from "next/navigation";
import Link from "next/link";
import { useLocale, useTranslations } from "next-intl";
import { InfoScreen, TosModal } from "@/components/auth";
import { ApiError, refreshAccessToken } from "@/lib/api/client";
import { acceptTos } from "@/lib/api/auth";
import { useAuthStore } from "@/lib/stores/auth-store";

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
  const tTos = useTranslations("auth.tos");
  const setAccessToken = useAuthStore((s) => s.setAccessToken);

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

  const [tokenReady, setTokenReady] = useState(false);
  const [refreshFailed, setRefreshFailed] = useState(false);
  const [tosSubmitting, setTosSubmitting] = useState(false);
  const [tosError, setTosError] = useState<string | null>(null);

  // success / new_user → exchange the HttpOnly refresh cookie (set by the
  // backend on the Steam callback) for an access token and store it (WP11,
  // 07 §4.3). Without this the token was never written and isAuthenticated
  // stayed false forever.
  useEffect(() => {
    if (status !== "success" && status !== "new_user") return;
    let cancelled = false;
    void (async () => {
      const token = await refreshAccessToken();
      if (cancelled) return;
      if (!token) {
        setRefreshFailed(true);
        return;
      }
      setAccessToken(token);
      setTokenReady(true);
    })();
    return () => {
      cancelled = true;
    };
  }, [status, setAccessToken]);

  // success → once the token is stored, redirect into the app's return URL.
  useEffect(() => {
    if (status !== "success" || !tokenReady) return;
    router.replace(localePath(returnUrl));
  }, [status, tokenReady, returnUrl, router, locale]); // eslint-disable-line react-hooks/exhaustive-deps

  const handleAcceptTos = async (tosVersion: string) => {
    setTosSubmitting(true);
    setTosError(null);
    try {
      await acceptTos(tosVersion);
      router.replace(localePath(returnUrl));
    } catch (err) {
      // Already accepted this version (e.g. double-submit) → treat as done.
      if (err instanceof ApiError && err.code === "TOS_ALREADY_ACCEPTED") {
        router.replace(localePath(returnUrl));
        return;
      }
      setTosError(tTos("acceptError"));
      setTosSubmitting(false);
    }
  };

  // Loading spinner while completing sign-in OR exchanging the refresh cookie.
  const exchangingToken =
    (status === "success" || status === "new_user") && !tokenReady && !refreshFailed;

  if (status === "loading" || (status === "success" && exchangingToken)) {
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

  if (status === "success" && tokenReady) {
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

  if (status === "new_user" && !refreshFailed) {
    return (
      <>
        <div
          aria-hidden="true"
          className="mx-auto flex w-full max-w-md flex-col items-center gap-4 rounded-xl bg-white p-8 text-center shadow-sm ring-1 ring-blue-100"
        >
          <div className="h-10 w-10 animate-pulse rounded-full bg-blue-100" />
          <p className="text-sm text-gray-500">{t("preparing")}</p>
        </div>
        {tokenReady && (
          <TosModal
            open
            tosVersion={DEFAULT_TOS_VERSION}
            tosHref={localePath("/terms")}
            submitting={tosSubmitting}
            errorMessage={tosError}
            onAccept={({ tosVersion }) => void handleAcceptTos(tosVersion)}
            onAgeRejected={() => router.replace(localePath("/auth/age-gate"))}
          />
        )}
      </>
    );
  }

  // status === "error" OR the refresh-cookie exchange failed.
  const code = asErrorCode(refreshFailed ? "auth_failed" : rawError);
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
