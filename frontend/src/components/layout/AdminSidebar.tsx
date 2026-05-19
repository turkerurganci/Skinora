"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { useLocale, useTranslations } from "next-intl";
import { cn } from "@/lib/utils/cn";

interface AdminMenuItem {
  key:
    | "dashboard"
    | "flags"
    | "transactions"
    | "settings"
    | "steamAccounts"
    | "roles"
    | "users"
    | "auditLog";
  path: string;
}

const MENU: readonly AdminMenuItem[] = [
  { key: "dashboard", path: "/admin/dashboard" },
  { key: "flags", path: "/admin/flags" },
  { key: "transactions", path: "/admin/transactions" },
  { key: "settings", path: "/admin/settings" },
  { key: "steamAccounts", path: "/admin/steam-accounts" },
  { key: "roles", path: "/admin/roles" },
  { key: "users", path: "/admin/users" },
  { key: "auditLog", path: "/admin/audit-logs" },
] as const;

export interface AdminSidebarProps {
  className?: string;
}

export function AdminSidebar({ className }: AdminSidebarProps) {
  const t = useTranslations("adminNav");
  const locale = useLocale();
  const pathname = usePathname();

  const href = (path: string) => `/${locale}${path}`;

  function isActive(path: string) {
    const target = href(path);
    return pathname === target || pathname.startsWith(`${target}/`);
  }

  return (
    <aside
      className={cn("w-56 shrink-0 border-r border-gray-200 bg-white", className)}
      aria-label={t("ariaLabel")}
    >
      <nav className="flex flex-col py-2">
        {MENU.map((item) => {
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
    </aside>
  );
}
