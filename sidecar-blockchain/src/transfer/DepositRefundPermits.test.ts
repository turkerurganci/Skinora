import { mkdtempSync, readFileSync, readdirSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import TronWeb from 'tronweb';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { SidecarError, TransferDeferredError } from '../errors/SidecarError.js';
import { runPermitCli } from '../cli/permitCli.js';
import { DepositRefundLimit, openDepositRefundLedger } from './DepositRefundLimit.js';
import {
  DepositRefundPermitStore,
  MAX_PERMIT_VALIDITY_MS,
  PERMIT_DIR_NAME,
  type PermitEvent,
} from './DepositRefundPermits.js';
import { RefundService, type RefundRequest } from './RefundService.js';
import { TransferGuard, parseLimitUnits } from './TransferGuard.js';

/**
 * T172b — the operator's one-time permit past the signer's deposit refund cap
 * (proje sahibi kararı 2026-10-23, modal: "sunucuda tek seferlik izin").
 * Binds network + refund id + recipient + exact amount + validity; single use;
 * the permitted refund still counts; created only by the on-server command.
 */

const UNIT = 1_000_000n;
const TOKEN_USDT = 'TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t';
const TOKEN_USDC = 'TEkxiTehnzSmSe2XqrBj4w32RUN966rdz8';
const HOT_WALLET = 'TMmY2ARUpirKFwuW8HMGDuEkBWZZjK44jE';
const BUYER = 'TGkh6US9LiJc1iovkYoAfTZpCGZtfM6nY5';
const OTHER_BUYER = 'TGpQ6KteKAbJDu7zZuoRnUvUTxvjKG4tv5';
const T0 = 1_800_000_000_000;
const STUB = { signer: 'cd'.padStart(64, 'c') };
const ID = (n: number) => `00000000-0000-0000-0000-${n.toString().padStart(12, '0')}`;

let dir: string;
let clock: number;
let events: PermitEvent[];

beforeEach(() => {
  dir = mkdtempSync(join(tmpdir(), 'tron-permit-'));
  clock = T0;
  events = [];
});

afterEach(() => rmSync(dir, { recursive: true, force: true }));

function store(network: 'TRON' | 'ETHEREUM' = 'TRON'): DepositRefundPermitStore {
  return new DepositRefundPermitStore({
    baseDir: dir,
    network,
    isValidAddress: (a) => TronWeb.isAddress(a),
    now: () => clock,
    onEvent: (e) => events.push(e),
  });
}

function permit(overrides: Partial<Parameters<DepositRefundPermitStore['create']>[0]> = {}) {
  return store().create({
    blockchainTransactionId: ID(1),
    recipient: BUYER,
    amount: '500',
    createdBy: 'operator',
    reason: 'single refund above the cap',
    ...overrides,
  });
}

function limit(maxUsdt = '100'): DepositRefundLimit {
  const opened = openDepositRefundLedger(dir);
  return new DepositRefundLimit({
    maxDailyUnits: parseLimitUnits(maxUsdt, UNIT),
    ledger: opened.ledger,
    ledgerError: opened.error,
    decimalsPower: UNIT,
    permits: store(),
    now: () => clock,
  });
}

function service(
  depositRefundLimit: DepositRefundLimit,
  options: { send?: () => Promise<{ txHash: string }>; gate?: Promise<void> } = {},
) {
  let n = 0;
  const sendTransfer = vi.fn(options.send ?? (async () => ({ txHash: `tx-${++n}` })));
  const refundService = new RefundService({
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
    client: { sendTransfer } as any,
    tokenContracts: { USDT: TOKEN_USDT, USDC: TOKEN_USDC },
    guard: new TransferGuard({
      hotWalletAddress: HOT_WALLET,
      coldWalletAddress: '',
      maxSingleTransferUnits: 10_000n * UNIT,
      maxDailyOutflowUnits: 100_000n * UNIT,
      tokenContracts: { USDT: TOKEN_USDT, USDC: TOKEN_USDC },
      history: { listTrc20: vi.fn(async () => ({ records: [], fingerprint: null })) },
    }),
    depositRefundLimit,
    energyDelegation: {
      withDelegation: vi.fn(async (_t: unknown, action: () => Promise<{ txHash: string }>) => {
        if (options.gate) await options.gate;
        return {
          mode: 'burn',
          delegationAmountSun: 0,
          fallbackAmountSun: 0,
          platformSpendSun: 0,
          action: await action(),
        };
      }),
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
    } as any,
  });
  return { refundService, sendTransfer };
}

function refund(n: number, amount: string, to = BUYER): RefundRequest {
  return {
    blockchainTransactionId: ID(n),
    depositIndex: n,
    depositAddress: `TDeposit${n}`,
    toBuyerAddress: to,
    amount,
    token: 'USDT',
    correlationId: `c-${n}`,
  };
}

async function waits(promise: Promise<unknown>): Promise<string> {
  const err = await promise.then(
    () => null,
    (e: unknown) => e,
  );
  expect(err).toBeInstanceOf(TransferDeferredError);
  return (err as TransferDeferredError).code;
}

describe('a permit lets exactly one refund past the cap', () => {
  it('the matching refund is signed once, counts toward the window, and the cap stays shut for others', async () => {
    const created = permit();
    const depositRefundLimit = limit();
    const { refundService, sendTransfer } = service(depositRefundLimit);

    await refundService.refund(refund(1, '500'));
    expect(sendTransfer).toHaveBeenCalledTimes(1);
    expect(depositRefundLimit.usedUnits()).toBe(500n * UNIT);
    const ledger = JSON.parse(readFileSync(join(dir, 'deposit-refunds.json'), 'utf8'));
    expect(ledger[0].permitId).toBe(created.id);
    expect(store().list()[0].consumedAt).toBe(T0);
    expect(events).toEqual(expect.arrayContaining(['created', 'seen', 'used']));

    // The window is now over the cap: even a 1-unit refund waits.
    expect(await waits(refundService.refund(refund(2, '0.000001')))).toBe(
      'DEPOSIT_REFUND_DAILY_LIMIT_EXCEEDED',
    );
  });

  it('single use: the same refund asked again is refused (the permit is consumed)', async () => {
    permit();
    const { refundService, sendTransfer } = service(limit());
    await refundService.refund(refund(1, '500'));
    expect(await waits(refundService.refund(refund(1, '500')))).toBe(
      'DEPOSIT_REFUND_DAILY_LIMIT_EXCEEDED',
    );
    expect(sendTransfer).toHaveBeenCalledTimes(1);
  });

  it.each([
    ['another recipient', refund(1, '500', OTHER_BUYER)],
    ['another amount (exact match, not a ceiling)', refund(1, '499.999999')],
    ['a larger amount', refund(1, '500.000001')],
    ['another refund id', refund(2, '500')],
  ])('%s is not covered — it waits, nothing signed', async (_label, request) => {
    permit();
    const { refundService, sendTransfer } = service(limit());
    expect(await waits(refundService.refund(request))).toBe('DEPOSIT_REFUND_DAILY_LIMIT_EXCEEDED');
    expect(sendTransfer).not.toHaveBeenCalled();
    expect(store().list()[0].consumedAt).toBeNull();
  });

  it("another network's permit is never honoured", async () => {
    store('ETHEREUM').create({
      blockchainTransactionId: ID(1),
      recipient: BUYER,
      amount: '500',
      createdBy: 'operator',
      reason: 'wrong service',
    });
    const { refundService, sendTransfer } = service(limit());
    expect(await waits(refundService.refund(refund(1, '500')))).toBe(
      'DEPOSIT_REFUND_DAILY_LIMIT_EXCEEDED',
    );
    expect(sendTransfer).not.toHaveBeenCalled();
  });

  it('an expired permit is refused and reported (log + metric) once', async () => {
    permit({ validForMs: 60 * 60 * 1000 });
    clock = T0 + 60 * 60 * 1000; // expiresAt reached
    const { refundService, sendTransfer } = service(limit());
    expect(await waits(refundService.refund(refund(1, '500')))).toBe(
      'DEPOSIT_REFUND_DAILY_LIMIT_EXCEEDED',
    );
    expect(sendTransfer).not.toHaveBeenCalled();
    store().reportExpiredAll();
    expect(events.filter((e) => e === 'expired')).toHaveLength(1);
    expect(store().list()[0].expiredReportedAt).toBe(clock);
  });

  it('a revoked permit is refused', async () => {
    const created = permit();
    store().revoke(created.id);
    expect(
      store().findUsable({
        blockchainTransactionId: ID(1),
        recipient: BUYER,
        amountUnits: 500n * UNIT,
      }),
    ).toBeNull();
    const { refundService } = service(limit());
    expect(await waits(refundService.refund(refund(1, '500')))).toBe(
      'DEPOSIT_REFUND_DAILY_LIMIT_EXCEEDED',
    );
  });

  it('two identical requests at once: one permit, one signing', async () => {
    permit();
    let open!: () => void;
    const gate = new Promise<void>((r) => (open = r));
    const { refundService, sendTransfer } = service(limit(), { gate });
    const both = Promise.allSettled([
      refundService.refund(refund(1, '500')),
      refundService.refund(refund(1, '500')),
    ]);
    open();
    const results = await both;
    expect(results.map((r) => r.status).sort()).toEqual(['fulfilled', 'rejected']);
    expect(sendTransfer).toHaveBeenCalledTimes(1);
  });

  it('a permitted refund that certainly never left gives the permit back; one that may have left does not', async () => {
    permit();
    const failing = service(limit(), {
      send: async () => {
        throw new SidecarError('build', 'TRANSFER_BUILD_FAILED', true);
      },
    });
    await expect(failing.refundService.refund(refund(1, '500'))).rejects.toBeInstanceOf(
      SidecarError,
    );
    expect(store().list()[0].consumedAt).toBeNull();
    expect(events).toContain('restored');

    const ambiguous = service(limit(), {
      send: async () => {
        throw new SidecarError('broadcast', 'TRANSFER_BROADCAST_FAILED', true);
      },
    });
    await expect(ambiguous.refundService.refund(refund(1, '500'))).rejects.toBeInstanceOf(
      SidecarError,
    );
    expect(store().list()[0].consumedAt).not.toBeNull();
  });

  it('a refund that fits the window does not touch the permit (read only over the cap)', async () => {
    permit({ amount: '50' });
    const { refundService } = service(limit());
    await refundService.refund(refund(1, '50'));
    // Fits without the permit: the permit stays unused (it is only read over the cap).
    expect(store().list()[0].consumedAt).toBeNull();
  });
});

describe('the permit command — on the server only', () => {
  function cli(argv: string[]) {
    const lines: string[] = [];
    const code = runPermitCli(
      argv,
      () => store(),
      MAX_PERMIT_VALIDITY_MS,
      (l) => lines.push(l),
    );
    return { code, body: JSON.parse(lines.join('\n')) as Record<string, unknown> };
  }
  const base = ['--tx', ID(1), '--to', BUYER, '--amount', '500', '--by', 'op', '--reason', 'r'];

  it('creates a 24-hour permit by default', () => {
    const { code, body } = cli(['create', ...base]);
    expect(code).toBe(0);
    const created = body.created as { expiresAt: number; amountUnits: string; network: string };
    expect(created.expiresAt).toBe(T0 + 24 * 60 * 60 * 1000);
    expect(created.amountUnits).toBe('500000000');
    expect(created.network).toBe('TRON');
    expect(readdirSync(join(dir, PERMIT_DIR_NAME))).toHaveLength(1);
  });

  it.each([
    ['a malformed address', ['--to', '0x1111111111111111111111111111111111111111']],
    ['a malformed amount', ['--amount', '1e3']],
    ['too many decimals', ['--amount', '1.0000001']],
    ['zero', ['--amount', '0']],
    ['more than 24 hours', ['--hours', '25']],
    ['a refund id that is not a GUID', ['--tx', 'bt-1']],
    ['no operator', ['--by', ' ']],
  ])('refuses %s', (_label, override) => {
    const args = [...base];
    const at = args.indexOf(override[0]);
    if (at >= 0) args[at + 1] = override[1];
    else args.push(...override);
    const { code, body } = cli(['create', ...args]);
    expect(code).toBe(1);
    expect(body.error).toBeTruthy();
    expect(store().list()).toHaveLength(0);
  });

  it('lists and revokes', () => {
    const { body } = cli(['create', ...base]);
    const id = (body.created as { id: string }).id;
    expect((cli(['list']).body.permits as unknown[]).length).toBe(1);
    expect(cli(['revoke', '--id', id]).code).toBe(0);
    expect(store().list()[0].revokedAt).toBe(T0);
  });

  it('no HTTP route can create a permit: the API layer never touches the permit store', () => {
    const apiDir = resolve(__dirname, '../api');
    for (const file of readdirSync(apiDir)) {
      if (!file.endsWith('.ts') || file.endsWith('.test.ts')) continue;
      const source = readFileSync(join(apiDir, file), 'utf8');
      expect(source, file).not.toMatch(/DepositRefundPermit|permitCli/);
    }
    const index = readFileSync(resolve(__dirname, '../index.ts'), 'utf8');
    expect(index).not.toMatch(/\.create\(\{[^}]*blockchainTransactionId/);
  });
});
