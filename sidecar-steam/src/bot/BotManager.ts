import crypto from 'crypto';
import { logger as rootLogger } from '../logger.js';
import { activeBotSessions } from '../metrics.js';
import type { WebhookPayload } from '../webhook/WebhookPayloads.js';
import { sendCallback } from '../webhook/WebhookClient.js';
import { BotSession, type BotFailureReason, type BotSessionStatus } from './BotSession.js';
import { loadBotCredentials, type BotCredentials } from './BotConfig.js';

const DEFAULT_BOT_EVENT_ENDPOINT = '/api/v1/sidecar/steam/bot-events';

export interface BotManagerOptions {
  /** Override credential source (used by tests). */
  credentials?: BotCredentials[];
  /** Webhook endpoint to notify backend of bot lifecycle events (T68 consumer). */
  botEventEndpoint?: string;
  /** Override the session constructor (used by tests to inject mocks). */
  sessionFactory?: (
    creds: BotCredentials,
    onFatalFailure: (status: BotSessionStatus, reason: BotFailureReason) => void,
  ) => BotSession;
  /** Override the webhook transport (used by tests). */
  webhookSender?: (
    endpoint: string,
    payload: WebhookPayload,
    correlationId: string,
  ) => Promise<void>;
}

/**
 * Aggregate health snapshot consumed by /health and /api/bots/status.
 */
export interface BotPoolSnapshot {
  healthy: number;
  total: number;
  removed: number;
  bots: BotSessionStatus[];
}

/**
 * Manages the pool of Steam bot sessions (05 §3.2):
 *   - Loads credentials from STEAM_BOTS_CONFIG_PATH or STEAM_BOTS_JSON
 *   - Starts each BotSession in parallel
 *   - Surfaces healthy bots via selectBot() (round-robin in T64;
 *     capacity-based selection deferred to T69)
 *   - Removes terminally-failed bots from the pool and notifies backend
 *     so admins can be alerted (08 §2.7 retry exhaustion path)
 *   - Updates the active_bot_sessions gauge for Prometheus scraping
 */
export class BotManager {
  private readonly sessions = new Map<string, BotSession>();
  private readonly removed = new Map<string, { reason: BotFailureReason; at: string }>();
  private roundRobinCursor = 0;
  private started = false;
  private readonly botEventEndpoint: string;
  private readonly webhookSender: NonNullable<BotManagerOptions['webhookSender']>;
  private readonly sessionFactory: NonNullable<BotManagerOptions['sessionFactory']>;
  private readonly explicitCredentials?: BotCredentials[];
  private readonly log = rootLogger.child({ component: 'BotManager' });

  constructor(options: BotManagerOptions = {}) {
    this.botEventEndpoint = options.botEventEndpoint ?? DEFAULT_BOT_EVENT_ENDPOINT;
    this.webhookSender = options.webhookSender ?? sendCallback;
    this.sessionFactory =
      options.sessionFactory ??
      ((creds, onFatalFailure) =>
        new BotSession(creds, {
          onFatalFailure,
          onStateChanged: (status) => {
            this.log.debug({ status }, 'BotSession state changed');
            this.refreshGauge();
          },
        }));
    this.explicitCredentials = options.credentials;
  }

  async initialize(): Promise<void> {
    if (this.started) {
      this.log.warn('initialize() called twice, ignoring');
      return;
    }
    this.started = true;

    const credentials = this.explicitCredentials ?? loadBotCredentials();
    if (credentials.length === 0) {
      this.log.warn('BotManager started with zero bots (skeleton mode)');
      this.refreshGauge();
      return;
    }

    this.log.info({ count: credentials.length }, 'Starting bot sessions');
    await Promise.allSettled(credentials.map((c) => this.startBot(c)));
    this.refreshGauge();
  }

  async shutdown(): Promise<void> {
    if (!this.started) return;
    this.log.info({ count: this.sessions.size }, 'Shutting down bot sessions');
    await Promise.allSettled([...this.sessions.values()].map((s) => s.stop()));
    this.sessions.clear();
    this.refreshGauge();
  }

