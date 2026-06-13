import { describe, it, expect, vi, beforeEach } from 'vitest';

vi.mock('../logger.js', () => ({
  logger: {
    info: vi.fn(),
    warn: vi.fn(),
    error: vi.fn(),
    debug: vi.fn(),
    child: vi.fn().mockReturnThis(),
  },
}));

vi.mock('../webhook/WebhookClient.js', () => ({
  sendCallback: vi.fn(),
}));

vi.mock('../metrics.js', () => ({
  activeBotSessions: { set: vi.fn() },
  // Other metric exports are unused by BotManager — provide empty stubs.
  httpRequestDuration: {},
  httpRequestsTotal: {},
  steamApiRequestDuration: {},
  steamApiErrorsTotal: {},
  tradeOffersTotal: {},
  metricsHandler: vi.fn(),
}));

import { BotManager } from './BotManager.js';
import type { BotSession, BotSessionStatus, BotFailureReason } from './BotSession.js';
import type { BotCredentials } from './BotConfig.js';

function makeFakeSession(
  accountName: string,
  initialReady: boolean,
): BotSession & {
  triggerFatal: (reason: BotFailureReason) => void;
  setReady: (v: boolean) => void;
} {
  let ready = initialReady;
  let onFatal: ((status: BotSessionStatus, reason: BotFailureReason) => void) | undefined;
  const stub = {
    accountName,
    config: { accountName, password: '', sharedSecret: '', identitySecret: '' } as BotCredentials,
    start: vi.fn().mockResolvedValue(undefined),
    stop: vi.fn().mockResolvedValue(undefined),
    isReady: () => ready,
    isHealthy: () => ready,
    isTerminal: () => false,
    recoverSession: vi.fn().mockResolvedValue(true),
    getStatus: (): BotSessionStatus => ({
      accountName,
      state: ready ? 'READY' : 'INITIALIZING',
      lastTransitionAt: new Date().toISOString(),
      retryCount: 0,
    }),
    triggerFatal(reason: BotFailureReason) {
      onFatal?.(
        {
          accountName,
          state: 'FAILED',
          lastTransitionAt: new Date().toISOString(),
          retryCount: 3,
        },
        reason,
      );
    },
    setReady(v: boolean) {
      ready = v;
    },
  } as unknown as BotSession & {
    triggerFatal: (reason: BotFailureReason) => void;
    setReady: (v: boolean) => void;
  };
  return Object.assign(stub, { __setOnFatal: (cb: typeof onFatal) => (onFatal = cb) }) as never;
}

const cred = (name: string): BotCredentials => ({
  accountName: name,
  password: 'p',
  sharedSecret: 's',
  identitySecret: 'i',
});

beforeEach(() => {
  vi.clearAllMocks();
});

