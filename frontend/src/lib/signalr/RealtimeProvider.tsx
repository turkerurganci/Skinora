"use client";

import { useEffect, type ReactNode } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { useAuthStore } from "@/lib/stores/auth-store";
import { useToast } from "@/components/common";
import { notificationsHubClient } from "./NotificationsHubClient";
import { transactionsHubClient } from "./TransactionsHubClient";
import type { TransactionDetailResponse } from "@/lib/api/transactions";
import type { UnreadCountResponse } from "@/lib/api/notifications";

/**
 * Mounts the singleton SignalR hub clients (T96 — 07 §11.1 RT1, §11.2 RT2)
 * and wires global React Query cache invalidations for every event listed in
 * the spec tables. Per-transaction subscribers (S07 detail page) layer their
 * own handlers on top of these globals via {@link useTransactionRealtime}.
 *
 * Lifecycle is gated by the auth store: hubs start when the session is
 * authenticated AND an access token is present; they stop on logout or token
 * removal. Token rotation (T32 refresh flow) is handled transparently by the
 * SignalR access-token factory — the factory reads `accessToken` from the
 * auth store on every (re)connect attempt, so a rotated token is picked up
 * by the next reconnect without recreating the connection wrapper.
 *
 * Cache invalidation strategy:
 *   • `TransactionStatusChanged` → invalidate detail + the user's lists
 *   • `CountdownSync` → patch the detail cache in place (no refetch — the
 *     payload contains the new `remainingSeconds` + frozen flag verbatim).
 *   • `PaymentDetected/PaymentConfirmed/DisputeUpdate/FlagResolved/
 *     EmergencyHoldApplied/EmergencyHoldReleased` → invalidate detail.
 *   • `NewNotification` → invalidate inbox lists + unread count AND surface a
 *     C09 toast (WP9) with the notification text.
 *   • `UnreadCountChanged` → patch unread-count cache directly.
 *   • `TelegramConnected/DiscordConnected` → invalidate account settings.
 *   • `MaintenanceStatusChanged` → invalidate platform maintenance cache.
 *   • Admin-scoped (WP9, only delivered to admin connections):
 *     `AdminReconciliationMismatch` / `AdminHotWalletThresholdBreached` →
 *     invalidate the audit-log + dashboard caches (where they surface durably).
 */
export function RealtimeProvider({ children }: { children: ReactNode }) {
  const isAuthenticated = useAuthStore((s) => s.isAuthenticated);
  const accessToken = useAuthStore((s) => s.accessToken);
  const queryClient = useQueryClient();
  const { push } = useToast();

  useEffect(() => {
    if (!isAuthenticated || !accessToken) {
      void transactionsHubClient.stop();
      void notificationsHubClient.stop();
      return;
    }

    // Read from the store on each call so rotated tokens (T32 refresh) are
    // picked up by the next (re)connect without tearing down the client.
    const tokenFactory = () => useAuthStore.getState().accessToken;

    void transactionsHubClient.start(tokenFactory).catch((err) => {
      if (process.env.NODE_ENV === "development") {
        console.warn("Failed to start /hubs/transactions", err);
      }
    });
    void notificationsHubClient.start(tokenFactory).catch((err) => {
      if (process.env.NODE_ENV === "development") {
        console.warn("Failed to start /hubs/notifications", err);
      }
    });

    const unsubTx = transactionsHubClient.subscribeGlobal({
      onTransactionStatusChanged: (p) => {
        queryClient.invalidateQueries({ queryKey: ["transactions", "detail", p.transactionId] });
        queryClient.invalidateQueries({ queryKey: ["transactions", "active"] });
        queryClient.invalidateQueries({ queryKey: ["transactions", "completed"] });
        queryClient.invalidateQueries({ queryKey: ["transactions", "cancelled"] });
      },
      onCountdownSync: (p) => {
        queryClient.setQueryData<TransactionDetailResponse | undefined>(
          ["transactions", "detail", p.transactionId],
          (old) => {
            if (!old || !old.timeout) return old;
            return {
              ...old,
              timeout: {
                ...old.timeout,
                remainingSeconds: p.remainingSeconds,
                frozen: p.frozen,
                frozenReason: p.frozenReason ?? null,
              },
            };
          },
        );
      },
      onPaymentDetected: (p) => {
        queryClient.invalidateQueries({ queryKey: ["transactions", "detail", p.transactionId] });
      },
      onPaymentConfirmed: (p) => {
        queryClient.invalidateQueries({ queryKey: ["transactions", "detail", p.transactionId] });
      },
      onDisputeUpdate: (p) => {
        queryClient.invalidateQueries({ queryKey: ["transactions", "detail", p.transactionId] });
      },
      onFlagResolved: (p) => {
        queryClient.invalidateQueries({ queryKey: ["transactions", "detail", p.transactionId] });
      },
      onEmergencyHoldApplied: (p) => {
        queryClient.invalidateQueries({ queryKey: ["transactions", "detail", p.transactionId] });
      },
      onEmergencyHoldReleased: (p) => {
        queryClient.invalidateQueries({ queryKey: ["transactions", "detail", p.transactionId] });
      },
    });

    const unsubNot = notificationsHubClient.subscribe({
      onNewNotification: (p) => {
        queryClient.invalidateQueries({ queryKey: ["notifications", "list"] });
        queryClient.invalidateQueries({ queryKey: ["notifications", "unread-count"] });
        // WP9 — C09 toast: the message is server-rendered, so surface it verbatim.
        push({ variant: "info", message: p.message });
      },
      onUnreadCountChanged: (p) => {
        queryClient.setQueryData<UnreadCountResponse>(["notifications", "unread-count"], {
          unreadCount: p.unreadCount,
        });
      },
      onTelegramConnected: () => {
        queryClient.invalidateQueries({ queryKey: ["users", "me", "settings"] });
      },
      onDiscordConnected: () => {
        queryClient.invalidateQueries({ queryKey: ["users", "me", "settings"] });
      },
      onMaintenanceStatusChanged: () => {
        queryClient.invalidateQueries({ queryKey: ["platform", "maintenance"] });
      },
      // WP9 — admin-scoped events. The backend only delivers these to admin
      // connections (admin group), so the handlers are inert for regular users.
      onAdminReconciliationMismatch: () => {
        queryClient.invalidateQueries({ queryKey: ["admin", "audit-logs"] });
        queryClient.invalidateQueries({ queryKey: ["admin", "dashboard"] });
      },
      onAdminHotWalletThresholdBreached: () => {
        queryClient.invalidateQueries({ queryKey: ["admin", "audit-logs"] });
        queryClient.invalidateQueries({ queryKey: ["admin", "dashboard"] });
      },
    });

    return () => {
      unsubTx();
      unsubNot();
    };
  }, [isAuthenticated, accessToken, queryClient, push]);

  return <>{children}</>;
}
