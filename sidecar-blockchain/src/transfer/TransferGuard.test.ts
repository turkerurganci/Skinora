import { describe, it, expect, vi } from 'vitest';
import {
  TransferGuard,
  parseLimitUnits,
  OUTFLOW_WINDOW_MS,
  OutflowHistoryPage,
  OutflowHistoryRecord,
} from './TransferGuard.js';

const HOT = 'TMmY2ARUpirKFwuW8HMGDuEkBWZZjK44jE';
const COLD = 'TGpQ6KteKAbJDu7zZuoRnUvUTxvjKG4tv5';
const OTHER = 'TGkh6US9LiJc1iovkYoAfTZpCGZtfM6nY5';
const TOKEN_USDT = 'TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t';
const TOKEN_USDC = 'TEkxiTehnzSmSe2XqrBj4w32RUN966rdz8';
const SPAM_TOKEN = 'TUrLLrScAEZ1WrJLvFm2PQaL7uTnVZ7kCF';
const UNIT = 1_000_000n;
const NOW = 1_700_000_000_000;

function record(overrides: Partial<OutflowHistoryRecord> = {}): OutflowHistoryRecord {
  return {
    transaction_id: overrides.transaction_id ?? 'tx-' + Math.random().toString(16).slice(2),
    from: overrides.from ?? HOT,
    to: overrides.to ?? OTHER,
    value: overrides.value ?? '1000000',
    block_timestamp: overrides.block_timestamp ?? NOW - 60_000,
    token_info: overrides.token_info ?? { address: TOKEN_USDT },
  };
}

interface BuildOptions {
  coldWalletAddress?: string;
  maxSingleTransferUnits?: bigint | null;
  maxDailyOutflowUnits?: bigint | null;
  pages?: OutflowHistoryPage[];
  listTrc20?: (options: Record<string, unknown>) => Promise<OutflowHistoryPage>;
  maxHistoryPages?: number;
}

function build(options: BuildOptions = {}) {
  const pages = options.pages ?? [{ records: [], fingerprint: null }];
  let call = 0;
  const listTrc20 = vi.fn(
    options.listTrc20 ??
      (async () => pages[Math.min(call++, pages.length - 1)] ?? { records: [], fingerprint: null }),
  );
  const guard = new TransferGuard({
    hotWalletAddress: HOT,
    coldWalletAddress: options.coldWalletAddress ?? COLD,
    maxSingleTransferUnits:
      options.maxSingleTransferUnits === undefined
        ? 10_000n * UNIT
        : options.maxSingleTransferUnits,
    maxDailyOutflowUnits:
      options.maxDailyOutflowUnits === undefined ? 1_000n * UNIT : options.maxDailyOutflowUnits,
    tokenContracts: { USDT: TOKEN_USDT, USDC: TOKEN_USDC },
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    history: { listTrc20: listTrc20 as any },
    now: () => NOW,
    maxHistoryPages: options.maxHistoryPages,
  });
  return { guard, listTrc20 };
}

describe('parseLimitUnits()', () => {
  const power = 10n ** 6n;
  it('converts a decimal limit to raw units', () => {
    expect(parseLimitUnits('10000', power)).toBe(10_000n * UNIT);
    expect(parseLimitUnits('0.5', power)).toBe(500_000n);
  });
  it('returns null for unset, malformed, zero or over-precise values (fail closed)', () => {
    expect(parseLimitUnits(undefined, power)).toBeNull();
    expect(parseLimitUnits('  ', power)).toBeNull();
    expect(parseLimitUnits('abc', power)).toBeNull();
    expect(parseLimitUnits('-5', power)).toBeNull();
    expect(parseLimitUnits('0', power)).toBeNull();
    expect(parseLimitUnits('1.0000001', power)).toBeNull();
  });
});

describe('TransferGuard construction', () => {
  it('refuses to construct on a malformed pinned address', () => {
    expect(() => build({ coldWalletAddress: 'not-an-address' })).toThrow(
      /COLD_WALLET_ADDRESS is not a valid Tron address/,
    );
  });
  it('accepts an unset cold wallet (consolidation stays disabled)', () => {
    expect(() => build({ coldWalletAddress: '' })).not.toThrow();
  });
});

