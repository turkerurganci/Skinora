"use client";

import { HubConnection, HubConnectionState } from "@microsoft/signalr";
import { createHubConnection } from "./connection";
import {
  TransactionHubEvents,
  TransactionHubMethods,
  type CountdownSyncPayload,
  type DisputeUpdatePayload,
  type EmergencyHoldAppliedPayload,
  type EmergencyHoldReleasedPayload,
  type FlagResolvedPayload,
  type PaymentConfirmedPayload,
  type PaymentDetectedPayload,
  type TransactionStatusChangedPayload,
} from "./events";

export interface TransactionHubHandlers {
  onTransactionStatusChanged?: (p: TransactionStatusChangedPayload) => void;
  onCountdownSync?: (p: CountdownSyncPayload) => void;
  onPaymentDetected?: (p: PaymentDetectedPayload) => void;
  onPaymentConfirmed?: (p: PaymentConfirmedPayload) => void;
  onDisputeUpdate?: (p: DisputeUpdatePayload) => void;
  onFlagResolved?: (p: FlagResolvedPayload) => void;
  onEmergencyHoldApplied?: (p: EmergencyHoldAppliedPayload) => void;
  onEmergencyHoldReleased?: (p: EmergencyHoldReleasedPayload) => void;
}

type TokenFactory = () => string | null | Promise<string | null>;

/**
 * Singleton client wrapping the `/hubs/transactions` SignalR connection
 * (T96 — 07 §11.1 RT1). A single connection is shared between every detail
 * page subscriber and the optional global handlers (cache invalidation).
 *
 * Subscribers register through {@link subscribe}, which is fire-and-forget
 * with respect to connection state: subscriber handlers are stored in the
 * registry immediately, and re-joins are issued on every (re)connect for the
 * full set of currently-subscribed transaction ids. This survives the
 * automatic reconnect window (0/2/5/10/30s — `connection.ts`) and re-joins
 * once the new connection settles.
 *
 * Per-event handlers fan out to every subscriber of the affected
 * `transactionId`. Subscriptions are scoped to a transaction id, not to a
 * single React component — when a second consumer (e.g. a sibling panel)
 * subscribes to the same id we reuse the existing group membership.
 */
class TransactionsHubClient {
  private connection: HubConnection | null = null;
  private tokenFactory: TokenFactory | null = null;
  private startPromise: Promise<void> | null = null;
  private starting = false;
  private subscriptions = new Map<string, Set<TransactionHubHandlers>>();
  private globalHandlers = new Set<TransactionHubHandlers>();

  isConnected(): boolean {
    return this.connection?.state === HubConnectionState.Connected;
  }

  /**
   * Idempotent: calling more than once with the same token factory reuses the
   * underlying connection. Call {@link stop} first if the auth identity has
   * changed (logout → login as another user).
   */
  async start(tokenFactory: TokenFactory): Promise<void> {
    if (this.startPromise) return this.startPromise;
    if (this.isConnected()) return;

    this.tokenFactory = tokenFactory;
    this.connection = createHubConnection("transactions", tokenFactory);
    this.attachEventListeners(this.connection);
    this.attachLifecycleListeners(this.connection);

    this.starting = true;
    this.startPromise = this.connection
      .start()
      .then(async () => {
        await this.rejoinAll();
      })
      .catch((err) => {
        // Surface the failure to the caller but keep the connection wrapper —
        // SignalR's withAutomaticReconnect only fires after a successful start,
        // so a first-start failure means we need to drop the wrapper to allow
        // a clean retry.
        this.connection = null;
        throw err;
      })
      .finally(() => {
        this.starting = false;
        this.startPromise = null;
      });

    return this.startPromise;
  }

  /**
   * Tear down the connection. Subscriptions are kept so a subsequent
   * {@link start} call re-joins the same transaction rooms.
   */
  async stop(): Promise<void> {
    const conn = this.connection;
    if (!conn) return;
    this.connection = null;
    this.tokenFactory = null;
    try {
      await conn.stop();
    } catch {
      /* swallow — stop should not throw on shutdown */
    }
  }

