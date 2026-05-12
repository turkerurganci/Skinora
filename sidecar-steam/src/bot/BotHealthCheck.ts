import { logger as rootLogger } from '../logger.js';
import type { BotManager } from './BotManager.js';
import type { BotSession } from './BotSession.js';

const DEFAULT_INTERVAL_MS = 60_000; // 05 §3.2: "periyodik session kontrolü (60 saniye)"

export interface BotHealthCheckOptions {
  intervalMs?: number;
  /** Defaults to setInterval; overridable for tests (fake timers). */
  scheduler?: (cb: () => void, ms: number) => NodeJS.Timeout;
  /** Defaults to clearInterval; overridable for tests. */
  cancelScheduler?: (handle: NodeJS.Timeout) => void;
}

/**
 * Periodic health probe for the bot pool (T64 acceptance criterion 3).
 *
 * Even though {@link BotSession} reacts to steamcommunity `sessionExpired`
 * events, network drops or library bugs can swallow that signal. This timer
 * is the second defence layer: every 60 seconds it inspects each session's
 * state, triggers `recoverSession()` for unhealthy-but-recoverable sessions,
 * and asks {@link BotManager} to remove sessions that have permanently failed.
 *
 * 08 §2.7 retry table (5s / 15s / 45s) lives inside BotSession; this class
 * only orchestrates *when* to start that loop.
 */
export class BotHealthCheck {
  private timer?: NodeJS.Timeout;
  private inFlight = false;
  private readonly intervalMs: number;
  private readonly scheduler: NonNullable<BotHealthCheckOptions['scheduler']>;
  private readonly cancelScheduler: NonNullable<BotHealthCheckOptions['cancelScheduler']>;
  private readonly log = rootLogger.child({ component: 'BotHealthCheck' });

  constructor(
    private readonly manager: BotManager,
    options: BotHealthCheckOptions = {},
  ) {
    this.intervalMs = options.intervalMs ?? DEFAULT_INTERVAL_MS;
    this.scheduler = options.scheduler ?? ((cb, ms) => setInterval(cb, ms));
    this.cancelScheduler = options.cancelScheduler ?? clearInterval;
  }

  start(): void {
    if (this.timer) return;
    this.log.info({ intervalMs: this.intervalMs }, 'BotHealthCheck started');
    this.timer = this.scheduler(() => {
      this.runTick().catch((err) => {
        this.log.error({ err }, 'Health check tick threw');
      });
    }, this.intervalMs);
    this.timer.unref?.();
  }

  stop(): void {
    if (!this.timer) return;
    this.cancelScheduler(this.timer);
    this.timer = undefined;
    this.log.info('BotHealthCheck stopped');
  }

  /** Public for tests — runs one tick synchronously. */
  async runTick(): Promise<{ healthy: number; total: number; recovered: number; removed: number }> {
    if (this.inFlight) {
      this.log.warn('Health check tick already in flight, skipping');
      return { healthy: 0, total: 0, recovered: 0, removed: 0 };
    }
    this.inFlight = true;
    try {
      const sessions = this.manager.allSessions();
      const results = await Promise.allSettled(sessions.map((s) => this.probe(s)));
      const summary = { healthy: 0, total: sessions.length, recovered: 0, removed: 0 };
      results.forEach((r) => {
        if (r.status === 'fulfilled') {
          if (r.value === 'healthy') summary.healthy++;
          else if (r.value === 'recovered') summary.recovered++;
          else if (r.value === 'removed') summary.removed++;
        }
      });
      this.log.debug({ summary }, 'BotHealthCheck tick complete');
      return summary;
    } finally {
      this.inFlight = false;
    }
  }

  private async probe(
    session: BotSession,
  ): Promise<'healthy' | 'recovered' | 'removed' | 'skipped'> {
    if (session.isReady()) return 'healthy';
    if (session.isTerminal()) {
      // Already declared dead by BotSession itself — sweep out of the pool
      // if BotManager hasn't removed it yet (race-safe via Map.delete idempotency).
      await this.manager.removeFromPool(session.accountName, 'session_recovery_failed');
      return 'removed';
    }

    // Session is in LOGGING_IN / SESSION_EXPIRED / RECONNECTING — try recovery.
    this.log.warn(
      { bot: session.accountName, state: session.getStatus().state },
      'Bot not ready, triggering recovery',
    );
    const recovered = await session.recoverSession();
    if (recovered) return 'recovered';

    await this.manager.removeFromPool(session.accountName, 'session_recovery_failed');
    return 'removed';
  }
}
