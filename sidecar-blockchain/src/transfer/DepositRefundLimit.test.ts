import { mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { SidecarError, TransferDeferredError } from '../errors/SidecarError.js';
import { depositRefundLimitRefusalsTotal } from '../metrics.js';
import {
  DEPOSIT_REFUND_WINDOW_MS,
  DepositRefundLedger,
  DepositRefundLimit,
  openDepositRefundLedger,
} from './DepositRefundLimit.js';
import { RefundService, type RefundRequest } from './RefundService.js';
import { TransferService } from './TransferService.js';
import { TransferGuard, parseLimitUnits } from './TransferGuard.js';

/**
 * T172b — the signer's 24-hour deposit refund cap (proje sahibi kararı
 * 2026-10-23). Each money rule has a case: the boundary (equal passes), the
 * rolling window, a restart, two refunds at once, what does and does not
 * count, an unset / malformed cap, an unusable ledger, and the release rule
 * (only a transfer that certainly never left is uncounted).
 */

const UNIT = 1_000_000n;
const TOKEN_USDT = 'TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t';
const TOKEN_USDC = 'TEkxiTehnzSmSe2XqrBj4w32RUN966rdz8';
const HOT_WALLET = 'TMmY2ARUpirKFwuW8HMGDuEkBWZZjK44jE';
const COLD_WALLET = 'TGpQ6KteKAbJDu7zZuoRnUvUTxvjKG4tv5';
const T0 = 1_800_000_000_000;
// dummy signing material for the stub wallet — never reaches a chain
const STUB = { signer: 'cd'.padStart(64, 'c'), hot: '01'.padStart(64, '0') };

let dir: string;
let clock: number;

beforeEach(() => {
  dir = mkdtempSync(join(tmpdir(), 'tron-deposit-refund-'));
  clock = T0;
});

afterEach(() => rmSync(dir, { recursive: true, force: true }));

function limit(maxUsdt: string | null = '100', ledgerDir: string = dir): DepositRefundLimit {
  const opened = openDepositRefundLedger(ledgerDir);
  return new DepositRefundLimit({
    maxDailyUnits: maxUsdt === null ? null : parseLimitUnits(maxUsdt, UNIT),
    ledger: opened.ledger,
    ledgerError: opened.error,
    decimalsPower: UNIT,
    now: () => clock,
  });
}

function guard(): TransferGuard {
  return new TransferGuard({
    hotWalletAddress: HOT_WALLET,
    coldWalletAddress: COLD_WALLET,
    maxSingleTransferUnits: 10_000n * UNIT,
    maxDailyOutflowUnits: 100_000n * UNIT,
    tokenContracts: { USDT: TOKEN_USDT, USDC: TOKEN_USDC },
    history: { listTrc20: vi.fn(async () => ({ records: [], fingerprint: null })) },
  });
}

interface Harness {
  service: RefundService;
  sendTransfer: ReturnType<typeof vi.fn>;
  withDelegation: ReturnType<typeof vi.fn>;
}

function refundService(
  depositRefundLimit: DepositRefundLimit,
  options: {
    send?: () => Promise<{ txHash: string }>;
    beforeAction?: () => Promise<void>;
  } = {},
): Harness {
  let n = 0;
  const sendTransfer = vi.fn(options.send ?? (async () => ({ txHash: `tx-${++n}` })));
  const withDelegation = vi.fn(
    async (_transfer: unknown, action: () => Promise<{ txHash: string }>) => {
      if (options.beforeAction) await options.beforeAction();
      const result = await action();
      return {
        mode: 'burn',
        delegationAmountSun: 0,
        fallbackAmountSun: 0,
        platformSpendSun: 1,
        action: result,
      };
    },
  );
  const service = new RefundService({
    walletManager: {
      deriveSigner: (index: number) => ({
        address: `TDeposit${index}`,
        derivationPath: `m/44'/195'/0'/0/${index}`,
        index,
        privateKey: STUB.signer,
      }),
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
    } as any,
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    client: { sendTransfer } as any,
    tokenContracts: { USDT: TOKEN_USDT, USDC: TOKEN_USDC },
    guard: guard(),
    depositRefundLimit,
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    energyDelegation: { withDelegation } as any,
  });
  return { service, sendTransfer, withDelegation };
}

function refund(n: number, amount: string, token: 'USDT' | 'USDC' = 'USDT'): RefundRequest {
  return {
    blockchainTransactionId: `bt-${n}`,
    depositIndex: n,
    depositAddress: `TDeposit${n}`,
    toBuyerAddress: 'TBuyer',
    amount,
    token,
    correlationId: `corr-${n}`,
  };
}

async function refusal(promise: Promise<unknown>): Promise<TransferDeferredError> {
  const err = await promise.then(
    () => null,
    (e: unknown) => e,
  );
  expect(err).toBeInstanceOf(TransferDeferredError);
  return err as TransferDeferredError;
}

describe('the cap — boundary and what counts', () => {
  it('an amount that brings the 24 h total exactly to the cap is signed; one unit more waits, nothing sent', async () => {
    const h = refundService(limit('100'));
    await h.service.refund(refund(1, '60'));
    await h.service.refund(refund(2, '40')); // 100 = cap → passes
    expect(h.sendTransfer).toHaveBeenCalledTimes(2);

    const err = await refusal(h.service.refund(refund(3, '0.000001')));
    expect(err.code).toBe('DEPOSIT_REFUND_DAILY_LIMIT_EXCEEDED');
    expect(err.retryable).toBe(true);
    expect(err.deferred).toBe(true);
    // Refused before any TRX was spent on the deposit or anything was signed.
    expect(h.withDelegation).toHaveBeenCalledTimes(2);
    expect(h.sendTransfer).toHaveBeenCalledTimes(2);
  });

  it('a single refund above the cap never passes on its own', async () => {
    const h = refundService(limit('100'));
    const err = await refusal(h.service.refund(refund(1, '100.000001')));
    expect(err.code).toBe('DEPOSIT_REFUND_DAILY_LIMIT_EXCEEDED');
    expect(h.sendTransfer).not.toHaveBeenCalled();
  });

  it('USDC counts 1:1 with USDT (wrong-token refunds share the cap)', async () => {
    const h = refundService(limit('100'));
    await h.service.refund(refund(1, '70', 'USDC'));
    const err = await refusal(h.service.refund(refund(2, '30.5', 'USDT')));
    expect(err.code).toBe('DEPOSIT_REFUND_DAILY_LIMIT_EXCEEDED');
  });

  it('sweeps and payouts do not count — only what the deposit key sends to a caller-chosen address', async () => {
    const depositRefundLimit = limit('100');
    let n = 0;
    const client = { sendTransfer: vi.fn(async () => ({ txHash: `tx-${++n}` })) };
    const transfers = new TransferService({
      walletManager: {
        deriveSigner: (index: number) => ({
          address: `TDeposit${index}`,
          derivationPath: '',
          index,
          privateKey: STUB.signer,
        }),
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
      } as any,
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      client: client as any,
      tokenContracts: { USDT: TOKEN_USDT, USDC: TOKEN_USDC },
      hotWalletAddress: HOT_WALLET,
      hotWalletPrivateKey: STUB.hot,
      guard: guard(),
      energyDelegation: {
        withDelegation: vi.fn(async (_t: unknown, action: () => Promise<{ txHash: string }>) => ({
          mode: 'burn',
          delegationAmountSun: 0,
          fallbackAmountSun: 0,
          platformSpendSun: 0,
          action: await action(),
        })),
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
      } as any,
    });
    await transfers.sweep({
      blockchainTransactionId: 'sw-1',
      depositIndex: 9,
      depositAddress: 'TDeposit9',
      toHotWalletAddress: HOT_WALLET,
      amount: '500',
      token: 'USDT',
      correlationId: 'c',
    });
    await transfers.payout({
      blockchainTransactionId: 'po-1',
      toAddress: 'TSeller',
      amount: '500',
      token: 'USDT',
      correlationId: 'c',
    });
    expect(depositRefundLimit.usedUnits()).toBe(0n);

    // …and every refund counts, whatever the backend calls it: the TRON
    // refund body carries no type the cap could be talked out of.
    await refundService(depositRefundLimit).service.refund(refund(1, '100'));
    expect(depositRefundLimit.usedUnits()).toBe(100n * UNIT);
  });

  it('a retry of the same id is a new signing attempt and counts again (no dedup an attacker could reuse)', async () => {
    const h = refundService(limit('100'));
    await h.service.refund(refund(1, '50'));
    await h.service.refund(refund(1, '50'));
    const err = await refusal(h.service.refund(refund(1, '50')));
    expect(err.code).toBe('DEPOSIT_REFUND_DAILY_LIMIT_EXCEEDED');
    expect(h.sendTransfer).toHaveBeenCalledTimes(2);
  });
});

describe('the rolling 24-hour window', () => {
  it('room comes back only when the earlier refund turns 24 hours old', async () => {
    const h = refundService(limit('100'));
    await h.service.refund(refund(1, '80'));

    clock = T0 + DEPOSIT_REFUND_WINDOW_MS - 1;
    expect((await refusal(h.service.refund(refund(2, '30')))).code).toBe(
      'DEPOSIT_REFUND_DAILY_LIMIT_EXCEEDED',
    );
    clock = T0 + DEPOSIT_REFUND_WINDOW_MS; // still inside: window is [now − 24 h, now]
    expect((await refusal(h.service.refund(refund(2, '30')))).code).toBe(
      'DEPOSIT_REFUND_DAILY_LIMIT_EXCEEDED',
    );
    clock = T0 + DEPOSIT_REFUND_WINDOW_MS + 1;
    await h.service.refund(refund(2, '30'));
    expect(h.sendTransfer).toHaveBeenCalledTimes(2);
  });

  it('no midnight reset: 100 at 23:59 and 100 at 00:01 cannot both go', async () => {
    const h = refundService(limit('100'));
    clock = Date.UTC(2026, 9, 23, 23, 59);
    await h.service.refund(refund(1, '100'));
    clock = Date.UTC(2026, 9, 24, 0, 1);
    expect((await refusal(h.service.refund(refund(2, '100')))).code).toBe(
      'DEPOSIT_REFUND_DAILY_LIMIT_EXCEEDED',
    );
  });

  it('the window is exactly 24 hours — pinned by a literal, not by the constant (S4-1)', async () => {
    // Uses a literal on purpose: a test that imports the constant would stay
    // green if the window were halved (twice the cap a day) or stretched.
    const HOUR = 60 * 60 * 1000;
    const h = refundService(limit('100'));
    await h.service.refund(refund(1, '100'));

    // 12 h and 23 h 59 min later the refund still counts: nothing more goes.
    for (const at of [12 * HOUR, 23 * HOUR + 59 * 60 * 1000]) {
      clock = T0 + at;
      expect((await refusal(h.service.refund(refund(2, '1')))).code).toBe(
        'DEPOSIT_REFUND_DAILY_LIMIT_EXCEEDED',
      );
    }
    // Just past 24 h the room is back — a 36 h window would still refuse.
    clock = T0 + 24 * HOUR + 1;
    await h.service.refund(refund(2, '100'));
    expect(h.sendTransfer).toHaveBeenCalledTimes(2);
  });

  it('entries leave the FILE only after 7 days, not when they leave the window', async () => {
    const DAY = 24 * 60 * 60 * 1000;
    const h = refundService(limit('100'));
    await h.service.refund(refund(1, '10'));
    const ids = (): string[] =>
      JSON.parse(readFileSync(join(dir, 'deposit-refunds.json'), 'utf8')).map(
        (e: { blockchainTransactionId: string }) => e.blockchainTransactionId,
      );

    clock = T0 + DAY + 1; // out of the window: no longer counted, still on disk
    await h.service.refund(refund(2, '100'));
    expect(ids()).toEqual(['bt-1', 'bt-2']);

    clock = T0 + 7 * DAY + 1; // past the retention: dropped on the next write
    await h.service.refund(refund(3, '1'));
    expect(ids()).toEqual(['bt-2', 'bt-3']);
  });
});

describe('the host clock jumps (S4-3)', () => {
  const HOUR = 60 * 60 * 1000;

  it('a jump ahead and back does not erase what was counted', async () => {
    const h = refundService(limit('100'));
    await h.service.refund(refund(1, '90'));

    // The clock jumps 30 h ahead (NTP fault, manual setting) and a refund is
    // written then: the 90 is out of the window for now, but it stays on disk.
    clock = T0 + 30 * HOUR;
    await h.service.refund(refund(2, '5'));

    // The clock comes back: the 90 counts again (and the 5, stamped in the
    // future, counts too) — another 10 would make 105.
    clock = T0 + 1 * HOUR;
    expect((await refusal(h.service.refund(refund(3, '10')))).code).toBe(
      'DEPOSIT_REFUND_DAILY_LIMIT_EXCEEDED',
    );
    await h.service.refund(refund(3, '5'));
    expect(h.sendTransfer).toHaveBeenCalledTimes(3);

    // …and a restart reading the file agrees.
    const restarted = refundService(limit('100'));
    expect((await refusal(restarted.service.refund(refund(4, '0.000001')))).code).toBe(
      'DEPOSIT_REFUND_DAILY_LIMIT_EXCEEDED',
    );
  });

  it('an entry stamped in the future (clock was ahead) keeps counting until real time passes it', async () => {
    clock = T0 + 48 * HOUR;
    await refundService(limit('100')).service.refund(refund(1, '100'));
    clock = T0; // back to the right time, two days "before" the entry
    const h = refundService(limit('100'));
    expect((await refusal(h.service.refund(refund(2, '1')))).code).toBe(
      'DEPOSIT_REFUND_DAILY_LIMIT_EXCEEDED',
    );
  });
});

describe('a restart does not reset the window', () => {
  it('a new process on the same volume sees what the old one counted', async () => {
    await refundService(limit('100')).service.refund(refund(1, '90'));

    // "Restart": a fresh ledger and limit read from the same directory.
    const restarted = refundService(limit('100'));
    expect((await refusal(restarted.service.refund(refund(2, '10.000001')))).code).toBe(
      'DEPOSIT_REFUND_DAILY_LIMIT_EXCEEDED',
    );
    await restarted.service.refund(refund(3, '10'));
    expect(restarted.sendTransfer).toHaveBeenCalledTimes(1);
  });

  it('the entry is on disk BEFORE the transfer is broadcast (a crash mid-send still counts it)', async () => {
    let onDiskAtBroadcast: unknown[] = [];
    const h = refundService(limit('100'), {
      send: async () => {
        onDiskAtBroadcast = JSON.parse(readFileSync(join(dir, 'deposit-refunds.json'), 'utf8'));
        return { txHash: 'tx-1' };
      },
    });
    await h.service.refund(refund(1, '25'));
    expect(onDiskAtBroadcast).toEqual([
      expect.objectContaining({ blockchainTransactionId: 'bt-1', units: '25000000', txHash: null }),
    ]);
    const after = JSON.parse(readFileSync(join(dir, 'deposit-refunds.json'), 'utf8'));
    expect(after).toEqual([expect.objectContaining({ txHash: 'tx-1' })]);
  });
});

describe('two refunds at once cannot both pass', () => {
  it('60 + 60 against a cap of 100, issued together: exactly one is signed, the other waits', async () => {
    const depositRefundLimit = limit('100');
    // Both reach the delegation (where the real flow waits on the chain)
    // before either broadcasts.
    let release!: () => void;
    const gate = new Promise<void>((resolve) => (release = resolve));
    const h = refundService(depositRefundLimit, { beforeAction: () => gate });

    const first = h.service.refund(refund(1, '60'));
    const second = h.service.refund(refund(2, '60'));
    const settled = Promise.allSettled([first, second]);
    release();
    const [a, b] = await settled;

    const outcomes = [a.status, b.status].sort();
    expect(outcomes).toEqual(['fulfilled', 'rejected']);
    const rejected = (a.status === 'rejected' ? a : b) as PromiseRejectedResult;
    expect(rejected.reason).toBeInstanceOf(TransferDeferredError);
    expect((rejected.reason as TransferDeferredError).code).toBe(
      'DEPOSIT_REFUND_DAILY_LIMIT_EXCEEDED',
    );
    expect(h.sendTransfer).toHaveBeenCalledTimes(1);
    expect(depositRefundLimit.usedUnits()).toBe(60n * UNIT);
  });
});

describe('fail closed: unset, malformed, no ledger', () => {
  it.each([
    ['unset', ''],
    ['malformed', '1e2'],
    ['zero', '0'],
    ['too many decimals', '100.0000001'],
  ])('%s cap → every deposit refund waits, nothing spent or signed', async (_label, raw) => {
    const h = refundService(limit(raw));
    const err = await refusal(h.service.refund(refund(1, '1')));
    expect(err.code).toBe('DEPOSIT_REFUND_LIMIT_NOT_CONFIGURED');
    expect(h.withDelegation).not.toHaveBeenCalled();
    expect(h.sendTransfer).not.toHaveBeenCalled();
  });

  it('no ledger directory configured → every deposit refund waits', async () => {
    const h = refundService(limit('100', ''));
    const err = await refusal(h.service.refund(refund(1, '1')));
    expect(err.code).toBe('DEPOSIT_REFUND_LEDGER_UNAVAILABLE');
    expect(h.sendTransfer).not.toHaveBeenCalled();
  });

  it('an unreadable ledger file is never treated as empty → every deposit refund waits', async () => {
    writeFileSync(join(dir, 'deposit-refunds.json'), '{"not": "a list"');
    const opened = openDepositRefundLedger(dir);
    expect(opened.ledger).toBeNull();
    const h = refundService(limit('100'));
    expect((await refusal(h.service.refund(refund(1, '1')))).code).toBe(
      'DEPOSIT_REFUND_LEDGER_UNAVAILABLE',
    );
    expect(h.sendTransfer).not.toHaveBeenCalled();
  });

  it.each([
    ['an object, not a list', '{}'],
    ['a list with a non-entry', '[{"foo": 1}]'],
    [
      'units not a number',
      JSON.stringify([
        { key: 'k', blockchainTransactionId: 'bt', units: 'abc', at: T0, txHash: null },
      ]),
    ],
    [
      'at not a number',
      JSON.stringify([
        { key: 'k', blockchainTransactionId: 'bt', units: '1', at: 'now', txHash: null },
      ]),
    ],
  ])(
    'valid JSON of the wrong shape (%s) is never treated as empty → every deposit refund waits (S4-2)',
    async (_label, content) => {
      writeFileSync(join(dir, 'deposit-refunds.json'), content);
      expect(openDepositRefundLedger(dir).ledger).toBeNull();
      const h = refundService(limit('100'));
      expect((await refusal(h.service.refund(refund(1, '1')))).code).toBe(
        'DEPOSIT_REFUND_LEDGER_UNAVAILABLE',
      );
      expect(h.withDelegation).not.toHaveBeenCalled();
      expect(h.sendTransfer).not.toHaveBeenCalled();
    },
  );

  it('a ledger directory that cannot be created → unavailable (the service still starts)', () => {
    const file = join(dir, 'a-file');
    writeFileSync(file, 'x');
    const opened = openDepositRefundLedger(join(file, 'sub'));
    expect(opened.ledger).toBeNull();
    expect(opened.error).toBeTruthy();
  });

  it('every refusal is counted on the alarm metric with its reason', async () => {
    const count = async (reason: string): Promise<number> =>
      (await depositRefundLimitRefusalsTotal.get()).values.find((v) => v.labels.reason === reason)
        ?.value ?? 0;
    const before = await count('DEPOSIT_REFUND_LIMIT_NOT_CONFIGURED');
    const h = refundService(limit(''));
    await refusal(h.service.refund(refund(1, '1')));
    expect(await count('DEPOSIT_REFUND_LIMIT_NOT_CONFIGURED')).toBe(before + 1);
  });
});

describe('release only what certainly never left', () => {
  it('build failed (nothing signed) → uncounted', async () => {
    const depositRefundLimit = limit('100');
    const h = refundService(depositRefundLimit, {
      send: async () => {
        throw new SidecarError('triggerSmartContract failed', 'TRANSFER_BUILD_FAILED', true);
      },
    });
    await expect(h.service.refund(refund(1, '60'))).rejects.toMatchObject({
      code: 'TRANSFER_BUILD_FAILED',
    });
    expect(depositRefundLimit.usedUnits()).toBe(0n);
  });

  it('the resource step failed before the action ran → uncounted', async () => {
    const depositRefundLimit = limit('100');
    const h = refundService(depositRefundLimit, {
      beforeAction: async () => {
        throw new SidecarError('activation failed', 'TRX_TRANSFER_FAILED', true);
      },
    });
    await expect(h.service.refund(refund(1, '60'))).rejects.toMatchObject({
      code: 'TRX_TRANSFER_FAILED',
    });
    expect(depositRefundLimit.usedUnits()).toBe(0n);
  });

  it.each(['TRANSFER_BROADCAST_REJECTED', 'TRANSFER_BROADCAST_FAILED'])(
    '%s may have reached a node → stays counted (fail closed)',
    async (code) => {
      const depositRefundLimit = limit('100');
      const h = refundService(depositRefundLimit, {
        send: async () => {
          throw new SidecarError('broadcast', code, true);
        },
      });
      await expect(h.service.refund(refund(1, '60'))).rejects.toMatchObject({ code });
      expect(depositRefundLimit.usedUnits()).toBe(60n * UNIT);
    },
  );

  it('a failure after the broadcast succeeded (e.g. waiting for the block) stays counted', async () => {
    const depositRefundLimit = limit('100');
    const sendTransfer = vi.fn(async () => ({ txHash: 'tx-1' }));
    const service = new RefundService({
      walletManager: {
        deriveSigner: () => ({
          address: 'TDeposit1',
          derivationPath: '',
          index: 1,
          privateKey: STUB.signer,
        }),
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
      } as any,
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      client: { sendTransfer } as any,
      tokenContracts: { USDT: TOKEN_USDT, USDC: TOKEN_USDC },
      guard: guard(),
      depositRefundLimit,
      energyDelegation: {
        withDelegation: vi.fn(async (_t: unknown, action: () => Promise<unknown>) => {
          await action();
          throw new SidecarError('block wait timed out', 'TRANSFER_BUILD_FAILED', true);
        }),
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
      } as any,
    });
    await expect(service.refund(refund(1, '60'))).rejects.toBeInstanceOf(SidecarError);
    expect(depositRefundLimit.usedUnits()).toBe(60n * UNIT);
  });
});

describe('the ledger file', () => {
  it('reads back exactly what it wrote', () => {
    const ledger = new DepositRefundLedger(dir);
    ledger.replace([{ key: 'k', blockchainTransactionId: 'bt', units: '5', at: 1, txHash: null }]);
    expect(new DepositRefundLedger(dir).list()).toEqual([
      { key: 'k', blockchainTransactionId: 'bt', units: '5', at: 1, txHash: null },
    ]);
  });
});