  /**
   * Subscribe to events for a single transaction id. The returned dispose
   * function removes the handler set and leaves the SignalR group if no
   * other consumer is still subscribed.
   */
  subscribe(transactionId: string, handlers: TransactionHubHandlers): () => void {
    let set = this.subscriptions.get(transactionId);
    if (!set) {
      set = new Set();
      this.subscriptions.set(transactionId, set);
      // First subscriber for this id — join on the wire if we're connected.
      // If not, rejoinAll() picks it up after start() completes.
      if (this.isConnected()) {
        void this.joinTransaction(transactionId);
      }
    }
    set.add(handlers);

    return () => {
      const current = this.subscriptions.get(transactionId);
      if (!current) return;
      current.delete(handlers);
      if (current.size === 0) {
        this.subscriptions.delete(transactionId);
        if (this.isConnected()) {
          void this.leaveTransaction(transactionId);
        }
      }
    };
  }

  /**
   * Register a handler that receives every event regardless of transaction
   * id (used for global cache invalidation in RealtimeProvider). Returns a
   * dispose function.
   */
  subscribeGlobal(handlers: TransactionHubHandlers): () => void {
    this.globalHandlers.add(handlers);
    return () => {
      this.globalHandlers.delete(handlers);
    };
  }

  private async joinTransaction(transactionId: string): Promise<void> {
    if (!this.connection) return;
    try {
      await this.connection.invoke(TransactionHubMethods.JoinTransaction, transactionId);
    } catch (err) {
      // TRANSACTION_NOT_FOUND / TRANSACTION_FORBIDDEN / AUTH_INVALID land here;
      // the detail page itself will surface 403/404 via the REST query. We log
      // and move on — a stale subscription does no harm without a group.
      if (process.env.NODE_ENV === "development") {
        console.warn(`SignalR JoinTransaction(${transactionId}) failed`, err);
      }
    }
  }

  private async leaveTransaction(transactionId: string): Promise<void> {
    if (!this.connection) return;
    try {
      await this.connection.invoke(TransactionHubMethods.LeaveTransaction, transactionId);
    } catch {
      /* swallow — leave is best-effort */
    }
  }

  private async rejoinAll(): Promise<void> {
    if (!this.connection) return;
    const ids = Array.from(this.subscriptions.keys());
    await Promise.all(ids.map((id) => this.joinTransaction(id)));
  }

  private attachEventListeners(conn: HubConnection): void {
    conn.on(TransactionHubEvents.TransactionStatusChanged, (p: TransactionStatusChangedPayload) =>
      this.dispatch(p.transactionId, (h) => h.onTransactionStatusChanged?.(p)),
    );
    conn.on(TransactionHubEvents.CountdownSync, (p: CountdownSyncPayload) =>
      this.dispatch(p.transactionId, (h) => h.onCountdownSync?.(p)),
    );
    conn.on(TransactionHubEvents.PaymentDetected, (p: PaymentDetectedPayload) =>
      this.dispatch(p.transactionId, (h) => h.onPaymentDetected?.(p)),
    );
    conn.on(TransactionHubEvents.PaymentConfirmed, (p: PaymentConfirmedPayload) =>
      this.dispatch(p.transactionId, (h) => h.onPaymentConfirmed?.(p)),
    );
    conn.on(TransactionHubEvents.DisputeUpdate, (p: DisputeUpdatePayload) =>
      this.dispatch(p.transactionId, (h) => h.onDisputeUpdate?.(p)),
    );
    conn.on(TransactionHubEvents.FlagResolved, (p: FlagResolvedPayload) =>
      this.dispatch(p.transactionId, (h) => h.onFlagResolved?.(p)),
    );
    conn.on(TransactionHubEvents.EmergencyHoldApplied, (p: EmergencyHoldAppliedPayload) =>
      this.dispatch(p.transactionId, (h) => h.onEmergencyHoldApplied?.(p)),
    );
    conn.on(TransactionHubEvents.EmergencyHoldReleased, (p: EmergencyHoldReleasedPayload) =>
      this.dispatch(p.transactionId, (h) => h.onEmergencyHoldReleased?.(p)),
    );
  }

  private attachLifecycleListeners(conn: HubConnection): void {
    conn.onreconnected(() => {
      // After a transient drop SignalR re-establishes the connection but the
      // server-side group membership is gone; replay every subscription.
      void this.rejoinAll();
    });
    conn.onclose(() => {
      if (!this.starting && process.env.NODE_ENV === "development") {
        console.warn("SignalR /hubs/transactions closed");
      }
    });
  }

  private dispatch(transactionId: string, fn: (h: TransactionHubHandlers) => void): void {
    const local = this.subscriptions.get(transactionId);
    if (local) {
      for (const h of local) fn(h);
    }
    for (const h of this.globalHandlers) fn(h);
  }
}

export const transactionsHubClient = new TransactionsHubClient();
