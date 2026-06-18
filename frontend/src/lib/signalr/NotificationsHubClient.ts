"use client";

import { HubConnection, HubConnectionState } from "@microsoft/signalr";
import { createHubConnection } from "./connection";
import {
  NotificationHubEvents,
  type AdminBotStatusChangedPayload,
  type AdminHotWalletThresholdBreachedPayload,
  type AdminReconciliationMismatchPayload,
  type DiscordConnectedPayload,
  type MaintenanceStatusChangedPayload,
  type NewNotificationPayload,
  type TelegramConnectedPayload,
  type UnreadCountChangedPayload,
} from "./events";

export interface NotificationHubHandlers {
  onNewNotification?: (p: NewNotificationPayload) => void;
  onUnreadCountChanged?: (p: UnreadCountChangedPayload) => void;
  onTelegramConnected?: (p: TelegramConnectedPayload) => void;
  onDiscordConnected?: (p: DiscordConnectedPayload) => void;
  onMaintenanceStatusChanged?: (p: MaintenanceStatusChangedPayload) => void;
  // Admin-scoped (WP9) — only fire for connections the backend joined to the
  // admin group, so a non-admin session never receives these.
  onAdminBotStatusChanged?: (p: AdminBotStatusChangedPayload) => void;
  onAdminReconciliationMismatch?: (p: AdminReconciliationMismatchPayload) => void;
  onAdminHotWalletThresholdBreached?: (p: AdminHotWalletThresholdBreachedPayload) => void;
}

type TokenFactory = () => string | null | Promise<string | null>;

/**
 * Singleton client wrapping the `/hubs/notifications` SignalR connection
 * (T96 — 07 §11.2 RT2). Unlike the transactions hub (per-id rooms), the
 * notifications hub auto-joins the caller to a single per-user group on
 * connect — no client→server methods to invoke.
 */
class NotificationsHubClient {
  private connection: HubConnection | null = null;
  private startPromise: Promise<void> | null = null;
  private handlers = new Set<NotificationHubHandlers>();

  isConnected(): boolean {
    return this.connection?.state === HubConnectionState.Connected;
  }

  async start(tokenFactory: TokenFactory): Promise<void> {
    if (this.startPromise) return this.startPromise;
    if (this.isConnected()) return;

    this.connection = createHubConnection("notifications", tokenFactory);
    this.attachEventListeners(this.connection);
    this.attachLifecycleListeners(this.connection);

    this.startPromise = this.connection
      .start()
      .catch((err) => {
        this.connection = null;
        throw err;
      })
      .finally(() => {
        this.startPromise = null;
      });

    return this.startPromise;
  }

  async stop(): Promise<void> {
    const conn = this.connection;
    if (!conn) return;
    this.connection = null;
    try {
      await conn.stop();
    } catch {
      /* swallow */
    }
  }

  subscribe(handlers: NotificationHubHandlers): () => void {
    this.handlers.add(handlers);
    return () => {
      this.handlers.delete(handlers);
    };
  }

  private attachEventListeners(conn: HubConnection): void {
    conn.on(NotificationHubEvents.NewNotification, (p: NewNotificationPayload) =>
      this.dispatch((h) => h.onNewNotification?.(p)),
    );
    conn.on(NotificationHubEvents.UnreadCountChanged, (p: UnreadCountChangedPayload) =>
      this.dispatch((h) => h.onUnreadCountChanged?.(p)),
    );
    conn.on(NotificationHubEvents.TelegramConnected, (p: TelegramConnectedPayload) =>
      this.dispatch((h) => h.onTelegramConnected?.(p)),
    );
    conn.on(NotificationHubEvents.DiscordConnected, (p: DiscordConnectedPayload) =>
      this.dispatch((h) => h.onDiscordConnected?.(p)),
    );
    conn.on(NotificationHubEvents.MaintenanceStatusChanged, (p: MaintenanceStatusChangedPayload) =>
      this.dispatch((h) => h.onMaintenanceStatusChanged?.(p)),
    );
    conn.on(NotificationHubEvents.AdminBotStatusChanged, (p: AdminBotStatusChangedPayload) =>
      this.dispatch((h) => h.onAdminBotStatusChanged?.(p)),
    );
    conn.on(
      NotificationHubEvents.AdminReconciliationMismatch,
      (p: AdminReconciliationMismatchPayload) =>
        this.dispatch((h) => h.onAdminReconciliationMismatch?.(p)),
    );
    conn.on(
      NotificationHubEvents.AdminHotWalletThresholdBreached,
      (p: AdminHotWalletThresholdBreachedPayload) =>
        this.dispatch((h) => h.onAdminHotWalletThresholdBreached?.(p)),
    );
  }

  private attachLifecycleListeners(conn: HubConnection): void {
    conn.onclose(() => {
      if (process.env.NODE_ENV === "development") {
        console.warn("SignalR /hubs/notifications closed");
      }
    });
  }

  private dispatch(fn: (h: NotificationHubHandlers) => void): void {
    for (const h of this.handlers) fn(h);
  }
}

export const notificationsHubClient = new NotificationsHubClient();
