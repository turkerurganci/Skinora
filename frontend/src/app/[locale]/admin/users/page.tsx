"use client";

import { useCallback, useMemo } from "react";
import Link from "next/link";
import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { useLocale, useTranslations } from "next-intl";
import { useQuery } from "@tanstack/react-query";
import { getMe } from "@/lib/api/auth";
import { hasPermission } from "@/lib/auth/roles";
import { EmptyState, ErrorState, FilterBar, Pagination, Skeleton } from "@/components/common";
import type { FilterField } from "@/components/common";
import { AdminUserTable } from "@/components/admin";
import { useAdminUsers } from "@/lib/hooks/useAdminUsers";
import { useAdminRoles } from "@/lib/hooks/useAdminRoles";
import type { AdminUserListQuery } from "@/lib/api/admin";

const PAGE_SIZE = 20;

/**
 * F5 — Admin user directory (AD15, 07 §9.15), closing
 * `UITour-AdminUsersPageIsStub`. Until this page existed the whole file was
 * `return <div>Admin Users</div>` while the sidebar linked to it and the S20
 * detail page (`/admin/users/{steamId}`) was reachable only by typing a Steam
 * ID into the address bar.
 *
 * SCOPE IS SURFACED, NOT HIDDEN. `AdminUserService.ListAsync` has two modes:
 * with no search term it returns *only* users holding an active admin-role
 * assignment ("admin browse"), and a search term broadens it to every
 * non-deactivated user. An admin who did not know that would read the default
 * list as "the platform has four users", so the hint under the filter bar says
 * which mode is active.
 *
 * Filters (search / role / page) are synced to the URL — same WP13 contract as
 * S21, so a filtered view is shareable and survives a refresh. There is no
 * client-side permission guard on the list itself (a 403 surfaces as the error
 * state — the T103 K5 precedent every other admin page follows).
 *
 * THE TWO ENDPOINTS NO LONGER SHARE A POLICY. AD15 accepts VIEW_USERS *or*
 * MANAGE_ROLES since `AdminUsersDirectoryPermissionMismatch` was closed, while
 * the role list AD11 that feeds the role filter still requires MANAGE_ROLES.
 * So the filter is rendered only for admins who can actually read it, and its
 * query is not fired otherwise: a select with permanently empty options would
 * read as "there are no roles" rather than "this is not yours to filter by".
 */
export default function AdminUsersPage() {
  const t = useTranslations("adminUsers");
  const locale = useLocale();
  const router = useRouter();
  const pathname = usePathname();
  const searchParams = useSearchParams();

  const search = searchParams.get("search") ?? undefined;
  const roleId = searchParams.get("roleId") ?? undefined;
  const pageParam = Number(searchParams.get("page"));
  const page = Number.isFinite(pageParam) && pageParam > 0 ? pageParam : 1;

  const query: AdminUserListQuery = useMemo(
    () => ({ search, roleId, page, pageSize: PAGE_SIZE }),
    [search, roleId, page],
  );

  const { data, isLoading, isError, refetch } = useAdminUsers(query);
  // AD11 (role list) still requires MANAGE_ROLES; AD15 (this list) does not.
  const { data: me } = useQuery({ queryKey: ["auth", "me"], queryFn: getMe });
  // Until `me` resolves the filter stays hidden rather than flashing in and
  // out — the same direction AdminSidebar fails in, inverted because here the
  // cost of guessing wrong is a request that 403s.
  const canFilterByRole = hasPermission(me?.role, me?.permissions, "MANAGE_ROLES");
  const rolesQuery = useAdminRoles({ enabled: canFilterByRole });

  const pushParams = useCallback(
    (next: Record<string, string | undefined>) => {
      const params = new URLSearchParams(searchParams.toString());
      for (const [k, v] of Object.entries(next)) {
        if (v && v.length > 0) params.set(k, v);
        else params.delete(k);
      }
      const qs = params.toString();
      router.replace(qs ? `${pathname}?${qs}` : pathname);
    },
    [router, pathname, searchParams],
  );

  const fields: FilterField[] = [
    {
      key: "search",
      label: t("filters.search"),
      kind: "text",
      placeholder: t("filters.searchPlaceholder"),
    },
    ...(canFilterByRole
      ? [
          {
            key: "roleId",
            label: t("filters.role"),
            kind: "select" as const,
            placeholder: t("filters.allRoles"),
            options: (rolesQuery.data?.roles ?? []).map((r) => ({ value: r.id, label: r.name })),
          },
        ]
      : []),
  ];

  const initialValues: Record<string, string> = {};
  if (search) initialValues.search = search;
  if (roleId) initialValues.roleId = roleId;

  function handleApply(values: Record<string, string>) {
    // Filter changes reset to page 1.
    pushParams({ search: values.search, roleId: values.roleId, page: undefined });
  }

  function handleClear() {
    router.replace(pathname);
  }

  function handlePageChange(next: number) {
    pushParams({ page: next > 1 ? String(next) : undefined });
  }

  const totalPages = data ? Math.max(1, Math.ceil(data.totalCount / data.pageSize)) : 1;
  const isEmpty = !!data && data.items.length === 0;

  return (
    <div className="mx-auto w-full max-w-6xl px-4 py-6">
      <div className="mb-4 flex flex-col items-start justify-between gap-2 sm:flex-row sm:items-center">
        <div>
          <h1 className="text-2xl font-semibold text-gray-900">{t("title")}</h1>
          <p className="text-sm text-gray-500">{t("description")}</p>
        </div>
        <Link
          href={`/${locale}/admin/roles`}
          className="text-sm text-blue-600 hover:underline"
          data-testid="manage-roles-link"
        >
          {t("manageRolesLink")}
        </Link>
      </div>

      <FilterBar
        fields={fields}
        initialValues={initialValues}
        onApply={handleApply}
        onClear={handleClear}
        className="mb-3"
      />

      <p className="mb-4 rounded-md border border-gray-200 bg-gray-50 px-3 py-2 text-sm text-gray-600">
        {search ? t("scope.search") : t("scope.browse")}
      </p>

      {isError ? (
        <ErrorState message={t("loadError")} onRetry={() => refetch()} />
      ) : isLoading ? (
        <div className="flex flex-col gap-2">
          {[0, 1, 2, 3, 4].map((i) => (
            <Skeleton key={i} className="h-12" />
          ))}
        </div>
      ) : isEmpty ? (
        <EmptyState
          title={t("empty.title")}
          description={search ? t("empty.searchDescription") : t("empty.browseDescription")}
        />
      ) : (
        <>
          <p className="mb-2 text-xs text-gray-500">
            {t("resultCount", { count: data?.totalCount ?? 0 })}
          </p>
          <AdminUserTable users={data?.items ?? []} />
          <Pagination
            currentPage={data?.page ?? 1}
            totalPages={totalPages}
            onPageChange={handlePageChange}
            className="mt-4 justify-center"
          />
        </>
      )}
    </div>
  );
}
