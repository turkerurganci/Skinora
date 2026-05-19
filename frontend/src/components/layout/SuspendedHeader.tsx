"use client";

import Link from "next/link";
import { useLocale, useTranslations } from "next-intl";
import { useAuthStore } from "@/lib/stores/auth-store";
import { LanguageSelector } from "@/components/common";
import { cn } from "@/lib/utils/cn";

export interface SuspendedHeaderProps {
  className?: string;
  supportUrl?: string;
}

export function SuspendedHeader({ className, supportUrl = "/support" }: SuspendedHeaderProps) {
  const t = useTranslations("nav");
  const locale = useLocale();
  const logout = useAuthStore((s) => s.logout);

  const href = (path: string) => `/${locale}${path}`;

  return (
    <header
      className={cn(
        "flex items-center justify-between border-b border-orange-200 bg-orange-50 px-4 py-3",
        className,
      )}
      data-suspended="true"
    >
      <Link
        href={href("/dashboard")}
        className="text-lg font-semibold text-gray-900"
        aria-label="Skinora"
      >
        Skinora
      </Link>

      <nav className="flex items-center gap-3" aria-label={t("suspendedNav")}>
        <LanguageSelector />

        <Link
          href={supportUrl.startsWith("http") ? supportUrl : href(supportUrl)}
          className="inline-flex items-center rounded-md px-2 py-1 text-sm text-gray-700 hover:bg-orange-100"
          aria-label={t("support")}
        >
          {t("support")}
        </Link>

        <button
          type="button"
          onClick={() => logout()}
          className="inline-flex items-center rounded-md border border-gray-300 bg-white px-2 py-1 text-sm text-gray-700 hover:bg-gray-50"
          aria-label={t("signOut")}
        >
          {t("signOut")}
        </button>
      </nav>
    </header>
  );
}
