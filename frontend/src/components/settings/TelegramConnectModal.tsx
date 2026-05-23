"use client";

import { useEffect, useRef, useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { useTranslations } from "next-intl";
import { TelegramConnectResponse, getAccountSettings } from "@/lib/api/settings";
import { cn } from "@/lib/utils/cn";

export interface TelegramConnectModalProps {
  open: boolean;
  payload: TelegramConnectResponse | null;
  onClose: () => void;
}

interface TelegramConnectModalBodyProps {
  payload: TelegramConnectResponse;
  onClose: () => void;
  onCopied: () => void;
  onConnected: () => void;
}

/**
 * 04 §7.6 Telegram bağlama akışı (5 adım). Modal kullanıcıya doğrulama
 * kodu + bot link gösterir. Backend webhook (W1) `/start {code}` komutunu
 * aldığında bağlantı kurulur; UI manuel "Kontrol Et" ile settings refetch
 * eder (SignalR `TelegramConnected` push T96 forward-deferred).
 *
 * Body, payload `null` değilken ayrı bir component'e ayrılmış — modal
 * kapalıyken `<dialog>` içine hiçbir state render etmiyoruz (CancelModal
 * paterni).
 */
function TelegramConnectModalBody({
  payload,
  onClose,
  onCopied,
  onConnected,
}: TelegramConnectModalBodyProps) {
  const t = useTranslations("settings.linkedAccounts.telegram.modal");
  const queryClient = useQueryClient();
  const [copied, setCopied] = useState(false);
  const [checking, setChecking] = useState(false);
  const [stillNotConnected, setStillNotConnected] = useState(false);

  async function handleCopy() {
    try {
      await navigator.clipboard.writeText(payload.verificationCode);
      setCopied(true);
      onCopied();
      window.setTimeout(() => setCopied(false), 2000);
    } catch {
      /* Clipboard unavailable — fall back to manual selection */
    }
  }

  async function handleCheck() {
    setChecking(true);
    setStillNotConnected(false);
    try {
      const fresh = await getAccountSettings();
      queryClient.setQueryData(["users", "me", "settings"], fresh);
      if (fresh.notifications.telegram.connected) {
        onConnected();
      } else {
        setStillNotConnected(true);
      }
    } finally {
      setChecking(false);
    }
  }

  return (
    <div className="flex flex-col gap-4 p-6">
      <h2 id="telegram-modal-title" className="text-lg font-semibold text-gray-900">
        {t("title")}
      </h2>
      <ol className="list-decimal space-y-2 pl-5 text-sm text-gray-700">
        <li>{t("steps.openBot")}</li>
        <li>{t("steps.sendCode")}</li>
        <li>{t("steps.wait")}</li>
        <li>{t("steps.check")}</li>
      </ol>

      <div className="rounded-md bg-gray-50 p-3">
        <div className="text-xs font-medium uppercase text-gray-500">{t("verificationCode")}</div>
        <div className="mt-1 flex items-center gap-2">
          <code className="flex-1 rounded bg-white px-2 py-1 font-mono text-sm text-gray-900">
            {payload.verificationCode}
          </code>
          <button
            type="button"
            onClick={handleCopy}
            className="rounded-md border border-gray-300 bg-white px-2 py-1 text-xs font-medium text-gray-700 hover:bg-gray-50"
          >
            {copied ? t("copied") : t("copy")}
          </button>
        </div>
        <div className="mt-1 text-xs text-gray-500">
          {t("expiresIn", { seconds: payload.expiresIn })}
        </div>
      </div>

      <a
        href={payload.botUrl}
        target="_blank"
        rel="noopener noreferrer"
        className="rounded-md bg-blue-600 px-3 py-2 text-center text-sm font-medium text-white hover:bg-blue-700"
      >
        {t("openBot")}
      </a>

      {stillNotConnected && (
        <p className="rounded-md bg-amber-50 px-3 py-2 text-sm text-amber-800">
          {t("stillNotConnected")}
        </p>
      )}

      <div className="flex justify-between gap-2">
        <button
          type="button"
          onClick={onClose}
          className="rounded-md border border-gray-300 bg-white px-3 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50"
        >
          {t("dismiss")}
        </button>
        <button
          type="button"
          onClick={handleCheck}
          disabled={checking}
          className="rounded-md bg-green-600 px-3 py-2 text-sm font-medium text-white hover:bg-green-700 disabled:opacity-50"
        >
          {checking ? t("checking") : t("check")}
        </button>
      </div>
    </div>
  );
}

export function TelegramConnectModal({ open, payload, onClose }: TelegramConnectModalProps) {
  const dialogRef = useRef<HTMLDialogElement>(null);

  useEffect(() => {
    const dialog = dialogRef.current;
    if (!dialog) return;
    if (open && payload && !dialog.open) {
      dialog.showModal();
    } else if (!open && dialog.open) {
      dialog.close();
    }
  }, [open, payload]);

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
      className={cn("w-full max-w-md rounded-lg p-0 backdrop:bg-black/50")}
      aria-labelledby="telegram-modal-title"
    >
      {open && payload && (
        <TelegramConnectModalBody
          payload={payload}
          onClose={onClose}
          onCopied={() => {
            /* placeholder for analytics */
          }}
          onConnected={onClose}
        />
      )}
    </dialog>
  );
}
