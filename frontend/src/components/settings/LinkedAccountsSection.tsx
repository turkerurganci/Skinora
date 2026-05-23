"use client";

import { useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { useTranslations } from "next-intl";
import { ApiError } from "@/lib/api/client";
import {
  AccountSettings,
  TelegramConnectResponse,
  connectDiscord,
  connectTelegram,
  disconnectDiscord,
  disconnectTelegram,
} from "@/lib/api/settings";
import { TelegramConnectModal } from "./TelegramConnectModal";

export interface LinkedAccountsSectionProps {
  settings: AccountSettings;
}

interface AccountRowProps {
  label: string;
  username: string | null;
  connected: boolean;
  connecting: boolean;
  disconnecting: boolean;
  connectLabel: string;
  disconnectLabel: string;
  connectingLabel: string;
  disconnectingLabel: string;
  connectedBadge: string;
  notConnectedBadge: string;
  onConnect: () => void;
  onDisconnect: () => void;
}

function AccountRow({
  label,
  username,
  connected,
  connecting,
  disconnecting,
  connectLabel,
  disconnectLabel,
  connectingLabel,
  disconnectingLabel,
  connectedBadge,
  notConnectedBadge,
  onConnect,
  onDisconnect,
}: AccountRowProps) {
  return (
    <div className="flex items-center justify-between gap-4 py-3">
      <div className="flex-1">
        <div className="font-medium text-gray-900">{label}</div>
        <div className="text-sm">
          {connected ? (
            <span className="text-green-700">
              {connectedBadge}
              {username && <span className="ml-2 text-gray-600">{username}</span>}
            </span>
          ) : (
            <span className="text-gray-500">{notConnectedBadge}</span>
          )}
        </div>
      </div>
      {connected ? (
        <button
          type="button"
          onClick={onDisconnect}
          disabled={disconnecting}
          className="rounded-md border border-red-300 bg-white px-3 py-2 text-sm font-medium text-red-700 hover:bg-red-50 disabled:opacity-50"
        >
          {disconnecting ? disconnectingLabel : disconnectLabel}
        </button>
      ) : (
        <button
          type="button"
          onClick={onConnect}
          disabled={connecting}
          className="rounded-md bg-blue-600 px-3 py-2 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-50"
        >
          {connecting ? connectingLabel : connectLabel}
        </button>
      )}
    </div>
  );
}

/**
 * 04 §7.6 Bağlı Hesaplar. Telegram bağlama: modal içinde kod + bot link.
 * Discord bağlama: `window.location.assign(discordAuthUrl)` ile redirect;
 * dönüş `/settings?discord=connected|error&reason=...` page-level handler'da
 * okunur (T93 re-auth pattern'i ile birebir tutarlı).
 */
export function LinkedAccountsSection({ settings }: LinkedAccountsSectionProps) {
  const t = useTranslations("settings.linkedAccounts");
  const queryClient = useQueryClient();

  const [telegramConnecting, setTelegramConnecting] = useState(false);
  const [telegramDisconnecting, setTelegramDisconnecting] = useState(false);
  const [discordConnecting, setDiscordConnecting] = useState(false);
  const [discordDisconnecting, setDiscordDisconnecting] = useState(false);

  const [telegramModalOpen, setTelegramModalOpen] = useState(false);
  const [telegramPayload, setTelegramPayload] = useState<TelegramConnectResponse | null>(null);

  const [error, setError] = useState<string | null>(null);

  function invalidate() {
    return queryClient.invalidateQueries({ queryKey: ["users", "me", "settings"] });
  }

  async function handleTelegramConnect() {
    setTelegramConnecting(true);
    setError(null);
    try {
      const payload = await connectTelegram();
      setTelegramPayload(payload);
      setTelegramModalOpen(true);
    } catch {
      setError(t("errors.telegramConnect"));
    } finally {
      setTelegramConnecting(false);
    }
  }

  async function handleTelegramDisconnect() {
    setTelegramDisconnecting(true);
    setError(null);
    try {
      await disconnectTelegram();
      await invalidate();
    } catch {
      setError(t("errors.telegramDisconnect"));
    } finally {
      setTelegramDisconnecting(false);
    }
  }

  async function handleDiscordConnect() {
    setDiscordConnecting(true);
    setError(null);
    try {
      const result = await connectDiscord();
      if (typeof window !== "undefined") {
        window.location.assign(result.discordAuthUrl);
      }
    } catch (err) {
      setError(err instanceof ApiError ? t("errors.discordConnect") : t("errors.discordConnect"));
      setDiscordConnecting(false);
    }
  }

  async function handleDiscordDisconnect() {
    setDiscordDisconnecting(true);
    setError(null);
    try {
      await disconnectDiscord();
      await invalidate();
    } catch {
      setError(t("errors.discordDisconnect"));
    } finally {
      setDiscordDisconnecting(false);
    }
  }

  function handleTelegramModalClose() {
    setTelegramModalOpen(false);
    void invalidate();
  }

  return (
    <section className="rounded-lg border border-gray-200 bg-white p-6">
      <h2 className="mb-4 text-lg font-semibold text-gray-900">{t("title")}</h2>

      <div className="divide-y divide-gray-100">
        <AccountRow
          label={t("telegram.label")}
          username={settings.notifications.telegram.username}
          connected={settings.notifications.telegram.connected}
          connecting={telegramConnecting}
          disconnecting={telegramDisconnecting}
          connectLabel={t("connect")}
          disconnectLabel={t("disconnect")}
          connectingLabel={t("connecting")}
          disconnectingLabel={t("disconnecting")}
          connectedBadge={t("status.connected")}
          notConnectedBadge={t("status.notConnected")}
          onConnect={handleTelegramConnect}
          onDisconnect={handleTelegramDisconnect}
        />
        <AccountRow
          label={t("discord.label")}
          username={settings.notifications.discord.username}
          connected={settings.notifications.discord.connected}
          connecting={discordConnecting}
          disconnecting={discordDisconnecting}
          connectLabel={t("connect")}
          disconnectLabel={t("disconnect")}
          connectingLabel={t("connecting")}
          disconnectingLabel={t("disconnecting")}
          connectedBadge={t("status.connected")}
          notConnectedBadge={t("status.notConnected")}
          onConnect={handleDiscordConnect}
          onDisconnect={handleDiscordDisconnect}
        />
      </div>

      {error && <p className="mt-3 rounded-md bg-red-50 px-3 py-2 text-sm text-red-700">{error}</p>}

      <TelegramConnectModal
        open={telegramModalOpen}
        payload={telegramPayload}
        onClose={handleTelegramModalClose}
      />
    </section>
  );
}
