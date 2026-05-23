"use client";

import { useEffect, useState } from "react";
import { usePathname, useRouter } from "next/navigation";
import { useTranslations } from "next-intl";
import { ApiError } from "@/lib/api/client";
import { useAuthStore } from "@/lib/stores/auth-store";
import { useAccountSettings } from "@/lib/hooks/useAccountSettings";
import { ErrorState, Skeleton } from "@/components/common";
import { SuspendedBanner } from "@/components/dashboard";
import {
  AccountManagementSection,
  LanguagePreferenceSection,
  LinkedAccountsSection,
  NotificationPreferencesSection,
} from "@/components/settings";

type DiscordCallbackStatus =
  | "connected"
  | "denied"
  | "already_linked"
  | "expired"
  | "exchange_failed"
  | "invalid_state"
  | "error";

/**
 * Capture Discord OAuth callback query params from `window.location` once
 * on mount (U10b — 07 §5.13). Same lazy-init pattern profile/page.tsx uses
 * for the re-auth token: SSR guard returns null; client re-reads on hydration.
 */
function captureDiscordCallback(): { status: DiscordCallbackStatus | null } {
  if (typeof window === "undefined") return { status: null };
  const params = new URLSearchParams(window.location.search);
  const discord = params.get("discord");
  if (discord === "connected") return { status: "connected" };
  if (discord === "error") {
    const reason = params.get("reason");
    const mapped: Record<string, DiscordCallbackStatus> = {
      denied: "denied",
      already_linked: "already_linked",
      expired: "expired",
      exchange_failed: "exchange_failed",
      invalid_state: "invalid_state",
    };
    return { status: reason && mapped[reason] ? mapped[reason] : "error" };
  }
  return { status: null };
}

/**
 * S10 — Hesap Ayarları (04 §7.6). Authenticated kullanıcı bildirim
 * tercihlerini, bağlı hesaplarını, dil tercihini ve hesap durumunu
 * yönetir.
 *
 * Discord OAuth callback handling: backend U10b redirect ile
 * `/settings?discord=connected|error&reason=...` döndürür. Page mount'unda
 * query param parse edilir → banner gösterilir → router.replace ile param
 * URL'den temizlenir (T93 re-auth token paterni — referrer/history sızıntısı
 * önleme).
 *
 * Known limitations (T-future devir):
 *   K1 — SignalR `TelegramConnected`/`DiscordConnected` realtime push
 *        yok — T96 devir; modal "Kontrol Et" + page-level callback ile
 *        kapsanır.
 *   K2 — Verification kodu countdown UI yok; backend `expiresIn` saniye
 *        olarak döner ama UI sadece toplam süreyi gösterir (T96
 *        countdown shared util ile birleşebilir).
 *   K3 — Language change locale prefix'i yenilemek için `router.replace`
 *        kullanır; full page reload yapılmadığından React Query cache
 *        eski locale anahtarlarını korur (next-intl provider yeniden
 *        mount ettiğinde drift düzelir; manuel test edildi).
 */
export default function SettingsPage() {
  const t = useTranslations("settings");
  const router = useRouter();
  const pathname = usePathname();

  const isAuthenticated = useAuthStore((s) => s.isAuthenticated);
  const isSuspended = useAuthStore((s) => s.isSuspended);
  const settings = useAccountSettings(isAuthenticated);

  const [discordCallback, setDiscordCallback] = useState(captureDiscordCallback);

  useEffect(() => {
    if (!discordCallback.status) return;
    if (typeof window === "undefined") return;
    const params = new URLSearchParams(window.location.search);
    if (!params.has("discord") && !params.has("reason")) return;
    params.delete("discord");
    params.delete("reason");
    const query = params.toString();
    router.replace(query ? `${pathname}?${query}` : pathname);
  }, [pathname, discordCallback.status, router]);

  function dismissDiscordBanner() {
    setDiscordCallback({ status: null });
  }

  if (!isAuthenticated) {
    return (
      <ErrorState title={t("errors.forbidden.title")} message={t("errors.forbidden.message")} />
    );
  }

  if (settings.isLoading) {
    return (
      <div className="mx-auto w-full max-w-4xl space-y-4 px-4 py-6">
        <Skeleton className="h-12 w-48" />
        <Skeleton className="h-72 w-full" />
        <Skeleton className="h-48 w-full" />
        <Skeleton className="h-32 w-full" />
        <Skeleton className="h-32 w-full" />
      </div>
    );
  }

  if (settings.error instanceof ApiError && settings.error.status === 401) {
    return (
      <ErrorState title={t("errors.forbidden.title")} message={t("errors.forbidden.message")} />
    );
  }

  if (settings.isError || !settings.data) {
    return (
      <ErrorState
        title={t("errors.generic.title")}
        message={t("errors.generic.message")}
        onRetry={() => settings.refetch()}
      />
    );
  }

  const data = settings.data;

  return (
    <div className="mx-auto w-full max-w-4xl space-y-4 px-4 py-6">
      {isSuspended && <SuspendedBanner />}

      <header>
        <h1 className="text-2xl font-bold text-gray-900">{t("pageTitle")}</h1>
      </header>

      {discordCallback.status && (
        <DiscordCallbackBanner status={discordCallback.status} onDismiss={dismissDiscordBanner} />
      )}

      <NotificationPreferencesSection settings={data} />
      <LinkedAccountsSection settings={data} />
      <LanguagePreferenceSection settings={data} />
      <AccountManagementSection />
    </div>
  );
}

interface DiscordCallbackBannerProps {
  status: DiscordCallbackStatus;
  onDismiss: () => void;
}

function DiscordCallbackBanner({ status, onDismiss }: DiscordCallbackBannerProps) {
  const t = useTranslations("settings.linkedAccounts.discord.callback");
  const isSuccess = status === "connected";
  const messageKey = isSuccess ? "connected" : status;

  return (
    <div
      role="status"
      className={
        isSuccess
          ? "flex items-center justify-between gap-2 rounded-md bg-green-50 px-3 py-2 text-sm text-green-800"
          : "flex items-center justify-between gap-2 rounded-md bg-red-50 px-3 py-2 text-sm text-red-700"
      }
    >
      <span>{t(messageKey)}</span>
      <button type="button" onClick={onDismiss} className="text-xs underline hover:no-underline">
        {t("dismiss")}
      </button>
    </div>
  );
}
