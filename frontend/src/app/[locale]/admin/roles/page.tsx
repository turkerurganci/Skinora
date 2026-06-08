"use client";

import { useTranslations } from "next-intl";
import { RolesManager } from "@/components/admin";

/**
 * S19 — Admin Rol & Yetki Yönetimi (04 §8.8). Super-admin-only screen wiring
 * AD11–AD17: the roles list + create / edit / delete, the 12-permission yetki
 * matrix, and the user-role assignment section. The backend enforces
 * MANAGE_ROLES; there is no frontend permission guard (consistent with the
 * other admin pages — every AD endpoint is server-protected).
 */
export default function AdminRolesPage() {
  const t = useTranslations("adminRoles");

  return (
    <div className="mx-auto w-full max-w-4xl px-4 py-6">
      <h1 className="mb-4 text-2xl font-semibold text-gray-900">{t("title")}</h1>
      <RolesManager />
    </div>
  );
}
