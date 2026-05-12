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

vi.mock('../metrics.js', () => ({
  activeBotSessions: { set: vi.fn() },
  httpRequestDuration: {},
  httpRequestsTotal: {},
  steamApiRequestDuration: {},
  steamApiErrorsTotal: {},
  tradeOffersTotal: {},
  metricsHandler: vi.fn(),
}));

import { BotHealthCheck } from './BotHealthCheck.js';
import type { BotManager } from './BotManager.js';
import type { BotSession, BotSessionStatus } from './BotSession.js';

interface FakeSession {
  accountName: string;
  state: BotSessionStatus['state'];
  recoverSession: ReturnType<typeof vi.fn>;
}

function makeManager(
  sessions: FakeSession[],
): BotManager & { removeFromPool: ReturnType<typeof vi.fn> } {
  const map = new Map(sessions.map((s) => [s.accountName, s] as const));
  const removeFromPool = vi.fn(async (name: string) => {
    map.delete(name);
  });
  return {
    allSessions: () =>
      [...map.values()].map(
        (s) =>
          ({
            accountName: s.accountName,
            isReady: () => s.state === 'READY',
            isTerminal: () => s.state === 'FAILED' || s.state === 'BANNED' || s.state === 'STOPPED',
            recoverSession: s.recoverSession,
            getStatus: (): BotSessionStatus => ({
              accountName: s.accountName,
              state: s.state,
              lastTransitionAt: new Date().toISOString(),
              retryCount: 0,
            }),
          }) as unknown as BotSession,
      ),
    removeFromPool,
  } as never;
}

beforeEach(() => {
  vi.clearAllMocks();
});

describe('BotHealthCheck', () => {
  it('marks every READY bot as healthy and does not attempt recovery', async () => {
    const manager = makeManager([
      { accountName: 'a', state: 'READY', recoverSession: vi.fn() },
      { accountName: 'b', state: 'READY', recoverSession: vi.fn() },
    ]);
    const hc = new BotHealthCheck(manager);
    const result = await hc.runTick();
    expect(result).toEqual({ healthy: 2, total: 2, recovered: 0, removed: 0 });
    expect(manager.removeFromPool).not.toHaveBeenCalled();
  });

  it('triggers recoverSession for non-ready, non-terminal sessions', async () => {
    const recoverable: FakeSession = {
      accountName: 'r',
      state: 'SESSION_EXPIRED',
      recoverSession: vi.fn().mockResolvedValue(true),
    };
    const manager = makeManager([recoverable]);
    const hc = new BotHealthCheck(manager);
    const result = await hc.runTick();
    expect(recoverable.recoverSession).toHaveBeenCalled();
    expect(result.recovered).toBe(1);
  });

  it('removes from pool when recovery fails', async () => {
    const dying: FakeSession = {
      accountName: 'dying',
      state: 'SESSION_EXPIRED',
      recoverSession: vi.fn().mockResolvedValue(false),
    };
    const manager = makeManager([dying]);
    const hc = new BotHealthCheck(manager);
    const result = await hc.runTick();
    expect(result.removed).toBe(1);
    expect(manager.removeFromPool).toHaveBeenCalledWith('dying', 'session_recovery_failed');
  });

  it('sweeps already-terminal sessions out of the pool', async () => {
    const terminal: FakeSession = {
      accountName: 'dead',
      state: 'FAILED',
      recoverSession: vi.fn(),
    };
    const manager = makeManager([terminal]);
    const hc = new BotHealthCheck(manager);
    const result = await hc.runTick();
    expect(result.removed).toBe(1);
    expect(terminal.recoverSession).not.toHaveBeenCalled();
    expect(manager.removeFromPool).toHaveBeenCalledWith('dead', 'session_recovery_failed');
  });

  it('skips overlapping ticks (inFlight guard)', async () => {
    let resolveRecover!: (v: boolean) => void;
    const session: FakeSession = {
      accountName: 's',
      state: 'SESSION_EXPIRED',
      recoverSession: vi.fn(() => new Promise<boolean>((res) => (resolveRecover = res))),
    };
    const manager = makeManager([session]);
    const hc = new BotHealthCheck(manager);
    const first = hc.runTick();
    const second = await hc.runTick();
    expect(second).toEqual({ healthy: 0, total: 0, recovered: 0, removed: 0 });
    resolveRecover(true);
    await first;
  });

  it('start/stop manage the scheduler exactly once', () => {
    const manager = makeManager([]);
    const handle = { unref: vi.fn() } as unknown as NodeJS.Timeout;
    const scheduler = vi.fn(() => handle);
    const cancelScheduler = vi.fn();
    const hc = new BotHealthCheck(manager, { scheduler, cancelScheduler, intervalMs: 1000 });
    hc.start();
    hc.start(); // idempotent
    expect(scheduler).toHaveBeenCalledTimes(1);
    hc.stop();
    hc.stop(); // idempotent
    expect(cancelScheduler).toHaveBeenCalledTimes(1);
  });
});