describe('TransferGuard destination pinning', () => {
  it('accepts the configured hot wallet as a sweep destination', () => {
    const { guard } = build();
    expect(() => guard.assertSweepDestination(HOT)).not.toThrow();
  });

  it('refuses a sweep to any other address', () => {
    const { guard } = build();
    expect(() => guard.assertSweepDestination(OTHER)).toThrowError(
      expect.objectContaining({ code: 'DESTINATION_NOT_ALLOWED', retryable: false }),
    );
  });

  it('refuses a cold transfer to any other address', () => {
    const { guard } = build();
    expect(() => guard.assertColdDestination(COLD)).not.toThrow();
    expect(() => guard.assertColdDestination(OTHER)).toThrowError(
      expect.objectContaining({ code: 'DESTINATION_NOT_ALLOWED', retryable: false }),
    );
  });

  it('refuses a cold transfer when no cold wallet is configured', () => {
    const { guard } = build({ coldWalletAddress: '' });
    expect(() => guard.assertColdDestination(OTHER)).toThrowError(
      expect.objectContaining({ code: 'COLD_WALLET_NOT_CONFIGURED', retryable: false }),
    );
  });
});

describe('TransferGuard single-transfer limit', () => {
  it('allows an amount at the limit and refuses one above it', () => {
    const { guard } = build({ maxSingleTransferUnits: 100n * UNIT });
    expect(() => guard.assertSingleTransferLimit(100n * UNIT)).not.toThrow();
    expect(() => guard.assertSingleTransferLimit(100n * UNIT + 1n)).toThrowError(
      expect.objectContaining({ code: 'TRANSFER_AMOUNT_ABOVE_LIMIT', retryable: false }),
    );
  });

  it('refuses every transfer when the limit is unset (fail closed)', () => {
    const { guard } = build({ maxSingleTransferUnits: null });
    expect(() => guard.assertSingleTransferLimit(1n)).toThrowError(
      expect.objectContaining({ code: 'TRANSFER_LIMIT_NOT_CONFIGURED', retryable: false }),
    );
  });
});

