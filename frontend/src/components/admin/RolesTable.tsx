"use client";

import { useTranslations } from "next-intl";
import { ResponsiveTable, type ResponsiveTableColumn } from "@/components/common";
import type { AdminRoleSummary } from "@/lib/api/admin";

export interface RolesTableProps {
  roles: readonly AdminRoleSummary[];
  onEdit: (role: AdminRoleSummary) => void;
  onDelete: (role: AdminRoleSummary) => void;
}

/**
 * S19 roller listesi (04 §8.8). Columns: Rol Adı (clickable → yetki düzenleme),
 * Açıklama, Atanmış Kullanıcı, Aksiyonlar (Düzenle / Sil). Super-admin roles are
 * rendered read-only (T104 owner decision): a badge replaces the actions and the
 * name is non-interactive so MANAGE_ROLES can't be stripped — or the role
 * deleted — by accident. The backend remains the enforcer of last resort.
 */
export function RolesTable({ roles, onEdit, onDelete }: RolesTableProps) {
  const t = useTranslations("adminRoles.roles");

  const columns: ReadonlyArray<ResponsiveTableColumn<AdminRoleSummary>> = [
    {
      key: "name",
      header: t("columns.name"),
      cell: (r) => (
        <div className="flex flex-col gap-0.5">
          <div className="flex flex-wrap items-center gap-2">
            {r.isSuperAdmin ? (
              <span className="font-medium text-gray-900">{r.name}</span>
            ) : (
              <button
                type="button"
                onClick={() => onEdit(r)}
                className="text-left font-medium text-blue-700 hover:underline"
              >
                {r.name}
              </button>
            )}
            {r.isSuperAdmin && (
              <span className="rounded-full bg-purple-100 px-2 py-0.5 text-[11px] font-medium text-purple-700">
                {t("superAdminBadge")}
              </span>
            )}
          </div>
          <span className="text-xs text-gray-400">
            {t("permissionCount", { count: r.permissions.length })}
          </span>
        </div>
      ),
    },
    {
      key: "description",
      header: t("columns.description"),
      cell: (r) => r.description ?? t("noDescription"),
      cellClassName: "text-gray-600",
    },
    {
      key: "assignedUsers",
      header: t("columns.assignedUsers"),
      cell: (r) => t("assignedCount", { count: r.assignedUserCount }),
    },
    {
      key: "actions",
      header: t("columns.actions"),
      cell: (r) =>
        r.isSuperAdmin ? (
          <span className="text-gray-400">{t("noDescription")}</span>
        ) : (
          <div className="flex gap-2">
            <button
              type="button"
              onClick={() => onEdit(r)}
              className="rounded-md border border-gray-300 px-2.5 py-1 text-xs font-medium text-gray-700 hover:bg-gray-50"
            >
              {t("edit")}
            </button>
            <button
              type="button"
              onClick={() => onDelete(r)}
              className="rounded-md border border-red-300 px-2.5 py-1 text-xs font-medium text-red-700 hover:bg-red-50"
            >
              {t("delete")}
            </button>
          </div>
        ),
    },
  ];

  return (
    <ResponsiveTable
      data={roles}
      columns={columns}
      getRowKey={(r) => r.id}
      ariaLabel={t("heading")}
      emptyMessage={t("empty")}
    />
  );
}
