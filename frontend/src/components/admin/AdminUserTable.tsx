"use client";

import Link from "next/link";
import { useLocale, useTranslations } from "next-intl";
import { ResponsiveTable } from "@/components/common";
import type { ResponsiveTableColumn } from "@/components/common";
import { cn } from "@/lib/utils/cn";
import type { AdminUserListItem } from "@/lib/api/admin";

export interface AdminUserTableProps {
  users: readonly AdminUserListItem[];
  className?: string;
}

/**
 * AD15 user-directory table (F5 / `UITour-AdminUsersPageIsStub`). Four columns —
 * user (avatar + name → S20 detail), Steam ID, assigned role, and an explicit
 * detail action.
 *
 * The row is a *navigation* surface, not an editing one: role changes stay in
 * S19 (`/admin/roles` → `UserRoleAssignment`, which already renders an AD15
 * list with an inline AD17 dropdown). Putting a second role editor here would
 * fork one workflow across two screens; the point of this table is that the
 * S20 detail page — live since T39 — had no entry point except typing a Steam
 * ID into the address bar.
 *
 * `UserCard` (C04) was rejected despite the fix plan naming it: it requires
 * `reputationScore`, `completedTransactions` and `accountAgeText`, none of
 * which AD15 returns. Rendering it would have meant inventing zeros.
 */
export function AdminUserTable({ users, className }: AdminUserTableProps) {
  const t = useTranslations("adminUsers");
  const locale = useLocale();

  const detailHref = (user: AdminUserListItem) =>
    `/${locale}/admin/users/${encodeURIComponent(user.steamId)}`;

  const columns: ReadonlyArray<ResponsiveTableColumn<AdminUserListItem>> = [
    {
      key: "user",
      header: t("columns.user"),
      cell: (user) => (
        <div className="flex items-center gap-2">
          {user.avatarUrl ? (
            // eslint-disable-next-line @next/next/no-img-element
            <img
              src={user.avatarUrl}
              alt=""
              className="h-8 w-8 shrink-0 rounded-full border border-gray-200 object-cover"
            />
          ) : (
            <span
              aria-hidden="true"
              className="flex h-8 w-8 shrink-0 items-center justify-center rounded-full bg-gray-200 text-xs font-semibold uppercase text-gray-600"
            >
              {user.displayName.slice(0, 2)}
            </span>
          )}
          <Link href={detailHref(user)} className="font-medium text-blue-600 hover:underline">
            {user.displayName}
          </Link>
        </div>
      ),
    },
    {
      key: "steamId",
      header: t("columns.steamId"),
      cell: (user) => <span className="font-mono text-xs text-gray-500">{user.steamId}</span>,
    },
    {
      key: "role",
      header: t("columns.role"),
      cell: (user) =>
        user.role ? (
          <span className="inline-flex items-center rounded-full bg-blue-50 px-2 py-0.5 text-xs font-medium text-blue-700">
            {user.role.name}
          </span>
        ) : (
          <span className="text-sm text-gray-400">{t("noRole")}</span>
        ),
    },
    {
      key: "actions",
      header: t("columns.actions"),
      // Mobile cards already expose the name as a link to the same page; a
      // second identical link would just be noise on a narrow screen.
      mobileHidden: true,
      cell: (user) => (
        <Link href={detailHref(user)} className="text-sm text-blue-600 hover:underline">
          {t("actions.detail")}
        </Link>
      ),
    },
  ];

  return (
    <ResponsiveTable
      data={users}
      columns={columns}
      getRowKey={(user) => user.id}
      ariaLabel={t("tableAriaLabel")}
      className={cn(className)}
    />
  );
}
