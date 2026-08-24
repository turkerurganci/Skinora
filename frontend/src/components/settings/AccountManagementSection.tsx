"use client";

import { FormEvent, useEffect, useRef, useState } from "react";
import { useRouter } from "next/navigation";
import { useLocale, useTranslations } from "next-intl";
import { ApiError } from "@/lib/api/client";
import { DELETE_ACCOUNT_CONFIRMATION, deactivateAccount, deleteAccount } from "@/lib/api/settings";
import { signOut } from "@/lib/auth/signOut";
import { cn } from "@/lib/utils/cn";

type Mode = "none" | "deactivate" | "delete";

interface ConfirmModalProps {
  mode: "deactivate" | "delete";
  open: boolean;
  submitting: boolean;
  error: string | null;
  onConfirm: () => void;
  onClose: () => void;
}

interface ConfirmModalBodyProps {
  mode: "deactivate" | "delete";
  submitting: boolean;
  error: string | null;
  onConfirm: () => void;
  onClose: () => void;
}

function ConfirmModalBody({ mode, submitting, error, onConfirm, onClose }: ConfirmModalBodyProps) {
  const t = useTranslations("settings.accountManagement");
  const [phrase, setPhrase] = useState("");

  function handleSubmit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault();
    if (mode === "delete" && phrase !== DELETE_ACCOUNT_CONFIRMATION) return;
    onConfirm();
  }

  const isDelete = mode === "delete";
  const canSubmit = isDelete ? phrase === DELETE_ACCOUNT_CONFIRMATION : true;

  return (
    <form onSubmit={handleSubmit} className="flex flex-col gap-4 p-6">
      <h2 id="account-mgmt-modal-title" className="text-lg font-semibold text-gray-900">
        {t(isDelete ? "delete.modalTitle" : "deactivate.modalTitle")}
      </h2>
      <p className={cn("text-sm", isDelete ? "text-red-700" : "text-gray-700")}>
        {t(isDelete ? "delete.modalWarning" : "deactivate.modalDescription")}
      </p>

      {isDelete && (
        <label className="flex flex-col gap-1 text-sm">
          <span className="font-medium text-gray-700">
            {t("delete.confirmLabel", { phrase: DELETE_ACCOUNT_CONFIRMATION })}
          </span>
          <input
            type="text"
            value={phrase}
            onChange={(e) => setPhrase(e.target.value)}
            placeholder={DELETE_ACCOUNT_CONFIRMATION}
            autoFocus
            className="rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-red-500 focus:outline-none focus:ring-1 focus:ring-red-500"
          />
        </label>
      )}

      {error && <p className="rounded-md bg-red-50 px-3 py-2 text-sm text-red-700">{error}</p>}

      <div className="flex justify-end gap-2">
        <button
          type="button"
          onClick={onClose}
          className="rounded-md border border-gray-300 bg-white px-3 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50"
        >
          {t("dismiss")}
        </button>
        <button
          type="submit"
          disabled={!canSubmit || submitting}
          className={cn(
            "rounded-md px-3 py-2 text-sm font-medium text-white disabled:opacity-50",
            isDelete ? "bg-red-600 hover:bg-red-700" : "bg-gray-600 hover:bg-gray-700",
          )}
        >
          {submitting
            ? t("submitting")
            : t(isDelete ? "delete.confirmButton" : "deactivate.confirmButton")}
        </button>
      </div>
    </form>
  );
}

function ConfirmModal({ mode, open, submitting, error, onConfirm, onClose }: ConfirmModalProps) {
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
      if (!submitting) onClose();
    };
    dialog.addEventListener("cancel", handleCancel);
    return () => dialog.removeEventListener("cancel", handleCancel);
  }, [onClose, submitting]);

  return (
    <dialog
      ref={dialogRef}
      className="w-full max-w-md rounded-lg p-0 backdrop:bg-black/50"
      aria-labelledby="account-mgmt-modal-title"
    >
      {open && (
        <ConfirmModalBody
          mode={mode}
          submitting={submitting}
          error={error}
          onConfirm={onConfirm}
          onClose={onClose}
        />
      )}
    </dialog>
  );
}

/**
 * 04 §7.6 Hesap Yönetimi. İki ayrı akış:
 *
 *  - **Deaktif Et:** Modal onay → U13 → 422 `HAS_ACTIVE_TRANSACTIONS`
 *    backend tarafından döner; UI banner gösterir. Başarı → cookie
 *    backend tarafından silinir, store reset + landing'e redirect.
 *  - **Sil:** Ciddi uyarı modal + "SİL" verbatim input (tüm dillerde sabit
 *    — backend `UsersController.cs:496` `"SİL"` bekliyor) → U14 → aynı
 *    redirect davranışı.
 *
 * Backend aktif işlem kontrolünü zorunlu kılıyor; client ön-kontrol yok
 * (race-free / tek kaynak).
 */
export function AccountManagementSection() {
  const t = useTranslations("settings.accountManagement");
  const router = useRouter();
  const locale = useLocale();
  // F7a — hesabı kapatan/silen kullanıcının refresh token'ı da iptal edilmeli.

  const [mode, setMode] = useState<Mode>("none");
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  function close() {
    if (submitting) return;
    setMode("none");
    setError(null);
  }

  async function handleConfirm() {
    setSubmitting(true);
    setError(null);
    try {
      if (mode === "deactivate") {
        await deactivateAccount();
      } else if (mode === "delete") {
        await deleteAccount(DELETE_ACCOUNT_CONFIRMATION);
      }
      // F7a — signOut A8'i çağırıp refresh token'ı sunucuda iptal eder, sonra
      // yerel token'ı temizler. Önceden yalnız yerel temizlik vardı: hesabını
      // silen kullanıcının oturumu sunucuda 7 gün daha uyandırılabilirdi.
      await signOut();
      router.replace(`/${locale}`);
    } catch (err) {
      if (err instanceof ApiError) {
        if (err.code === "HAS_ACTIVE_TRANSACTIONS") {
          setError(t("errors.hasActiveTransactions"));
        } else if (err.code === "VALIDATION_ERROR") {
          setError(t("errors.invalidConfirmation"));
        } else {
          setError(t("errors.generic"));
        }
      } else {
        setError(t("errors.generic"));
      }
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <section className="rounded-lg border border-gray-200 bg-white p-6">
      <h2 className="mb-2 text-lg font-semibold text-gray-900">{t("title")}</h2>
      <p className="mb-4 text-sm text-gray-600">{t("description")}</p>

      <div className="flex flex-col gap-3 sm:flex-row">
        <button
          type="button"
          onClick={() => {
            setMode("deactivate");
            setError(null);
          }}
          className="rounded-md border border-gray-300 bg-white px-3 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50"
        >
          {t("deactivate.button")}
        </button>
        <button
          type="button"
          onClick={() => {
            setMode("delete");
            setError(null);
          }}
          className="rounded-md border border-red-300 bg-white px-3 py-2 text-sm font-medium text-red-700 hover:bg-red-50"
        >
          {t("delete.button")}
        </button>
      </div>

      <ConfirmModal
        mode={mode === "none" ? "deactivate" : mode}
        open={mode !== "none"}
        submitting={submitting}
        error={error}
        onConfirm={handleConfirm}
        onClose={close}
      />
    </section>
  );
}