  /**
   * Select a ready bot for trade-offer operations.
   *
   * T106a: when `preferredAccountName` is supplied (the backend's
   * capacity-based escrow-bot choice — 06 §3.10 / 05 §3.2) the named bot is
   * returned if it is READY. The delivery + refund legs rely on this so the
   * item is sent from the very bot that holds it. When the hint is absent or
   * the named bot is not READY we fall back to round-robin (T64 behaviour) so
   * a transient health blip does not strand a dispatch.
   */
  selectBot(preferredAccountName?: string): BotSession | null {
    const ready = [...this.sessions.values()].filter((s) => s.isReady());
    if (ready.length === 0) {
      this.log.warn('selectBot called but no bots are READY');
      return null;
    }
    if (preferredAccountName) {
      const preferred = ready.find((s) => s.accountName === preferredAccountName);
      if (preferred) return preferred;
      this.log.warn(
        { preferredAccountName },
        'preferred escrow bot is not READY — falling back to round-robin',
      );
    }
    const index = this.roundRobinCursor % ready.length;
    this.roundRobinCursor = (this.roundRobinCursor + 1) % ready.length;
    return ready[index];
  }

  /** Expose all sessions (READY or otherwise) for health/observability surfaces. */
  allSessions(): BotSession[] {
    return [...this.sessions.values()];
  }

  snapshot(): BotPoolSnapshot {
    const bots = [...this.sessions.values()].map((s) => s.getStatus());
    const healthy = bots.filter((b) => b.state === 'READY').length;
    return {
      healthy,
      total: bots.length,
      removed: this.removed.size,
      bots,
    };
  }

  /** Used by BotHealthCheck after recovery fails permanently. */
  async removeFromPool(accountName: string, reason: BotFailureReason): Promise<void> {
    const session = this.sessions.get(accountName);
    if (!session) return;
    this.sessions.delete(accountName);
    this.removed.set(accountName, { reason, at: new Date().toISOString() });
    this.log.error({ accountName, reason }, 'Bot removed from pool — admin alert pending');
    this.refreshGauge();
    await this.emitBotEvent('bot.removed_from_pool', {
      accountName,
      reason,
      status: session.getStatus(),
    });
  }

  private async startBot(credentials: BotCredentials): Promise<void> {
    const session = this.sessionFactory(credentials, (status, reason) => {
      // Fire-and-forget so the BotSession state machine isn't blocked on webhook latency.
      this.handleFatalFailure(status, reason).catch((err) => {
        this.log.error({ err, accountName: status.accountName }, 'handleFatalFailure threw');
      });
    });
    this.sessions.set(credentials.accountName, session);
    try {
      await session.start();
    } catch (err) {
      this.log.error({ err, accountName: credentials.accountName }, 'BotSession.start() threw');
    }
  }

  private async handleFatalFailure(
    status: BotSessionStatus,
    reason: BotFailureReason,
  ): Promise<void> {
    // Emit a session_failed event first (admin visibility), then drop the bot.
    await this.emitBotEvent('bot.session_failed', { reason, status });
    await this.removeFromPool(status.accountName, reason);
  }

  private async emitBotEvent(event: string, data: Record<string, unknown>): Promise<void> {
    const payload: WebhookPayload = {
      event,
      timestamp: new Date().toISOString(),
      data,
    };
    const correlationId = crypto.randomUUID();
    try {
      await this.webhookSender(this.botEventEndpoint, payload, correlationId);
    } catch (err) {
      // Backend handler is T68 — until then 404 is expected; log and move on.
      this.log.warn(
        { err: (err as Error).message, event, correlationId },
        'Bot event webhook send failed (backend handler is wired in T68)',
      );
    }
  }

  private refreshGauge(): void {
    const healthy = [...this.sessions.values()].filter((s) => s.isReady()).length;
    activeBotSessions.set(healthy);
  }
}
