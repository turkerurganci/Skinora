"use client";

import Link from "next/link";
import { useLocale, useTranslations } from "next-intl";
import { signOut } from "@/lib/auth/signOut";
import { useAuthStore } from "@/lib/stores/auth-store";
import { cn } from "@/lib/utils/cn";

export interface AdminHeaderProps {
  className?: string;
  onMenuClick?: () => void;
}

export function AdminHeader({ className, onMenuClick }: AdminHeaderProps) {
  const t = useTranslations("nav");
  const tAdmin = useTranslations("adminNav");
  const locale = useLocale();
  const displayName = useAuthStore((s) => s.displayName);
  // F7a — mağazadaki logout yalnız yerel token'ı siler; signOut A8'i de
  // çağırıp refresh token'ı sunucuda iptal eder.

  const href = (path: string) => `/${locale}${path}`;
  const adminName = displayName ?? t("adminFallback");

  return (
    <header
      className={cn(
        "flex items-center justify-between border-b border-gray-800 bg-gray-900 px-4 py-3 text-gray-100",
        className,
      )}
    >
      <div className="flex items-center gap-2">
        {onMenuClick ? (
          <button
            type="button"
            onClick={onMenuClick}
            aria-label={tAdmin("openMenu")}
            className="inline-flex h-9 w-9 items-center justify-center rounded-md border border-gray-700 bg-gray-800 text-gray-100 hover:bg-gray-700 md:hidden"
          >
            <svg
              xmlns="http://www.w3.org/2000/svg"
              viewBox="0 0 20 20"
              fill="currentColor"
              className="h-5 w-5"
              aria-hidden="true"
            >
              <path
                fillRule="evenodd"
                d="M3 5.5A1 1 0 0 1 4 4.5h12a1 1 0 1 1 0 2H4a1 1 0 0 1-1-1Zm0 4.5a1 1 0 0 1 1-1h12a1 1 0 1 1 0 2H4a1 1 0 0 1-1-1Zm1 3.5a1 1 0 1 0 0 2h12a1 1 0 1 0 0-2H4Z"
                clipRule="evenodd"
              />
            </svg>
          </button>
        ) : null}
        <Link
          href={href("/admin/dashboard")}
          className="text-lg font-semibold"
          aria-label="Skinora Admin"
        >
          Skinora <span className="text-gray-400">/ Admin</span>
        </Link>
      </div>

      <div className="flex items-center gap-3">
        <span className="hidden text-sm text-gray-300 sm:inline" data-testid="admin-name">
          {adminName}
        </span>
        <button
          type="button"
          onClick={() => void signOut()}
          className="inline-flex items-center rounded-md border border-gray-700 bg-gray-800 px-2 py-1 text-sm text-gray-100 hover:bg-gray-700"
          aria-label={t("signOut")}
        >
          {t("signOut")}
        </button>
      </div>
    </header>
  );
}
