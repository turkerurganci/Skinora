"use client";

import { FormEvent, useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { useTranslations } from "next-intl";
import { ApiError } from "@/lib/api/client";
import {
  AccountSettings,
  sendEmailVerification,
  updateNotifications,
  verifyEmail,
} from "@/lib/api/settings";
import { cn } from "@/lib/utils/cn";

type Channel = "email" | "telegram" | "discord";

interface ToggleRowProps {
  label: string;
  description?: string;
  checked: boolean;
  disabled?: boolean;
  onChange: (next: boolean) => void;
  disabledReason?: string;
}

function ToggleRow({
  label,
  description,
  checked,
  disabled,
  onChange,
  disabledReason,
}: ToggleRowProps) {
  return (
    <div className="flex items-start justify-between gap-4 py-3">
      <div className="flex-1">
        <div className="font-medium text-gray-900">{label}</div>
        {description && <div className="text-sm text-gray-500">{description}</div>}
        {disabled && disabledReason && (
          <div className="mt-1 text-xs text-gray-500">{disabledReason}</div>
        )}
      </div>
      <button
        type="button"
        role="switch"
        aria-checked={checked}
        aria-label={label}
        disabled={disabled}
        onClick={() => !disabled && onChange(!checked)}
        className={cn(
          "relative inline-flex h-6 w-11 flex-shrink-0 cursor-pointer rounded-full border-2 border-transparent transition-colors",
          "focus:outline-none focus:ring-2 focus:ring-blue-500 focus:ring-offset-2",
          checked ? "bg-blue-600" : "bg-gray-300",
          disabled && "cursor-not-allowed opacity-50",
        )}
      >
        <span
          aria-hidden="true"
          className={cn(
            "inline-block h-5 w-5 transform rounded-full bg-white shadow ring-0 transition",
            checked ? "translate-x-5" : "translate-x-0",
          )}
        />
      </button>
    </div>
  );
}

export interface NotificationPreferencesSectionProps {
  settings: AccountSettings;
}

/**
 * 04 §7.6 — Bildirim Tercihleri. Platform satırı her zaman aktif ve
 * devre dışı bırakılamaz (`canDisable=false` server-side garanti). Email
 * için adres + doğrulama akışı: kullanıcı adresi girer → "Kaydet" → backend
 * persist → "Doğrulama Kodu Gönder" → kod 10dk geçerli → "Doğrula" → verified.
 *
 * Telegram/Discord toggle'ları sadece `connected=true` iken aktif. Bağlama
 * akışı LinkedAccountsSection'da; U7 backend'i `CHANNEL_NOT_CONNECTED`
 * 422 ile koruma katmanını sağlıyor (defense-in-depth).
 */
export function NotificationPreferencesSection({ settings }: NotificationPreferencesSectionProps) {
  const t = useTranslations("settings.notifications");
  const queryClient = useQueryClient();

  const [emailAddress, setEmailAddress] = useState(settings.notifications.email.address ?? "");
  const [emailSaving, setEmailSaving] = useState(false);
  const [emailError, setEmailError] = useState<string | null>(null);
  const [verifyOpen, setVerifyOpen] = useState(false);
  const [verifySending, setVerifySending] = useState(false);
  const [verifySentTo, setVerifySentTo] = useState<string | null>(null);
  const [verifyCode, setVerifyCode] = useState("");
  const [verifying, setVerifying] = useState(false);
  const [verifyError, setVerifyError] = useState<string | null>(null);

  const email = settings.notifications.email;
  const telegram = settings.notifications.telegram;
  const discord = settings.notifications.discord;

  function invalidate() {
    return queryClient.invalidateQueries({ queryKey: ["users", "me", "settings"] });
  }

  async function toggleChannel(channel: Channel, enabled: boolean) {
    try {
      await updateNotifications({ [channel]: { enabled } });
      await invalidate();
    } catch (err) {
      if (err instanceof ApiError && err.code === "CHANNEL_NOT_CONNECTED") {
        // Backend rejects toggle if channel not connected — UI hint via
        // disabledReason already covers this, but surface the error if
        // racing connection state.
        return;
      }
      throw err;
    }
  }

  async function handleEmailAddressSave(e: FormEvent<HTMLFormElement>) {
    e.preventDefault();
    const trimmed = emailAddress.trim();
    if (!trimmed) {
      setEmailError(t("email.errors.required"));
      return;
    }
    setEmailSaving(true);
    setEmailError(null);
    try {
      await updateNotifications({ email: { address: trimmed } });
      await invalidate();
    } catch (err) {
      setEmailError(
        err instanceof ApiError && err.code === "VALIDATION_ERROR"
          ? t("email.errors.invalid")
          : t("email.errors.generic"),
      );
    } finally {
      setEmailSaving(false);
    }
  }

  async function handleSendVerification() {
    setVerifySending(true);
    setVerifyError(null);
    try {
      const result = await sendEmailVerification();
      setVerifySentTo(result.sentTo);
      setVerifyOpen(true);
    } catch (err) {
      if (err instanceof ApiError) {
        if (err.code === "NO_EMAIL_SET") {
          setVerifyError(t("verify.errors.noEmail"));
        } else if (err.code === "VERIFICATION_COOLDOWN") {
          setVerifyError(t("verify.errors.cooldown"));
        } else {
          setVerifyError(t("verify.errors.generic"));
        }
      } else {
        setVerifyError(t("verify.errors.generic"));
      }
    } finally {
      setVerifySending(false);
    }
  }

  async function handleVerifyCode(e: FormEvent<HTMLFormElement>) {
    e.preventDefault();
    if (!verifyCode.trim()) return;
    setVerifying(true);
    setVerifyError(null);
    try {
      await verifyEmail(verifyCode.trim());
      await invalidate();
      setVerifyOpen(false);
      setVerifyCode("");
      setVerifySentTo(null);
    } catch (err) {
      if (err instanceof ApiError) {
        if (err.code === "INVALID_VERIFICATION_CODE") {
          setVerifyError(t("verify.errors.invalidCode"));
        } else if (err.code === "VERIFICATION_CODE_EXPIRED") {
          setVerifyError(t("verify.errors.expired"));
        } else {
          setVerifyError(t("verify.errors.generic"));
        }
      } else {
        setVerifyError(t("verify.errors.generic"));
      }
    } finally {
      setVerifying(false);
    }
  }

  const emailAddressChanged = emailAddress.trim() !== (email.address ?? "").trim();
  const canSendVerification = !!email.address && !email.verified && !emailAddressChanged;

  return (
    <section className="rounded-lg border border-gray-200 bg-white p-6">
      <h2 className="mb-4 text-lg font-semibold text-gray-900">{t("title")}</h2>

      <div className="divide-y divide-gray-100">
        <ToggleRow
          label={t("channels.platform.label")}
          description={t("channels.platform.description")}
          checked={settings.notifications.platform.enabled}
          disabled={!settings.notifications.platform.canDisable}
          disabledReason={t("channels.platform.locked")}
          onChange={() => {
            /* Platform channel cannot be disabled — server-side enforced */
          }}
        />

        <div className="py-3">
          <ToggleRow
            label={t("channels.email.label")}
            description={t("channels.email.description")}
            checked={email.enabled}
            disabled={!email.address}
            disabledReason={!email.address ? t("channels.email.noAddress") : undefined}
            onChange={(next) => void toggleChannel("email", next)}
          />

          <form
            onSubmit={handleEmailAddressSave}
            className="mt-3 flex flex-col gap-2 sm:flex-row sm:items-start"
          >
            <div className="flex-1">
              <label className="sr-only" htmlFor="settings-email-input">
                {t("email.addressLabel")}
              </label>
              <input
                id="settings-email-input"
                type="email"
                value={emailAddress}
                onChange={(e) => setEmailAddress(e.target.value)}
                placeholder={t("email.placeholder")}
                className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
              />
              {email.address && email.verified && (
                <p className="mt-1 text-xs text-green-700">{t("email.verifiedBadge")}</p>
              )}
              {email.address && !email.verified && (
                <p className="mt-1 text-xs text-amber-700">{t("email.unverifiedBadge")}</p>
              )}
              {emailError && <p className="mt-1 text-xs text-red-600">{emailError}</p>}
            </div>
            <div className="flex gap-2">
              <button
                type="submit"
                disabled={!emailAddressChanged || emailSaving}
                className="rounded-md bg-blue-600 px-3 py-2 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-50"
              >
                {emailSaving ? t("email.saving") : t("email.save")}
              </button>
              {canSendVerification && (
                <button
                  type="button"
                  onClick={handleSendVerification}
                  disabled={verifySending}
                  className="rounded-md border border-blue-600 px-3 py-2 text-sm font-medium text-blue-700 hover:bg-blue-50 disabled:opacity-50"
                >
                  {verifySending ? t("verify.sending") : t("verify.sendButton")}
                </button>
              )}
            </div>
          </form>

          {verifyOpen && (
            <form
              onSubmit={handleVerifyCode}
              className="mt-3 rounded-md border border-blue-200 bg-blue-50 p-3"
            >
              {verifySentTo && (
                <p className="mb-2 text-sm text-blue-900">
                  {t("verify.sentTo", { address: verifySentTo })}
                </p>
              )}
              <div className="flex gap-2">
                <input
                  type="text"
                  inputMode="numeric"
                  value={verifyCode}
                  onChange={(e) => setVerifyCode(e.target.value)}
                  placeholder={t("verify.codePlaceholder")}
                  className="flex-1 rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
                />
                <button
                  type="submit"
                  disabled={!verifyCode.trim() || verifying}
                  className="rounded-md bg-blue-600 px-3 py-2 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-50"
                >
                  {verifying ? t("verify.verifying") : t("verify.verify")}
                </button>
                <button
                  type="button"
                  onClick={() => {
                    setVerifyOpen(false);
                    setVerifyCode("");
                    setVerifyError(null);
                  }}
                  className="rounded-md border border-gray-300 bg-white px-3 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50"
                >
                  {t("verify.cancel")}
                </button>
              </div>
              {verifyError && <p className="mt-2 text-xs text-red-600">{verifyError}</p>}
            </form>
          )}

          {!verifyOpen && verifyError && <p className="mt-2 text-xs text-red-600">{verifyError}</p>}
        </div>

        <ToggleRow
          label={t("channels.telegram.label")}
          description={
            telegram.connected && telegram.username
              ? t("channels.telegram.connected", { username: telegram.username })
              : t("channels.telegram.description")
          }
          checked={telegram.enabled}
          disabled={!telegram.connected}
          disabledReason={!telegram.connected ? t("channels.telegram.notConnected") : undefined}
          onChange={(next) => void toggleChannel("telegram", next)}
        />

        <ToggleRow
          label={t("channels.discord.label")}
          description={
            discord.connected && discord.username
              ? t("channels.discord.connected", { username: discord.username })
              : t("channels.discord.description")
          }
          checked={discord.enabled}
          disabled={!discord.connected}
          disabledReason={!discord.connected ? t("channels.discord.notConnected") : undefined}
          onChange={(next) => void toggleChannel("discord", next)}
        />
      </div>
    </section>
  );
}
