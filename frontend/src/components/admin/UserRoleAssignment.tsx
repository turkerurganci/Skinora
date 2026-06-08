"use client";

import { useEffect, useState } from "react";
import { useTranslations } from "next-intl";
import {
  ErrorState,
  Pagination,
  ResponsiveTable,
  Skeleton,
  useToast,
  type ResponsiveTableColumn,
} from "@/components/common";
import { useAdminUsers } from "@/lib/hooks/useAdminUsers";
import { useAssignUserRole } from "@/lib/hooks/useAdminRoleMutations";
import type { AdminRoleSummary, AdminUserListItem } from "@/lib/api/admin";

const PAGE_SIZE = 20;

export interface UserRoleAssignmentProps {
  roles: readonly AdminRoleSummary[];
}

/**
 * S19 "Kullanıcı-Rol Atama" (04 §8.8) — searchable, paginated admin-user list
 * (AD15) with an inline per-row role dropdown (AD17). Selecting a role assigns
 * it; the empty option clears it (`roleId = null`). A successful change fires a
 * toast and invalidates both the user and role caches (assignedUserCount).
 */
export function UserRoleAssignment({ roles }: UserRoleAssignmentProps) {
  const t = useTranslations("adminRoles.assignment");
  const { push } = useToast();

  const [searchInput, setSearchInput] = useState("");
  const [search, setSearch] = useState("");
  const [page, setPage] = useState(1);
  const [pendingUserId, setPendingUserId] = useState<string | null>(null);

  const assign = useAssignUserRole();

  // Debounce the search box and reset to the first page when the term changes.
  useEffect(() => {
    const id = setTimeout(() => {
      setSearch(searchInput.trim());
      setPage(1);
    }, 300);
    return () => clearTimeout(id);
  }, [searchInput]);

  const { data, isLoading, isError, refetch } = useAdminUsers({
    search: search || undefined,
    page,
    pageSize: PAGE_SIZE,
  });

  function handleAssign(user: AdminUserListItem, value: string) {
    const roleId = value === "" ? null : value;
    // Ignore no-op changes (same role re-selected).
    if ((user.role?.id ?? "") === (roleId ?? "")) return;
    setPendingUserId(user.id);
    assign.mutate(
      { userId: user.id, roleId },
      {
        onSuccess: () => push({ variant: "success", message: t("assigned") }),
        onError: () => push({ variant: "error", message: t("assignError") }),
        onSettled: () => setPendingUserId(null),
      },
    );
  }

  const totalPages = data ? Math.max(1, Math.ceil(data.totalCount / data.pageSize)) : 1;

  const columns: ReadonlyArray<ResponsiveTableColumn<AdminUserListItem>> = [
    {
      key: "user",
      header: t("columns.user"),
      cell: (u) => (
        <div className="flex items-center gap-2">
          <span className="flex h-8 w-8 shrink-0 items-center justify-center rounded-full bg-gray-200 text-xs font-semibold uppercase text-gray-600">
            {u.displayName.slice(0, 2)}
          </span>
          <span className="font-medium text-gray-900">{u.displayName}</span>
        </div>
      ),
    },
    {
      key: "steamId",
      header: t("columns.steamId"),
      cell: (u) => <span className="font-mono text-xs text-gray-500">{u.steamId}</span>,
    },
    {
      key: "role",
      header: t("columns.role"),
      cell: (u) => (
        <select
          aria-label={t("assignLabel", { name: u.displayName })}
          value={u.role?.id ?? ""}
          disabled={pendingUserId === u.id}
          onChange={(e) => handleAssign(u, e.target.value)}
          className="w-full rounded-md border border-gray-300 px-2 py-1.5 text-sm disabled:opacity-50 sm:w-52"
        >
          <option value="">{t("noRole")}</option>
          {roles.map((r) => (
            <option key={r.id} value={r.id}>
              {r.name}
            </option>
          ))}
        </select>
      ),
    },
  ];

  return (
    <section className="flex flex-col gap-3">
      <div>
        <h2 className="text-lg font-semibold text-gray-900">{t("heading")}</h2>
        <p className="text-sm text-gray-500">{t("description")}</p>
      </div>

      <input
        type="search"
        value={searchInput}
        onChange={(e) => setSearchInput(e.target.value)}
        placeholder={t("searchPlaceholder")}
        aria-label={t("searchPlaceholder")}
        className="w-full max-w-xs rounded-md border border-gray-300 px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-200"
      />

      {isError ? (
        <ErrorState message={t("loadError")} onRetry={() => refetch()} />
      ) : isLoading ? (
        <div className="flex flex-col gap-2">
          {[0, 1, 2, 3].map((i) => (
            <Skeleton key={i} className="h-12" />
          ))}
        </div>
      ) : (
        <>
          <ResponsiveTable
            data={data?.items ?? []}
            columns={columns}
            getRowKey={(u) => u.id}
            ariaLabel={t("heading")}
            emptyMessage={t("empty")}
          />
          <Pagination
            currentPage={page}
            totalPages={totalPages}
            onPageChange={setPage}
            className="justify-end"
          />
        </>
      )}
    </section>
  );
}
