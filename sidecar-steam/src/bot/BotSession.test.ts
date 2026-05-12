import { describe, it, expect, vi, beforeEach } from 'vitest';
import { EventEmitter } from 'events';

vi.mock('../logger.js', () => ({
  logger: {
    info: vi.fn(),
    warn: vi.fn(),
    error: vi.fn(),
    debug: vi.fn(),
    child: vi.fn().mockReturnThis(),
  },
}));

vi.mock('steam-totp', () => ({
  default: {
    generateAuthCode: vi.fn(() => 'TOTP123'),
  },
  generateAuthCode: vi.fn(() => 'TOTP123'),
}));

import {
  BotSession,
  type BotFailureReason,
  type BotSessionStatus,
  type BotSessionEvents,
} from './BotSession.js';
import type { BotCredentials } from './BotConfig.js';

class FakeSteamUser extends EventEmitter {
  logOn = vi.fn();
  logOff = vi.fn();
  steamID: { toString(): string } | null = { toString: () => '76561198000000001' };
}

class FakeSteamCommunity extends EventEmitter {
  setCookies = vi.fn();
  startConfirmationChecker = vi.fn();
}

const fakeLogger = {
  info: vi.fn(),
  warn: vi.fn(),
  error: vi.fn(),
  debug: vi.fn(),
  child: vi.fn(function () {
    return fakeLogger;
  }),
} as never;

const credentials: BotCredentials = {
  accountName: 'bot1',
  password: 'pw1',
  sharedSecret: 'shared',
  identitySecret: 'identity',
};

function newSession(
  opts: {
    events?: BotSessionEvents;
    backoffMs?: number[];
  } = {},
) {
  const client = new FakeSteamUser();
  const community = new FakeSteamCommunity();
  // Cast to never to satisfy the strict steam-user / steamcommunity types
  // — the BotSession only uses event-emitter + a small set of methods that
  // FakeSteamUser/FakeSteamCommunity implement faithfully.
  const session = new BotSession(
    credentials,
    opts.events ?? {},
    { backoffMs: opts.backoffMs ?? [1, 1, 1] },
    { client: client as never, community: community as never, logger: fakeLogger },
  );
  return { session, client, community };
}

beforeEach(() => {
  vi.clearAllMocks();
});

