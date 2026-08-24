"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { useLocale, useTranslations } from "next-intl";
import { useQuery } from "@tanstack/react-query";
import { getMe } from "@/lib/api/auth";
import { hasPermission } from "@/lib/auth/roles";
import { permissionForAdminRoute } from "@/lib/admin/routePermissions";
import { cn } from "@/lib/utils/cn";

interface AdminMenuItem {
  key:
    | "dashboard"
    | "flags"
    | "disputes"
    | "transactions"
    | "settings"
    | "roles"
    | "users"
    | "auditLog";
  path: string;
}

const MENU: readonly AdminMenuItem[] = [
  { key: "dashboard", path: "/admin/dashboard" },
  { key: "flags", path: "/admin/flags" },
  { key: "disputes", path: "/admin/disputes" },
  { key: "transactions", path: "/admin/transactions" },
  { key: "settings", path: "/admin/settings" },
  { key: "roles", path: "/admin/roles" },
  { key: "users", path: "/admin/users" },
  { key: "auditLog", path: "/admin/audit-logs" },
] as const;

export interface AdminSidebarProps {
  className?: string;
  isDrawerOpen?: boolean;
  onCloseDrawer?: () => void;
}

export function AdminSidebar({
  className,
  isDrawerOpen = false,
  onCloseDrawer,
}: AdminSidebarProps) {
  const t = useTranslations("adminNav");
  const locale = useLocale();
  const pathname = usePathname();

  // WP2c (FE-permission-guard) — hide the entries this admin's own token cannot
  // open. Reuses the shared ["auth","me"] query that AuthInitializer and
  // AdminGuard already prime, so this costs no extra request.
  //
  // While `me` is still loading the menu renders unfiltered. That is the
  // deliberate direction to fail in: a link that turns out to 403 is a smaller
  // problem than an admin watching their own menu items disappear and reappear
  // on every navigation. Nothing here is a security boundary — the backend
  // policy answers 403 regardless of what the menu shows.
  const { data: me } = useQuery({ queryKey: ["auth", "me"], queryFn: getMe });

  const visibleMenu = MENU.filter((item) => {
    if (!me) return true;
    const required = permissionForAdminRoute(item.path);
    return required === null || hasPermission(me.role, me.permissions, required);
  });

  const href = (path: string) => `/${locale}${path}`;

  function isActive(path: string) {
    const target = href(path);
    return pathname === target || pathname.startsWith(`${target}/`);
  }

  const nav = (
    <nav className="flex flex-col py-2">
      {visibleMenu.map((item) => {
        const active = isActive(item.path);
        return (
          <Link
            key={item.key}
            href={href(item.path)}
            aria-current={active ? "page" : undefined}
            className={cn(
              "px-4 py-2 text-sm transition-colors",
              active
                ? "border-l-2 border-blue-600 bg-blue-50 font-semibold text-blue-700"
                : "border-l-2 border-transparent text-gray-700 hover:bg-gray-50",
            )}
          >
            {t(item.key)}
          </Link>
        );
      })}
    </nav>
  );

  return (
    <>
      <aside
        className={cn("hidden w-56 shrink-0 border-r border-gray-200 bg-white md:block", className)}
        aria-label={t("ariaLabel")}
      >
        {nav}
      </aside>

      <div
        className={cn(
          "fixed inset-0 z-40 bg-gray-900/40 transition-opacity md:hidden",
          isDrawerOpen ? "opacity-100" : "pointer-events-none opacity-0",
        )}
        aria-hidden="true"
        onClick={onCloseDrawer}
      />
      <aside
        className={cn(
          "fixed inset-y-0 left-0 z-50 w-64 max-w-[80vw] transform border-r border-gray-200 bg-white shadow-xl transition-transform md:hidden",
          isDrawerOpen ? "translate-x-0" : "-translate-x-full",
        )}
        aria-label={t("ariaLabel")}
        aria-hidden={!isDrawerOpen}
        role="dialog"
        aria-modal="true"
      >
        <div className="flex items-center justify-between border-b border-gray-200 px-4 py-3">
          <span className="text-sm font-semibold text-gray-700">{t("ariaLabel")}</span>
          <button
            type="button"
            onClick={onCloseDrawer}
            aria-label={t("closeMenu")}
            className="rounded-md p-1 text-gray-600 hover:bg-gray-100"
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
                d="M4.293 4.293a1 1 0 0 1 1.414 0L10 8.586l4.293-4.293a1 1 0 1 1 1.414 1.414L11.414 10l4.293 4.293a1 1 0 0 1-1.414 1.414L10 11.414l-4.293 4.293a1 1 0 0 1-1.414-1.414L8.586 10 4.293 5.707a1 1 0 0 1 0-1.414Z"
                clipRule="evenodd"
              />
            </svg>
          </button>
        </div>
        <div onClick={onCloseDrawer}>{nav}</div>
      </aside>
    </>
  );
}
