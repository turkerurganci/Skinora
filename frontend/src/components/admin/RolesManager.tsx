"use client";

import { useState } from "react";
import { useTranslations } from "next-intl";
import { ErrorState, Skeleton, useToast } from "@/components/common";
import { ApiError } from "@/lib/api/client";
import { useAdminRoles } from "@/lib/hooks/useAdminRoles";
import { useCreateRole, useDeleteRole, useUpdateRole } from "@/lib/hooks/useAdminRoleMutations";
import type { AdminRoleSummary, RoleWriteRequest } from "@/lib/api/admin";
import { FlagActionModal } from "./FlagActionModal";
import { RoleFormModal } from "./RoleFormModal";
import { RolesTable } from "./RolesTable";
import { UserRoleAssignment } from "./UserRoleAssignment";

type FormState = { mode: "create" } | { mode: "edit"; role: AdminRoleSummary };

/**
 * S19 — Admin Rol & Yetki Yönetimi (04 §8.8). Loads the AD11 roles + permission
 * catalog in one request and orchestrates the three sub-surfaces: the roles
 * table, the create/edit modal (AD12/AD13), the delete confirm (AD14), and the
 * user-role assignment section (AD15/AD17). Role-name collisions and the
 * delete-with-users guard surface as inline errors / toasts respectively.
 */
export function RolesManager() {
  const t = useTranslations("adminRoles");
  const { push } = useToast();

  const { data, isLoading, isError, refetch } = useAdminRoles();
  const createRole = useCreateRole();
  const updateRole = useUpdateRole();
  const deleteRole = useDeleteRole();

  const [formState, setFormState] = useState<FormState | null>(null);
  const [deleteTarget, setDeleteTarget] = useState<AdminRoleSummary | null>(null);

  const activeMutation = formState?.mode === "edit" ? updateRole : createRole;

  function closeForm() {
    createRole.reset();
    updateRole.reset();
    setFormState(null);
  }

  function handleSubmit(request: RoleWriteRequest) {
    if (formState?.mode === "edit") {
      updateRole.mutate(
        { id: formState.role.id, request },
        {
          onSuccess: () => {
            push({ variant: "success", message: t("form.updated") });
            closeForm();
          },
        },
      );
    } else {
      createRole.mutate(request, {
        onSuccess: () => {
          push({ variant: "success", message: t("form.created") });
          closeForm();
        },
      });
    }
  }

  function formErrorMessage(): string | null {
    if (!activeMutation.isError) return null;
    const err = activeMutation.error;
    if (err instanceof ApiError && err.code === "ROLE_NAME_EXISTS") return t("form.nameExists");
    return t("form.saveError");
  }

  function handleDelete() {
    if (!deleteTarget) return;
    deleteRole.mutate(deleteTarget.id, {
      onSuccess: () => {
        push({ variant: "success", message: t("deleteConfirm.deleted") });
        setDeleteTarget(null);
      },
      onError: (err) => {
        const message =
          err instanceof ApiError && err.code === "ROLE_HAS_USERS"
            ? t("deleteConfirm.hasUsers")
            : t("deleteConfirm.deleteError");
        push({ variant: "error", message });
        setDeleteTarget(null);
      },
    });
  }

  if (isError) {
    return <ErrorState message={t("loadError")} onRetry={() => refetch()} />;
  }

  if (isLoading || !data) {
    return (
      <div className="flex flex-col gap-2">
        {[0, 1, 2, 3].map((i) => (
          <Skeleton key={i} className="h-14" />
        ))}
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-8">
      <section className="flex flex-col gap-3">
        <div className="flex items-center justify-between gap-2">
          <h2 className="text-lg font-semibold text-gray-900">{t("roles.heading")}</h2>
          <button
            type="button"
            onClick={() => setFormState({ mode: "create" })}
            className="rounded-md bg-blue-600 px-3 py-2 text-sm font-medium text-white hover:bg-blue-700"
          >
            {t("roles.createButton")}
          </button>
        </div>
        <RolesTable
          roles={data.roles}
          onEdit={(role) => setFormState({ mode: "edit", role })}
          onDelete={setDeleteTarget}
        />
      </section>

      <UserRoleAssignment roles={data.roles} />

      <RoleFormModal
        open={formState !== null}
        mode={formState?.mode ?? "create"}
        initialRole={formState?.mode === "edit" ? formState.role : undefined}
        availablePermissions={data.availablePermissions}
        pending={activeMutation.isPending}
        errorMessage={formErrorMessage()}
        onSubmit={handleSubmit}
        onClose={closeForm}
      />

      <FlagActionModal
        open={deleteTarget !== null}
        title={t("deleteConfirm.title")}
        description={deleteTarget ? t("deleteConfirm.message", { name: deleteTarget.name }) : ""}
        confirmLabel={t("deleteConfirm.confirm")}
        cancelLabel={t("deleteConfirm.cancel")}
        tone="reject"
        pending={deleteRole.isPending}
        onConfirm={() => handleDelete()}
        onClose={() => {
          deleteRole.reset();
          setDeleteTarget(null);
        }}
      />
    </div>
  );
}