describe('TransferGuard daily outflow limit', () => {
  // The window's MAGNITUDE is the requirement, not just its internal
  // consistency: 05 §3.3 and 08 §3.3a promise "the last 24 hours", and the
  // operator sizes MAX_DAILY_OUTFLOW_USDT for a day. Every other test here
  // derives its fixture timestamps from OUTFLOW_WINDOW_MS, so they move with
  // the constant and would stay green if it were shortened — a one-hour window
  // carrying a day-sized ceiling lets 24× through. This is the one assertion
  // that does not import its expectation.
  it('measures exactly 24 hours', () => {
    expect(OUTFLOW_WINDOW_MS).toBe(86_400_000);
  });

  it('asks the chain only for confirmed outgoing transfers inside the window', async () => {
    const { guard, listTrc20 } = build();
    await guard.assertHotWalletDailyLimit(1n * UNIT);
    expect(listTrc20).toHaveBeenCalledWith(
      expect.objectContaining({
        address: HOT,
        onlyFrom: true,
        minTimestamp: NOW - 86_400_000,
      }),
    );
  });

  it('refuses when the chain total plus this transfer crosses the limit', async () => {
    const { guard } = build({
      maxDailyOutflowUnits: 1_000n * UNIT,
      pages: [
        {
          records: [record({ value: (900n * UNIT).toString() })],
          fingerprint: null,
        },
      ],
    });
    await expect(guard.assertHotWalletDailyLimit(101n * UNIT)).rejects.toMatchObject({
      code: 'DAILY_OUTFLOW_LIMIT_EXCEEDED',
      retryable: false,
    });
    // ...and allows the transfer that lands exactly on it.
    await expect(guard.assertHotWalletDailyLimit(100n * UNIT)).resolves.toBeUndefined();
  });

  it('ignores records outside the window, other senders, other tokens and cold consolidation', async () => {
    const { guard } = build({
      maxDailyOutflowUnits: 10n * UNIT,
      pages: [
        {
          records: [
            record({
              value: (500n * UNIT).toString(),
              block_timestamp: NOW - OUTFLOW_WINDOW_MS - 1,
            }),
            record({ value: (500n * UNIT).toString(), from: OTHER }),
            record({ value: (500n * UNIT).toString(), token_info: { address: SPAM_TOKEN } }),
            record({ value: (500n * UNIT).toString(), to: COLD }),
          ],
          fingerprint: null,
        },
      ],
    });
    await expect(guard.assertHotWalletDailyLimit(10n * UNIT)).resolves.toBeUndefined();
  });

  it('counts each transaction once and counts USDC alongside USDT', async () => {
    const { guard } = build({
      maxDailyOutflowUnits: 10n * UNIT,
      pages: [
        {
          records: [
            record({ transaction_id: 'dup', value: (4n * UNIT).toString() }),
            record({ transaction_id: 'dup', value: (4n * UNIT).toString() }),
            record({ value: (5n * UNIT).toString(), token_info: { address: TOKEN_USDC } }),
          ],
          fingerprint: null,
        },
      ],
    });
    // 4 (deduped) + 5 = 9; one more unit fits, two do not.
    await expect(guard.assertHotWalletDailyLimit(1n * UNIT)).resolves.toBeUndefined();
    await expect(guard.assertHotWalletDailyLimit(2n * UNIT)).rejects.toMatchObject({
      code: 'DAILY_OUTFLOW_LIMIT_EXCEEDED',
    });
  });

  it('counts a broadcast the chain has not caught up with yet, and stops counting it twice once it appears', async () => {
    const pendingHash = 'tx-pending';
    const { guard } = build({ maxDailyOutflowUnits: 10n * UNIT });
    guard.recordHotWalletOutflow(pendingHash, 9n * UNIT);
    await expect(guard.assertHotWalletDailyLimit(2n * UNIT)).rejects.toMatchObject({
      code: 'DAILY_OUTFLOW_LIMIT_EXCEEDED',
    });

    const settled = build({
      maxDailyOutflowUnits: 10n * UNIT,
      pages: [
        {
          records: [record({ transaction_id: pendingHash, value: (9n * UNIT).toString() })],
          fingerprint: null,
        },
      ],
    });
    settled.guard.recordHotWalletOutflow(pendingHash, 9n * UNIT);
    // Chain 9 + in-flight 0 (same hash) + 1 = 10, exactly at the limit.
    await expect(settled.guard.assertHotWalletDailyLimit(1n * UNIT)).resolves.toBeUndefined();
  });

  it('drops in-flight records once they age out of the window', async () => {
    let now = NOW;
    const guard = new TransferGuard({
      hotWalletAddress: HOT,
      coldWalletAddress: COLD,
      maxSingleTransferUnits: 10_000n * UNIT,
      maxDailyOutflowUnits: 10n * UNIT,
      tokenContracts: { USDT: TOKEN_USDT, USDC: TOKEN_USDC },
      history: { listTrc20: async () => ({ records: [], fingerprint: null }) },
      now: () => now,
    });
    guard.recordHotWalletOutflow('tx-old', 9n * UNIT);
    now += OUTFLOW_WINDOW_MS + 1;
    await expect(guard.assertHotWalletDailyLimit(10n * UNIT)).resolves.toBeUndefined();
  });

  it('follows pagination until the history is exhausted', async () => {
    const full = Array.from({ length: 200 }, () => record({ value: (1n * UNIT).toString() }));
    const { guard, listTrc20 } = build({
      maxDailyOutflowUnits: 1_000n * UNIT,
      pages: [
        { records: full, fingerprint: 'page-2' },
        { records: [record({ value: (5n * UNIT).toString() })], fingerprint: null },
      ],
    });
    await expect(guard.assertHotWalletDailyLimit(1n * UNIT)).resolves.toBeUndefined();
    expect(listTrc20).toHaveBeenCalledTimes(2);
    expect(listTrc20).toHaveBeenLastCalledWith(expect.objectContaining({ fingerprint: 'page-2' }));
  });

  it('refuses when the history is longer than the paging budget', async () => {
    const full = Array.from({ length: 200 }, () => record({ value: '1' }));
    const { guard } = build({
      maxHistoryPages: 2,
      pages: [
        { records: full, fingerprint: 'p2' },
        { records: full, fingerprint: 'p3' },
        { records: full, fingerprint: 'p4' },
      ],
    });
    await expect(guard.assertHotWalletDailyLimit(1n)).rejects.toMatchObject({
      code: 'OUTFLOW_HISTORY_INCOMPLETE',
      retryable: false,
    });
  });

  it('is retryable when the history cannot be read at all', async () => {
    const { guard } = build({
      listTrc20: async () => {
        throw new Error('TronGrid 502');
      },
    });
    await expect(guard.assertHotWalletDailyLimit(1n)).rejects.toMatchObject({
      code: 'OUTFLOW_HISTORY_UNAVAILABLE',
      retryable: true,
    });
  });

  it('refuses every payout when the daily limit is unset (fail closed)', async () => {
    const { guard } = build({ maxDailyOutflowUnits: null });
    await expect(guard.assertHotWalletDailyLimit(1n)).rejects.toMatchObject({
      code: 'TRANSFER_LIMIT_NOT_CONFIGURED',
      retryable: false,
    });
  });
});
