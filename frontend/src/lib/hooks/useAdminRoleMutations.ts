"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import {
  assignUserRole,
  createAdminRole,
  deleteAdminRole,
  updateAdminRole,
  type RoleWriteRequest,
} from "@/lib/api/admin";

/**
 * Invalidate every admin surface a role write can change: the S19 role list
 * (assignedUserCount / permissions) and the user list (a renamed or deleted
 * role changes the inline role badge each user shows).
 */
function invalidateRoleSurfaces(queryClient: ReturnType<typeof useQueryClient>) {
  queryClient.invalidateQueries({ queryKey: ["admin", "roles"] });
  queryClient.invalidateQueries({ queryKey: ["admin", "users"] });
}

/** AD12 — create a role ("Yeni Rol Oluştur", 04 §8.8). */
export function useCreateRole() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (request: RoleWriteRequest) => createAdminRole(request),
    onSuccess: () => invalidateRoleSurfaces(queryClient),
  });
}

/** AD13 — update a role ("Düzenle", 04 §8.8). */
export function useUpdateRole() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, request }: { id: string; request: RoleWriteRequest }) =>
      updateAdminRole(id, request),
    onSuccess: () => invalidateRoleSurfaces(queryClient),
  });
}

/** AD14 — delete a role ("Sil", 04 §8.8). Backend refuses (422) if users are assigned. */
export function useDeleteRole() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => deleteAdminRole(id),
    onSuccess: () => invalidateRoleSurfaces(queryClient),
  });
}

/**
 * AD17 — assign or clear a user's role ("Rol Ata" / "Rol Değiştir", 04 §8.8).
 * Changing an assignment shifts each role's `assignedUserCount`, so both the
 * user list and the role list are invalidated.
 */
export function useAssignUserRole() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ userId, roleId }: { userId: string; roleId: string | null }) =>
      assignUserRole(userId, roleId),
    onSuccess: () => invalidateRoleSurfaces(queryClient),
  });
}
