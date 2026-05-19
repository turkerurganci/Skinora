"use client";

import Link from "next/link";
import { useLocale, useTranslations } from "next-intl";
import { useAuthStore } from "@/lib/stores/auth-store";
import { cn } from "@/lib/utils/cn";

export interface AdminHeaderProps {
  className?: string;
}

export function AdminHeader({ className }: AdminHeaderProps) {
  const t = useTranslations("nav");
  const locale = useLocale();
  const displayName = useAuthStore((s) => s.displayName);
  const logout = useAuthStore((s) => s.logout);

  const href = (path: string) => `/${locale}${path}`;
  const adminName = displayName ?? t("adminFallback");

  return (
    <header
      className={cn(
        "flex items-center justify-between border-b border-gray-800 bg-gray-900 px-4 py-3 text-gray-100",
        className,
      )}
    >
      <Link
        href={href("/admin/dashboard")}
        className="text-lg font-semibold"
        aria-label="Skinora Admin"
      >
        Skinora <span className="text-gray-400">/ Admin</span>
      </Link>

      <div className="flex items-center gap-3">
        <span className="text-sm text-gray-300" data-testid="admin-name">
          {adminName}
        </span>
        <button
          type="button"
          onClick={() => logout()}
          className="inline-flex items-center rounded-md border border-gray-700 bg-gray-800 px-2 py-1 text-sm text-gray-100 hover:bg-gray-700"
          aria-label={t("signOut")}
        >
          {t("signOut")}
        </button>
      </div>
    </header>
  );
}
