"use client";

import { useEffect, useMemo, useState } from "react";
import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { useLocale, useTranslations } from "next-intl";
import { sanitizeReturnUrl } from "@/lib/auth/returnUrl";
import { cn } from "@/lib/utils/cn";

const API_BASE_URL = process.env.NEXT_PUBLIC_API_URL ?? "/api/v1";

export default function SteamLoginPage() {
  const t = useTranslations("auth.login");
  const tAuth = useTranslations("auth");
  const locale = useLocale();
  const searchParams = useSearchParams();
  const [redirecting, setRedirecting] = useState(false);

  const returnUrl = sanitizeReturnUrl(searchParams.get("returnUrl"), locale);
  const steamLoginHref = useMemo(() => {
    const query = new URLSearchParams({ returnUrl });
    return `${API_BASE_URL}/auth/steam?${query.toString()}`;
  }, [returnUrl]);

  useEffect(() => {
    if (!redirecting) return;
    window.location.assign(steamLoginHref);
  }, [redirecting, steamLoginHref]);

  const baseClasses =
    "inline-flex w-full items-center justify-center gap-2 rounded-md px-6 py-3 text-base font-semibold shadow-sm focus:outline-none focus:ring-2 focus:ring-offset-2";

  return (
    <section
      role="region"
      aria-labelledby="steam-login-title"
      className="mx-auto w-full max-w-md rounded-xl bg-white p-6 shadow-sm ring-1 ring-blue-100"
    >
      <h1 id="steam-login-title" className="text-2xl font-semibold text-gray-900">
        {t("title")}
      </h1>
      <p className="mt-2 text-sm text-gray-600">{t("subtitle")}</p>

      <div className="mt-6 space-y-3">
        {redirecting ? (
          <button
            type="button"
            disabled
            aria-disabled="true"
            aria-busy="true"
            data-testid="steam-login-loading"
            className={cn(baseClasses, "cursor-not-allowed bg-slate-300 text-slate-600")}
          >
            <span aria-hidden="true">🎮</span>
            <span>{tAuth("authenticating")}</span>
          </button>
        ) : (
          <button
            type="button"
            data-testid="steam-login-button"
            onClick={() => setRedirecting(true)}
            className={cn(
              baseClasses,
              "bg-slate-900 text-white hover:bg-slate-800 focus:ring-slate-500",
            )}
          >
            <span aria-hidden="true">🎮</span>
            <span>{tAuth("loginWithSteam")}</span>
          </button>
        )}
        {redirecting && (
          <p className="text-center text-xs text-gray-500" role="status">
            {t("redirecting")}
          </p>
        )}
      </div>

      <ul className="mt-6 space-y-2 text-sm text-gray-600">
        <li className="flex items-start gap-2">
          <span aria-hidden="true">🔒</span>
          <span>{t("benefitSecurity")}</span>
        </li>
        <li className="flex items-start gap-2">
          <span aria-hidden="true">⚡</span>
          <span>{t("benefitNoPassword")}</span>
        </li>
        <li className="flex items-start gap-2">
          <span aria-hidden="true">🎯</span>
          <span>{t("benefitNoRegistration")}</span>
        </li>
      </ul>

      <div className="mt-6 border-t border-gray-200 pt-4 text-center">
        <Link
          href={`/${locale}`}
          className="text-sm text-gray-500 hover:text-gray-700 hover:underline"
        >
          {t("backToHome")}
        </Link>
      </div>
    </section>
  );
}