describe('BotSession', () => {
  it('starts in INITIALIZING state', () => {
    const { session } = newSession();
    expect(session.getStatus().state).toBe('INITIALIZING');
  });

  it('start() calls logOn with TOTP code and transitions LOGGING_IN', async () => {
    const { session, client } = newSession();
    await session.start();
    expect(client.logOn).toHaveBeenCalledTimes(1);
    expect(client.logOn).toHaveBeenCalledWith({
      accountName: 'bot1',
      password: 'pw1',
      twoFactorCode: 'TOTP123',
    });
    expect(session.getStatus().state).toBe('LOGGING_IN');
  });

  it('webSession event sets cookies and transitions to READY', async () => {
    const onStateChanged = vi.fn();
    const { session, client, community } = newSession({ events: { onStateChanged } });
    await session.start();
    client.emit('webSession', 'sid', ['c1=v1']);
    expect(community.setCookies).toHaveBeenCalledWith(['c1=v1']);
    expect(community.startConfirmationChecker).toHaveBeenCalledWith(20_000, 'identity');
    expect(session.isReady()).toBe(true);
    expect(session.getStatus().state).toBe('READY');
    expect(onStateChanged).toHaveBeenCalled();
  });

  it('confirmation checker is started exactly once across multiple webSession events', async () => {
    const { session, client, community } = newSession();
    await session.start();
    client.emit('webSession', 'sid', ['c1=v1']);
    client.emit('webSession', 'sid', ['c2=v2']);
    expect(community.startConfirmationChecker).toHaveBeenCalledTimes(1);
    expect(session.isReady()).toBe(true);
  });

  it('permanent eresult (InvalidPassword) transitions FAILED and fires onFatalFailure', async () => {
    const onFatalFailure = vi.fn();
    const { session, client } = newSession({ events: { onFatalFailure } });
    await session.start();
    const err: Error & { eresult?: number } = new Error('Invalid password');
    err.eresult = 5;
    client.emit('error', err);
    expect(session.getStatus().state).toBe('FAILED');
    expect(session.isTerminal()).toBe(true);
    expect(onFatalFailure).toHaveBeenCalledTimes(1);
    const [, reason] = onFatalFailure.mock.calls[0] as [BotSessionStatus, BotFailureReason];
    expect(reason).toBe('login_failed');
  });

  it('banned eresult transitions BANNED', async () => {
    const onFatalFailure = vi.fn();
    const { session, client } = newSession({ events: { onFatalFailure } });
    await session.start();
    const err: Error & { eresult?: number } = new Error('Locked down');
    err.eresult = 70;
    client.emit('error', err);
    expect(session.getStatus().state).toBe('BANNED');
    expect(onFatalFailure).toHaveBeenCalledWith(
      expect.objectContaining({ state: 'BANNED' }),
      'banned',
    );
  });

  it('transient error does not transition state', async () => {
    const { session, client } = newSession();
    await session.start();
    const err: Error & { eresult?: number } = new Error('NoConnection');
    err.eresult = 3; // (BANNED — sentinel; pick a transient one instead)
    // Use a code outside both PERMANENT and BANNED sets to guarantee no transition.
    err.eresult = 84; // RateLimitExceeded — transient
    client.emit('error', err);
    expect(session.getStatus().state).toBe('LOGGING_IN');
    expect(session.getStatus().lastError).toMatch(/eresult=84/);
  });

  it('sessionExpired triggers SESSION_EXPIRED transition and recovery loop', async () => {
    const { session, client, community } = newSession({ backoffMs: [1] });

    // Bring the session to READY first.
    await session.start();
    client.emit('webSession', 'sid', ['c1=v1']);
    expect(session.isReady()).toBe(true);

    // Wire recovery: simulate webSession arriving on the next logOn attempt.
    client.logOn.mockImplementation(() => {
      // schedule the webSession event after the current microtask
      Promise.resolve().then(() => client.emit('webSession', 'sid2', ['c2=v2']));
    });

    community.emit('sessionExpired', null);

    // Wait long enough for a 1ms backoff + microtask
    await new Promise((r) => setTimeout(r, 30));

    expect(session.isReady()).toBe(true);
    expect(client.logOn).toHaveBeenCalledTimes(2); // start + recovery
  });

  it('recoverSession exhausts backoff and declares FAILED + onFatalFailure', async () => {
    const onFatalFailure = vi.fn();
    const { session, client } = newSession({
      events: { onFatalFailure },
      backoffMs: [1, 1, 1],
    });
    await session.start();
    client.emit('webSession', 'sid', ['c1=v1']);

    client.logOn.mockImplementation(() => {
      Promise.resolve().then(() => {
        const err: Error & { eresult?: number } = new Error('NoConnection');
        err.eresult = 84; // transient (RateLimitExceeded) — per attempt resolves as 'not ready'
        client.emit('error', err);
      });
    });

    const recovered = await session.recoverSession();
    expect(recovered).toBe(false);
    expect(session.getStatus().state).toBe('FAILED');
    expect(onFatalFailure).toHaveBeenCalledWith(
      expect.objectContaining({ state: 'FAILED' }),
      'session_recovery_failed',
    );
  });

  it('stop() transitions STOPPED and calls logOff', async () => {
    const { session, client } = newSession();
    await session.start();
    await session.stop();
    expect(session.getStatus().state).toBe('STOPPED');
    expect(session.isTerminal()).toBe(true);
    expect(client.logOff).toHaveBeenCalled();
  });

  it('stop() is idempotent', async () => {
    const { session, client } = newSession();
    await session.start();
    await session.stop();
    await session.stop();
    expect(client.logOff).toHaveBeenCalledTimes(1);
  });

  it('isHealthy is false unless state is READY', async () => {
    const { session, client } = newSession();
    expect(session.isHealthy()).toBe(false);
    await session.start();
    expect(session.isHealthy()).toBe(false);
    client.emit('webSession', 'sid', ['c1=v1']);
    expect(session.isHealthy()).toBe(true);
  });

  it('getStatus carries retryCount and lastError', async () => {
    const { session, client } = newSession();
    await session.start();
    const err: Error & { eresult?: number } = new Error('boom');
    err.eresult = 84;
    client.emit('error', err);
    const status = session.getStatus();
    expect(status.retryCount).toBe(0);
    expect(status.lastError).toContain('boom');
  });
});