describe('BotManager', () => {
  it('initialize() with zero credentials does not throw and reports empty pool', async () => {
    const webhookSender = vi.fn().mockResolvedValue(undefined);
    const manager = new BotManager({ credentials: [], webhookSender });
    await manager.initialize();
    const snap = manager.snapshot();
    expect(snap.total).toBe(0);
    expect(snap.healthy).toBe(0);
    expect(webhookSender).not.toHaveBeenCalled();
  });

  it('initialize() starts each session in parallel', async () => {
    const sessions: ReturnType<typeof makeFakeSession>[] = [];
    const factory = vi.fn((c: BotCredentials, onFatal) => {
      const s = makeFakeSession(c.accountName, true);
      (s as unknown as { __setOnFatal: (cb: typeof onFatal) => void }).__setOnFatal(onFatal);
      sessions.push(s);
      return s;
    });
    const manager = new BotManager({
      credentials: [cred('a'), cred('b'), cred('c')],
      sessionFactory: factory,
      webhookSender: vi.fn().mockResolvedValue(undefined),
    });
    await manager.initialize();
    expect(factory).toHaveBeenCalledTimes(3);
    sessions.forEach((s) => expect(s.start).toHaveBeenCalled());
    expect(manager.snapshot().total).toBe(3);
    expect(manager.snapshot().healthy).toBe(3);
  });

  it('initialize() is idempotent', async () => {
    const factory = vi.fn((c: BotCredentials) => makeFakeSession(c.accountName, true));
    const manager = new BotManager({
      credentials: [cred('a')],
      sessionFactory: factory,
      webhookSender: vi.fn().mockResolvedValue(undefined),
    });
    await manager.initialize();
    await manager.initialize();
    expect(factory).toHaveBeenCalledTimes(1);
  });

  it('selectBot returns null when no bots are READY', async () => {
    const factory = vi.fn((c: BotCredentials) => makeFakeSession(c.accountName, false));
    const manager = new BotManager({
      credentials: [cred('a'), cred('b')],
      sessionFactory: factory,
      webhookSender: vi.fn().mockResolvedValue(undefined),
    });
    await manager.initialize();
    expect(manager.selectBot()).toBeNull();
  });

  it('selectBot round-robins across READY sessions', async () => {
    const factory = vi.fn((c: BotCredentials) => makeFakeSession(c.accountName, true));
    const manager = new BotManager({
      credentials: [cred('a'), cred('b'), cred('c')],
      sessionFactory: factory,
      webhookSender: vi.fn().mockResolvedValue(undefined),
    });
    await manager.initialize();
    const picks = [
      manager.selectBot()!.accountName,
      manager.selectBot()!.accountName,
      manager.selectBot()!.accountName,
      manager.selectBot()!.accountName,
    ];
    // Round-robin: a, b, c, a (or some rotation thereof). The exact order depends
    // on Map iteration order; what matters is uniqueness within the first 3 picks.
    expect(new Set(picks.slice(0, 3)).size).toBe(3);
    expect(picks[3]).toBe(picks[0]);
  });

  it('selectBot skips non-READY sessions', async () => {
    const ready = makeFakeSession('ready', true);
    const notReady = makeFakeSession('notReady', false);
    const factory = vi.fn((c: BotCredentials) => (c.accountName === 'ready' ? ready : notReady));
    const manager = new BotManager({
      credentials: [cred('ready'), cred('notReady')],
      sessionFactory: factory,
      webhookSender: vi.fn().mockResolvedValue(undefined),
    });
    await manager.initialize();
    expect(manager.selectBot()?.accountName).toBe('ready');
    expect(manager.selectBot()?.accountName).toBe('ready');
  });

  it('selectBot honours a READY preferred bot hint (T106a)', async () => {
    const factory = vi.fn((c: BotCredentials) => makeFakeSession(c.accountName, true));
    const manager = new BotManager({
      credentials: [cred('a'), cred('b'), cred('c')],
      sessionFactory: factory,
      webhookSender: vi.fn().mockResolvedValue(undefined),
    });
    await manager.initialize();
    // Repeated picks always return the hinted bot regardless of round-robin cursor.
    expect(manager.selectBot('b')?.accountName).toBe('b');
    expect(manager.selectBot('b')?.accountName).toBe('b');
    expect(manager.selectBot('c')?.accountName).toBe('c');
  });

  it('selectBot falls back to round-robin when the hinted bot is not READY (T106a)', async () => {
    const hinted = makeFakeSession('hinted', false);
    const other = makeFakeSession('other', true);
    const factory = vi.fn((c: BotCredentials) => (c.accountName === 'hinted' ? hinted : other));
    const manager = new BotManager({
      credentials: [cred('hinted'), cred('other')],
      sessionFactory: factory,
      webhookSender: vi.fn().mockResolvedValue(undefined),
    });
    await manager.initialize();
    expect(manager.selectBot('hinted')?.accountName).toBe('other');
  });

  it('onFatalFailure callback removes the bot and emits webhook events', async () => {
    const sessions: ReturnType<typeof makeFakeSession>[] = [];
    const factory = vi.fn((c: BotCredentials, onFatal) => {
      const s = makeFakeSession(c.accountName, true);
      (s as unknown as { __setOnFatal: (cb: typeof onFatal) => void }).__setOnFatal(onFatal);
      sessions.push(s);
      return s;
    });
    const webhookSender = vi.fn().mockResolvedValue(undefined);
    const manager = new BotManager({
      credentials: [cred('victim')],
      sessionFactory: factory,
      webhookSender,
      botEventEndpoint: '/x/y',
    });
    await manager.initialize();

    sessions[0].triggerFatal('login_failed');
    // Allow the queued microtasks to run
    await new Promise((r) => setImmediate(r));

    const events = webhookSender.mock.calls.map((c) => (c[1] as { event: string }).event);
    expect(events).toContain('bot.session_failed');
    expect(events).toContain('bot.removed_from_pool');
    expect(manager.snapshot().total).toBe(0);
    expect(manager.snapshot().removed).toBe(1);
  });

  it('webhook send failure does not crash the manager', async () => {
    const sessions: ReturnType<typeof makeFakeSession>[] = [];
    const factory = vi.fn((c: BotCredentials, onFatal) => {
      const s = makeFakeSession(c.accountName, true);
      (s as unknown as { __setOnFatal: (cb: typeof onFatal) => void }).__setOnFatal(onFatal);
      sessions.push(s);
      return s;
    });
    const webhookSender = vi.fn().mockRejectedValue(new Error('404 — backend handler is T68'));
    const manager = new BotManager({
      credentials: [cred('victim')],
      sessionFactory: factory,
      webhookSender,
    });
    await manager.initialize();

    sessions[0].triggerFatal('banned');
    await new Promise((r) => setImmediate(r));

    expect(manager.snapshot().total).toBe(0);
    expect(manager.snapshot().removed).toBe(1);
  });

  it('shutdown stops every session', async () => {
    const sessions: ReturnType<typeof makeFakeSession>[] = [];
    const factory = vi.fn((c: BotCredentials) => {
      const s = makeFakeSession(c.accountName, true);
      sessions.push(s);
      return s;
    });
    const manager = new BotManager({
      credentials: [cred('a'), cred('b')],
      sessionFactory: factory,
      webhookSender: vi.fn().mockResolvedValue(undefined),
    });
    await manager.initialize();
    await manager.shutdown();
    sessions.forEach((s) => expect(s.stop).toHaveBeenCalled());
    expect(manager.snapshot().total).toBe(0);
  });

  it('removeFromPool emits bot.removed_from_pool exactly once per bot', async () => {
    const factory = vi.fn((c: BotCredentials) => makeFakeSession(c.accountName, true));
    const webhookSender = vi.fn().mockResolvedValue(undefined);
    const manager = new BotManager({
      credentials: [cred('victim')],
      sessionFactory: factory,
      webhookSender,
    });
    await manager.initialize();
    await manager.removeFromPool('victim', 'session_recovery_failed');
    await manager.removeFromPool('victim', 'session_recovery_failed'); // second call: idempotent
    const removedEvents = webhookSender.mock.calls.filter(
      (c) => (c[1] as { event: string }).event === 'bot.removed_from_pool',
    );
    expect(removedEvents).toHaveLength(1);
  });
});
