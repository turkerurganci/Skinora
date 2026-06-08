"use client";

import { FormEvent, useEffect, useRef, useState } from "react";
import { useTranslations } from "next-intl";
import { cn } from "@/lib/utils/cn";
import { permissionLabelKey } from "@/lib/admin/permissionCatalog";
import type { AdminRoleSummary, AvailablePermission, RoleWriteRequest } from "@/lib/api/admin";

interface RoleFormProps {
  mode: "create" | "edit";
  initialRole?: AdminRoleSummary;
  availablePermissions: readonly AvailablePermission[];
  pending: boolean;
  errorMessage: string | null;
  onSubmit: (request: RoleWriteRequest) => void;
  onClose: () => void;
}

function RoleForm({
  mode,
  initialRole,
  availablePermissions,
  pending,
  errorMessage,
  onSubmit,
  onClose,
}: RoleFormProps) {
  const t = useTranslations("adminRoles");
  const [name, setName] = useState(initialRole?.name ?? "");
  const [description, setDescription] = useState(initialRole?.description ?? "");
  const [selected, setSelected] = useState<Set<string>>(
    () => new Set(initialRole?.permissions ?? []),
  );
  const [touched, setTouched] = useState(false);

  const nameEmpty = name.trim().length === 0;

  /** Localize a permission by `key`; fall back to the server label if absent. */
  function permissionLabel(p: AvailablePermission): string {
    const key = permissionLabelKey(p.key);
    return t.has(key) ? t(key) : p.label;
  }

  function togglePermission(key: string) {
    setSelected((prev) => {
      const next = new Set(prev);
      if (next.has(key)) next.delete(key);
      else next.add(key);
      return next;
    });
  }

  function handleSubmit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault();
    setTouched(true);
    if (nameEmpty) return;
    onSubmit({
      name: name.trim(),
      description: description.trim() === "" ? null : description.trim(),
      // Preserve the catalog order so the persisted list is deterministic.
      permissions: availablePermissions.map((p) => p.key).filter((k) => selected.has(k)),
    });
  }

  return (
    <form onSubmit={handleSubmit} className="flex max-h-[85vh] flex-col gap-4 overflow-y-auto p-6">
      <h2 id="role-form-title" className="text-lg font-semibold text-gray-900">
        {mode === "create" ? t("form.createTitle") : t("form.editTitle")}
      </h2>

      <label className="flex flex-col gap-1 text-sm">
        <span className="font-medium text-gray-700">{t("form.nameLabel")}</span>
        <input
          type="text"
          value={name}
          onChange={(e) => setName(e.target.value)}
          onBlur={() => setTouched(true)}
          placeholder={t("form.namePlaceholder")}
          required
          className={cn(
            "rounded-md border bg-white px-3 py-2 text-sm text-gray-900 focus:outline-none focus:ring-2",
            touched && nameEmpty
              ? "border-red-300 focus:ring-red-200"
              : "border-gray-300 focus:ring-blue-200",
          )}
        />
        {touched && nameEmpty && (
          <span className="text-xs text-red-600">{t("form.nameRequired")}</span>
        )}
      </label>

      <label className="flex flex-col gap-1 text-sm">
        <span className="font-medium text-gray-700">{t("form.descriptionLabel")}</span>
        <textarea
          value={description}
          onChange={(e) => setDescription(e.target.value)}
          placeholder={t("form.descriptionPlaceholder")}
          rows={2}
          className="rounded-md border border-gray-300 bg-white px-3 py-2 text-sm text-gray-900 focus:outline-none focus:ring-2 focus:ring-blue-200"
        />
      </label>

      <fieldset className="flex flex-col gap-2">
        <legend className="text-sm font-medium text-gray-700">{t("form.permissionsLabel")}</legend>
        <p className="text-xs text-gray-500">{t("form.permissionsHint")}</p>
        <div className="grid grid-cols-1 gap-1 sm:grid-cols-2">
          {availablePermissions.map((p) => (
            <label
              key={p.key}
              className="flex items-start gap-2 rounded-md px-2 py-1.5 text-sm hover:bg-gray-50"
            >
              <input
                type="checkbox"
                checked={selected.has(p.key)}
                onChange={() => togglePermission(p.key)}
                className="mt-0.5 h-4 w-4 rounded border-gray-300 text-blue-600 focus:ring-blue-200"
              />
              <span className="text-gray-700">{permissionLabel(p)}</span>
            </label>
          ))}
        </div>
      </fieldset>

      {errorMessage && <p className="text-sm text-red-600">{errorMessage}</p>}

      <div className="flex justify-end gap-2">
        <button
          type="button"
          onClick={onClose}
          disabled={pending}
          className="rounded-md border border-gray-300 bg-white px-3 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50 disabled:opacity-50"
        >
          {t("form.cancel")}
        </button>
        <button
          type="submit"
          disabled={pending || nameEmpty}
          className="rounded-md bg-blue-600 px-3 py-2 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-50"
        >
          {t("form.save")}
        </button>
      </div>
    </form>
  );
}

export interface RoleFormModalProps {
  open: boolean;
  mode: "create" | "edit";
  initialRole?: AdminRoleSummary;
  availablePermissions: readonly AvailablePermission[];
  pending?: boolean;
  errorMessage?: string | null;
  onSubmit: (request: RoleWriteRequest) => void;
  onClose: () => void;
  className?: string;
}

/**
 * S19 "Yeni Rol Oluştur" / "Düzenle" modal (04 §8.8) — name + description + the
 * 12-permission yetki matrix (rendered from the AD11 `availablePermissions`, so
 * the count tracks the backend catalog). Mirrors the `<dialog>` mechanics of
 * {@link FlagActionModal}; the inner form is keyed on the role id so its state
 * resets cleanly between create / edit / a different role.
 */
export function RoleFormModal({
  open,
  mode,
  initialRole,
  availablePermissions,
  pending = false,
  errorMessage = null,
  onSubmit,
  onClose,
  className,
}: RoleFormModalProps) {
  const dialogRef = useRef<HTMLDialogElement>(null);

  useEffect(() => {
    const dialog = dialogRef.current;
    if (!dialog) return;
    if (open && !dialog.open) {
      dialog.showModal();
    } else if (!open && dialog.open) {
      dialog.close();
    }
  }, [open]);

  useEffect(() => {
    const dialog = dialogRef.current;
    if (!dialog) return;
    const handleCancel = (e: Event) => {
      e.preventDefault();
      onClose();
    };
    dialog.addEventListener("cancel", handleCancel);
    return () => dialog.removeEventListener("cancel", handleCancel);
  }, [onClose]);

  return (
    <dialog
      ref={dialogRef}
      className={cn("w-full max-w-lg rounded-lg p-0 backdrop:bg-black/50", className)}
      aria-labelledby="role-form-title"
    >
      {open && (
        <RoleForm
          key={initialRole?.id ?? "create"}
          mode={mode}
          initialRole={initialRole}
          availablePermissions={availablePermissions}
          pending={pending}
          errorMessage={errorMessage}
          onSubmit={onSubmit}
          onClose={onClose}
        />
      )}
    </dialog>
  );
}
