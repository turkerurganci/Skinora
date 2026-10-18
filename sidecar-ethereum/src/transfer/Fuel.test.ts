import { mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { Transaction } from 'ethers';
import type { Server } from 'node:http';
import type { AddressInfo } from 'node:net';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { createApp } from '../api/routes.js';
import type { EthFeeEstimationService } from '../fee/FeeEstimationService.js';
import { canonicalAddress } from '../address/canonicalAddress.js';
import { MAINNET_TOKEN_CONTRACTS } from '../network.js';
import { FuelLedger } from '../reconcile/FuelLedger.js';
import { FakeSendChain } from '../testing/FakeEthereumChain.js';
import { EthereumHdWallet } from '../wallet/HdWalletService.js';
import { seedFuelLedger } from '../wiring.js';
import { decodeTransferCall } from './erc20.js';
import { FEE_CAP_WAIT_REASONS, FeeWaitTracker, GAS_WAIT_REASONS } from './FeeWaitTracker.js';
import {
  CAP_REFERENCE_GAS,
  fuelCapWei,
  capWeiPerTransaction,
  feeCeilingPerGas,
} from './FeePolicy.js';
import * as signerPolicy from './SignerPolicy.js';
import type { FuelIntent } from './SignerPolicy.js';
import { TransferGuard } from './TransferGuard.js';
import {
  TransferService,
  type FuelDeps,
  type RefundRequest,
  type SweepRequest,
} from './TransferService.js';
import { TxJournal } from './TxJournal.js';

/**
 * T153 — the fuel wallet (08 §3.7.5), against an in-memory chain that charges
 * gas like a node (`chargeGas`: a sender that cannot cover `value + gasLimit
 * × maxFeePerGas` is refused and never mined; a mined transaction pays
 * `gasUsed × price`). Each money rule of the fuel has a test here:
 *
 * <list type="bullet">
 *   <item>fuel only before a deposit's own send (sweep, refund), exactly the
 *     amount it lacks, to the deposit re-derived from the seed;</item>
 *   <item>the token send is signed only once the fuel is in a block;</item>
 *   <item>one fuel per request, ever (retry, parallel call, restart);</item>
 *   <item>the fee cap stops the fuel (both its share and the token's);</item>
 *   <item>single and 24-hour caps, the fuel wallet's balance — each a wait,
 *     never a refusal;</item>
 *   <item>another request's fuel on the same deposit is not taken;</item>
 *   <item>the keeper re-broadcasts and bumps a fuel like any send; the
 *     reconciliation's FuelLedger learns every fuel in a block.</item>
 * </list>
 */

// A pass-through spy: the real policy runs; the test reads the intent the
// service built (bulgu b — what `maxForSendWei` the signer is given).
vi.mock('./SignerPolicy.js', async (importOriginal) => {
  const original = await importOriginal<typeof import('./SignerPolicy.js')>();
  return { ...original, assertSignableFuel: vi.fn(original.assertSignableFuel) };
});
const fuelIntents = () =>
  vi.mocked(signerPolicy.assertSignableFuel).mock.calls.map((call) => call[1] as FuelIntent);

const MNEMONIC = `${'abandon '.repeat(11)}about`;
const wallet = new EthereumHdWallet(MNEMONIC);
const HOT = wallet.deriveSigner(30);
const COLD = wallet.derive(31).address;
const FUEL = wallet.deriveSigner(40);
const DEPOSIT = wallet.derive(5);
const DEPOSIT_B = wallet.derive(6);
const USDT = canonicalAddress(MAINNET_TOKEN_CONTRACTS.USDT);
const USDC = canonicalAddress(MAINNET_TOKEN_CONTRACTS.USDC);
const BUYER = '0x2222222222222222222222222222222222222222';
const ETH_USD = 2_364_480_000n; // 2,364.48 $ (T142)
const CAP = '0.50';
const CAP_MICROS = 500_000n;
const ONE_ETH = 10n ** 18n;
const FUEL_SINGLE = 10n ** 15n; // 0.001 ETH
const FUEL_DAILY = 10n ** 16n; // 0.01 ETH

const ID = (n: number) => `00000000-0000-0000-0000-${n.toString().padStart(12, '0')}`;

let dir: string;
let chain: FakeSendChain;
let clock: number;
let ledger: FuelLedger;

beforeEach(() => {
  dir = mkdtempSync(join(tmpdir(), 'eth-fuel-'));
  chain = new FakeSendChain(USDT, USDC);
  chain.chargeGas = true;
  chain.setToken(USDT, HOT.address, 1_000_000_000_000n);
  chain.setEth(HOT.address, ONE_ETH);
  chain.setEth(FUEL.address, ONE_ETH);
  chain.setToken(USDT, DEPOSIT.address, 50_000_000n);
  chain.setToken(USDT, DEPOSIT_B.address, 50_000_000n);
  clock = 1_800_000_000_000;
  ledger = new FuelLedger();
  vi.mocked(signerPolicy.assertSignableFuel).mockClear();
});

afterEach(() => rmSync(dir, { recursive: true, force: true }));

function build(fuel: Partial<FuelDeps> | null = {}) {
  const now = () => clock;
  const journal = new TxJournal(dir);
  const feeWaits = new FeeWaitTracker(dir, now);
  const service = new TransferService({
    rpc: chain.rpc,
    journal,
    feeWaits,
    guard: new TransferGuard({
      rpc: chain.rpc,
      hotWalletAddress: canonicalAddress(HOT.address),
      coldWalletAddress: canonicalAddress(COLD),
      tokenContracts: [USDT, USDC],
      maxSingleTransferUnits: 5_000_000_000n,
      maxDailyOutflowUnits: 20_000_000_000n,
      now,
    }),
    wallet,
    policy: {
      chainId: 11_155_111n,
      paymentToken: USDT,
      wrongTokenRefundToken: USDC,
      maxTokenGasLimit: 150_000n,
    },
    hotWalletKey: HOT.privateKey,
    hotWalletAddress: canonicalAddress(HOT.address),
    tokenContracts: { USDT, USDC },
    prices: { ethUsdMicros: () => Promise.resolve(ETH_USD) },
    finalizedHead: () => chain.finalized,
    bumpAfterMs: 300_000,
    conflictConfirmMs: 30_000,
    fuel:
      fuel === null
        ? undefined
        : {
            key: FUEL.privateKey,
            maxSingleWei: FUEL_SINGLE,
            maxDailyWei: FUEL_DAILY,
            coldWalletAddress: canonicalAddress(COLD),
            onMined: (deposit, wei, block, hash) => ledger.record(deposit, wei, block, hash),
            ...fuel,
          },
    now,
  });
  return { service, journal, feeWaits };
}

function sweep(n = 1, overrides: Partial<SweepRequest> = {}): SweepRequest {
  return {
    blockchainTransactionId: ID(n),
    depositIndex: 5,
    depositAddress: DEPOSIT.address,
    toHotWalletAddress: HOT.address,
    amount: '50',
    token: 'USDT',
    feeCapUsd: CAP,
    ...overrides,
  };
}

function refund(n = 2, overrides: Partial<RefundRequest> = {}): RefundRequest {
  return {
    blockchainTransactionId: ID(n),
    depositIndex: 5,
    depositAddress: DEPOSIT.address,
    toBuyerAddress: BUYER,
    amount: '10',
    token: 'USDT',
    refundType: 'EXCESS_REFUND',
    feeCapUsd: CAP,
    ...overrides,
  };
}

const sentTxs = () => chain.sent.map((raw) => Transaction.from(raw));
const fuelTxs = () =>
  sentTxs().filter((tx) => canonicalAddress(tx.from ?? '') === canonicalAddress(FUEL.address));
const distinctFuelSignatures = () => new Set(fuelTxs().map((tx) => tx.hash)).size;
const tokenTxs = () => sentTxs().filter((tx) => tx.data !== '0x');

async function reason(promise: Promise<unknown>): Promise<string | undefined> {
  const err = await promise.then(
    () => undefined,
    (e: unknown) => e as { reason?: string; code?: string },
  );
  return err?.reason ?? err?.code;
}

// Sweep at T142's prices: gas 46,201 → limit 50,822; tip 0.05 gwei, base 1 gwei.
const SWEEP_GAS_LIMIT = 50_822n;
const QUOTED_MAX_FEE = 2n * 1_000_000_000n + 50_000_000n; // 2 × base + tip
// What one fuel takes off the daily cap at these prices: value + its own worst-case fee.
const ONE_FUEL = SWEEP_GAS_LIMIT * QUOTED_MAX_FEE;
const ONE_FUEL_FEE = 21_000n * QUOTED_MAX_FEE;
const ONE_FUEL_OUTFLOW = ONE_FUEL + ONE_FUEL_FEE;

describe('fuel before a deposit send — exactly what it lacks, to the deposit itself', () => {
  it('an empty deposit: the sweep waits (FUEL_PENDING) and the fuel wallet signs exactly gasLimit × maxFeePerGas to it', async () => {
    const { service, journal } = build();
    expect(await reason(service.sweep(sweep()))).toBe('FUEL_PENDING');

    expect(tokenTxs()).toHaveLength(0); // nothing from the deposit yet
    const [fuel] = fuelTxs();
    expect(fuelTxs()).toHaveLength(1);
    expect(fuel.type).toBe(2);
    expect(fuel.chainId).toBe(11_155_111n);
    expect(canonicalAddress(fuel.to!)).toBe(canonicalAddress(DEPOSIT.address));
    expect(fuel.data).toBe('0x');
    expect(fuel.gasLimit).toBe(21_000n);
    expect(fuel.value).toBe(SWEEP_GAS_LIMIT * QUOTED_MAX_FEE);

    const record = journal.get(`fuel:${ID(1)}`)!;
    expect(record).toMatchObject({
      purpose: 'FUEL',
      signer: 'FUEL',
      from: canonicalAddress(FUEL.address),
      recipient: canonicalAddress(DEPOSIT.address),
      depositIndex: 5,
      contract: null,
      amountUnits: (SWEEP_GAS_LIMIT * QUOTED_MAX_FEE).toString(),
    });
    expect(journal.get(`transfer:${ID(1)}`)).toBeUndefined();
  });

  it('the fuel is journalled before its broadcast reaches a node', async () => {
    const { service } = build();
    let onDisk: string | undefined;
    chain.onSend = (raw) => {
      onDisk = new TxJournal(dir).findByHash(Transaction.from(raw).hash!)?.attempts[0].raw;
    };
    await reason(service.sweep(sweep()));
    expect(onDisk).toBe(chain.sent[0]);
  });

  it('the token send is signed only once the fuel is in a block — never before', async () => {
    const { service } = build();
    await reason(service.sweep(sweep()));
    const fuelHash = fuelTxs()[0].hash!.toLowerCase();

    // Still in the mempool: the sweep keeps waiting, nothing from the deposit.
    expect(await reason(service.sweep(sweep()))).toBe('FUEL_PENDING');
    expect(tokenTxs()).toHaveLength(0);

    let fuelMinedAtTokenBroadcast: boolean | undefined;
    chain.onSend = (raw) => {
      if (Transaction.from(raw).data !== '0x') {
        fuelMinedAtTokenBroadcast = chain.receipts.get(fuelHash)?.status === 1;
      }
    };
    chain.mine();
    const result = await service.sweep(sweep());
    expect(result.replayed).toBe(false);
    expect(fuelMinedAtTokenBroadcast).toBe(true);

    chain.mine();
    expect(chain.token(USDT, HOT.address)).toBe(1_000_000_000_000n + 50_000_000n);
    expect(chain.token(USDT, DEPOSIT.address)).toBe(0n);
  });

  it('a fuel mined in a block above the priced head is not counted yet', async () => {
    const { service, journal } = build();
    await reason(service.sweep(sweep()));
    chain.mine();
    const fuelBlock = BigInt(
      chain.receipts.get(journal.get(`fuel:${ID(1)}`)!.attempts[0].hash)!.blockNumber,
    );
    // A provider whose `latest` lags behind the block the receipt came from:
    // the balance read at that head does not show the fuel yet.
    chain.head = fuelBlock - 1n;
    chain.setEth(DEPOSIT.address, 0n);
    expect(await reason(service.sweep(sweep()))).toBe('FUEL_PENDING');
    expect(tokenTxs()).toHaveLength(0);
  });

  it('a deposit already holding part of the gas gets only the rest; one holding all of it gets none', async () => {
    const needs = SWEEP_GAS_LIMIT * QUOTED_MAX_FEE;
    chain.setEth(DEPOSIT.address, needs / 3n);
    const { service } = build();
    expect(await reason(service.sweep(sweep()))).toBe('FUEL_PENDING');
    expect(fuelTxs()[0].value).toBe(needs - needs / 3n);

    chain.setEth(DEPOSIT_B.address, needs);
    await service.sweep(sweep(2, { depositIndex: 6, depositAddress: DEPOSIT_B.address }));
    expect(fuelTxs()).toHaveLength(1); // none for B
  });

  it('a refund before the sweep is fuelled the same way, under the refund flow', async () => {
    const { service, journal } = build();
    expect(await reason(service.refund(refund()))).toBe('FUEL_PENDING');
    chain.mine();
    await service.refund(refund());
    chain.mine();
    expect(chain.token(USDT, BUYER)).toBe(10_000_000n);
    expect(journal.get(`fuel:${ID(2)}`)?.recipient).toBe(canonicalAddress(DEPOSIT.address));
  });

  it('the leftover stays on the deposit and is never more than the send budget minus what it burnt', async () => {
    const { service } = build();
    await reason(service.sweep(sweep()));
    chain.mine();
    await service.sweep(sweep());
    chain.mine();
    const fuel = fuelTxs()[0].value;
    const burnt = chain.receiptGasUsed * (chain.baseFee + chain.tip);
    expect(chain.ethOf(DEPOSIT.address)).toBe(fuel - burnt);
  });
});

describe('one fuel per request, ever', () => {
  it('a retry before the fuel lands signs no second fuel', async () => {
    const { service, journal } = build();
    for (let i = 0; i < 4; i++) expect(await reason(service.sweep(sweep()))).toBe('FUEL_PENDING');
    expect(distinctFuelSignatures()).toBe(1);
    expect(journal.all().filter((r) => r.purpose === 'FUEL')).toHaveLength(1);
  });

  it('two parallel calls with the same id sign one fuel', async () => {
    const { service } = build();
    const results = await Promise.all([
      reason(service.sweep(sweep())),
      reason(service.sweep(sweep())),
    ]);
    expect(results).toEqual(['FUEL_PENDING', 'FUEL_PENDING']);
    expect(distinctFuelSignatures()).toBe(1);
  });

  it('a restart finds the journalled fuel and signs no second one', async () => {
    await reason(build().service.sweep(sweep()));
    const restarted = build();
    expect(await reason(restarted.service.sweep(sweep()))).toBe('FUEL_PENDING');
    expect(distinctFuelSignatures()).toBe(1);
    chain.mine();
    await restarted.service.sweep(sweep());
    expect(tokenTxs()).toHaveLength(1);
  });

  it('a fuel that landed but is short of a later, higher fee is spent as it is — no top-up', async () => {
    const { service } = build();
    await reason(service.sweep(sweep()));
    chain.mine();
    chain.baseFee = 1_400_000_000n; // quoted 2.85 gwei now; the fuel carries 2.05
    await service.sweep(sweep());
    const [token] = tokenTxs();
    expect(token.maxFeePerGas).toBe(QUOTED_MAX_FEE); // ⌊fuel ÷ gasLimit⌋
    expect(distinctFuelSignatures()).toBe(1);
    chain.mine();
    expect(chain.token(USDT, DEPOSIT.address)).toBe(0n);
  });

  it('a fuel short of base fee + tip makes the send wait, still with no top-up', async () => {
    const { service } = build();
    await reason(service.sweep(sweep()));
    chain.mine();
    chain.baseFee = 2_100_000_000n; // base + tip 2.15 gwei > the 2.05 the fuel carries
    expect(await reason(service.sweep(sweep()))).toBe('FUEL_REQUIRED');
    expect(tokenTxs()).toHaveLength(0);
    expect(distinctFuelSignatures()).toBe(1);

    chain.baseFee = 1_000_000_000n; // the fee falls
    await service.sweep(sweep());
    expect(tokenTxs()).toHaveLength(1);
  });
});

describe('a fuel that reverted', () => {
  it('is not taken as delivered — the send waits even if the deposit could afford a lower fee', async () => {
    const needs = SWEEP_GAS_LIMIT * QUOTED_MAX_FEE;
    // Lacks 45 % (above a fuel's own fee); affords ~1.13 gwei on its own, above base + tip.
    chain.setEth(DEPOSIT.address, (needs * 55n) / 100n);
    chain.revertEthTo.add(canonicalAddress(DEPOSIT.address));
    const { service } = build();
    expect(await reason(service.sweep(sweep()))).toBe('FUEL_PENDING');
    chain.mine();
    expect(await reason(service.sweep(sweep()))).toBe('FUEL_REQUIRED');
    expect(tokenTxs()).toHaveLength(0);
    expect(distinctFuelSignatures()).toBe(1);
  });
});

describe('another request on the same deposit does not take this one’s fuel', () => {
  it('a refund arriving after a sweep’s fuel landed gets its own fuel; both sends go out', async () => {
    const { service } = build();
    const sweepFirst = sweep(1, { amount: '40' }); // leaves the refund's 10 on the deposit
    await reason(service.sweep(sweepFirst));
    chain.mine(); // the sweep's fuel is on the deposit; the sweep has not been asked again yet

    expect(await reason(service.refund(refund(2)))).toBe('FUEL_PENDING');
    expect(fuelTxs()).toHaveLength(2);
    chain.mine();

    await service.sweep(sweepFirst);
    // The sweep (nonce 0) and the refund (nonce 1) both find their own fuel.
    await service.refund(refund(2));
    chain.mine();
    expect(chain.token(USDT, BUYER)).toBe(10_000_000n);
    expect(chain.token(USDT, HOT.address)).toBe(1_000_000_000_000n + 40_000_000n);
    expect(distinctFuelSignatures()).toBe(2);
  });

  it('a deposit send already signed reserves its gas: a second request is fuelled for itself', async () => {
    chain.setEth(DEPOSIT.address, SWEEP_GAS_LIMIT * QUOTED_MAX_FEE); // enough for exactly one send
    const { service } = build();
    await service.refund(refund(2)); // signed, not mined: it holds that ETH back
    expect(fuelTxs()).toHaveLength(0);
    expect(await reason(service.sweep(sweep(1, { amount: '40' })))).toBe('FUEL_PENDING');
    expect(fuelTxs()[0].value).toBe(SWEEP_GAS_LIMIT * QUOTED_MAX_FEE);
  });
});

describe('the fee cap stops the fuel too (08 §3.7.5, §3.7.7)', () => {
  it.each([
    ['sweep', () => sweep()],
    ['refund', () => refund()],
  ] as const)(
    '%s above the cap: no fuel is sent, the send waits FEE_ABOVE_CAP and its wait clock runs',
    async (_flow, request) => {
      chain.baseFee = 5_000_000_000n;
      const { service, feeWaits } = build();
      const call = () =>
        _flow === 'sweep'
          ? service.sweep(request() as SweepRequest)
          : service.refund(request() as RefundRequest);
      expect(await reason(call())).toBe('FEE_ABOVE_CAP');
      expect(chain.sent).toHaveLength(0);
      clock += 60_000;
      expect(await reason(call())).toBe('FEE_ABOVE_CAP');
      expect(feeWaits.longestWaitSeconds(FEE_CAP_WAIT_REASONS)).toBe(60);
    },
  );

  it('the fuel send’s own share of the cap is checked too', async () => {
    // A small token send fits its share at 4 gwei; the 21,000-gas fuel does not fit its own.
    chain.estimate = 20_000n;
    chain.baseFee = 4_000_000_000n;
    const { service } = build();
    expect(await reason(service.sweep(sweep()))).toBe('FEE_ABOVE_CAP');
    expect(chain.sent).toHaveLength(0);
  });

  it.each([
    // Token share binds (2.86 gwei), the fuel's own share does not (3.15): fuel at 2 × base + tip.
    [1_420_000_000n, 2n * 1_420_000_000n + 50_000_000n],
    // The fuel's own share binds: ⌊fuel cap ÷ 21,000⌋.
    [1_600_000_000n, fuelCapWei(CAP_MICROS, 'SWEEP', ETH_USD) / 21_000n],
  ])('the fuel send is priced on its own share of the cap (base %s)', async (base, expected) => {
    chain.baseFee = base;
    const { service } = build();
    expect(await reason(service.sweep(sweep()))).toBe('FUEL_PENDING');
    const [fuel] = fuelTxs();
    expect(fuel.maxFeePerGas).toBe(expected);
    expect(21_000n * fuel.maxFeePerGas!).toBeLessThanOrEqual(
      fuelCapWei(CAP_MICROS, 'SWEEP', ETH_USD),
    );
  });

  it('fuel and token send together never cost more than the one 0.50 $ cap', async () => {
    const { service, journal } = build();
    await reason(service.sweep(sweep()));
    chain.mine();
    await service.sweep(sweep());
    const fuel = journal.get(`fuel:${ID(1)}`)!;
    const fuelTx = Transaction.from(fuel.attempts[0].raw);
    const wholeCap = (CAP_MICROS * ONE_ETH) / ETH_USD; // 0.50 $ in wei
    // ETH leaving the fuel wallet for this sweep, at worst: the fuel + the fuel send's fee.
    expect(fuelTx.value + 21_000n * fuelTx.maxFeePerGas!).toBeLessThanOrEqual(wholeCap);
    expect(BigInt(fuel.feeCapWei!)).toBe(fuelCapWei(CAP_MICROS, 'SWEEP', ETH_USD));
    expect(
      fuelCapWei(CAP_MICROS, 'SWEEP', ETH_USD) + capWeiPerTransaction(CAP_MICROS, 'SWEEP', ETH_USD),
    ).toBeLessThanOrEqual(wholeCap);
    expect(CAP_REFERENCE_GAS.SWEEP).toBe(67_201n);
  });
});

describe('fuel caps and the fuel wallet — a wait, never a refusal', () => {
  it('above the single-send cap: FUEL_LIMIT_REACHED, nothing signed', async () => {
    const { service } = build({ maxSingleWei: SWEEP_GAS_LIMIT * QUOTED_MAX_FEE - 1n });
    expect(await reason(service.sweep(sweep()))).toBe('FUEL_LIMIT_REACHED');
    expect(chain.sent).toHaveLength(0);
  });

  it('the 24-hour cap counts every fuel the journal signed, survives a restart and frees up after 24 h', async () => {
    const caps = { maxDailyWei: 2n * ONE_FUEL_OUTFLOW };
    const { service } = build(caps);
    expect(await reason(service.sweep(sweep(1)))).toBe('FUEL_PENDING');
    expect(
      await reason(service.sweep(sweep(2, { depositIndex: 6, depositAddress: DEPOSIT_B.address }))),
    ).toBe('FUEL_PENDING');
    const restarted = build(caps);
    expect(restarted.service.fuelSpentLast24hWei()).toBe(2n * ONE_FUEL_OUTFLOW);
    const third = wallet.derive(7);
    chain.setToken(USDT, third.address, 50_000_000n);
    const thirdSweep = sweep(3, { depositIndex: 7, depositAddress: third.address });
    expect(await reason(restarted.service.sweep(thirdSweep))).toBe('FUEL_LIMIT_REACHED');
    expect(distinctFuelSignatures()).toBe(2);

    clock += 24 * 60 * 60 * 1000 + 1;
    expect(await reason(restarted.service.sweep(thirdSweep))).toBe('FUEL_PENDING');
  });

  it.each([
    ['no fuel key', { key: null }],
    ['no single cap', { maxSingleWei: null }],
    ['no daily cap', { maxDailyWei: null }],
  ] as const)('%s: FUEL_REQUIRED, nothing signed (fail closed)', async (_label, fuel) => {
    const { service } = build(fuel);
    expect(await reason(service.sweep(sweep()))).toBe('FUEL_REQUIRED');
    expect(chain.sent).toHaveLength(0);
  });

  it('no fuel wallet at all: FUEL_REQUIRED', async () => {
    const { service } = build(null);
    expect(await reason(service.sweep(sweep()))).toBe('FUEL_REQUIRED');
    expect(chain.sent).toHaveLength(0);
  });

  it('a fuel wallet that cannot pay the fuel and its fee: FUEL_WALLET_LOW, nothing signed', async () => {
    chain.setEth(FUEL.address, SWEEP_GAS_LIMIT * QUOTED_MAX_FEE); // the fuel, but not its fee
    const { service } = build();
    expect(await reason(service.sweep(sweep()))).toBe('FUEL_WALLET_LOW');
    expect(chain.sent).toHaveLength(0);
  });

  it('the fuel wallet’s unmined fuel is not spent twice', async () => {
    const one = SWEEP_GAS_LIMIT * QUOTED_MAX_FEE + 21_000n * QUOTED_MAX_FEE;
    chain.setEth(FUEL.address, one + one / 2n); // one fuel and a half
    const { service } = build();
    expect(await reason(service.sweep(sweep(1)))).toBe('FUEL_PENDING');
    expect(
      await reason(service.sweep(sweep(2, { depositIndex: 6, depositAddress: DEPOSIT_B.address }))),
    ).toBe('FUEL_WALLET_LOW');
  });

  it('gas waits are published under the gas kind, not the fee-cap kind', async () => {
    const { service, feeWaits } = build({ key: null });
    await reason(service.sweep(sweep()));
    clock += 120_000;
    await reason(service.sweep(sweep()));
    expect(feeWaits.longestWaitSeconds(GAS_WAIT_REASONS)).toBe(120);
    expect(feeWaits.longestWaitSeconds(FEE_CAP_WAIT_REASONS)).toBe(0);
  });
});

describe('only the deposit the request names, holding what it sends', () => {
  it('an index that does not derive the named deposit: refused, no fuel', async () => {
    const { service } = build();
    expect(await reason(service.sweep(sweep(1, { depositIndex: 6 })))).toBe(
      'DEPOSIT_ADDRESS_MISMATCH',
    );
    expect(chain.sent).toHaveLength(0);
  });

  it('a deposit that does not hold the tokens is never fuelled (the simulation reverts first)', async () => {
    chain.setToken(USDT, DEPOSIT.address, 0n);
    const { service } = build();
    expect(await reason(service.sweep(sweep()))).toBe('TRANSFER_SIMULATION_REVERTED');
    expect(chain.sent).toHaveLength(0);
  });

  it('a refund to a blacklisted buyer is never fuelled', async () => {
    chain.blacklist.add(canonicalAddress(BUYER));
    const { service } = build();
    expect(await reason(service.refund(refund()))).toBe('RECIPIENT_BLACKLISTED');
    expect(chain.sent).toHaveLength(0);
  });

  it('an index that derives a platform wallet is never fuelled (signer policy)', async () => {
    chain.setEth(HOT.address, 0n);
    const { service } = build();
    expect(
      await reason(service.sweep(sweep(1, { depositIndex: 30, depositAddress: HOT.address }))),
    ).toBe('SIGNER_POLICY_REJECTED');
    expect(fuelTxs()).toHaveLength(0);
  });

  it('a deposit whose journal is behind the chain is not fuelled', async () => {
    chain.foreignSend(DEPOSIT.address);
    const { service } = build();
    expect(await reason(service.sweep(sweep()))).toBe('JOURNAL_BEHIND_CHAIN');
    expect(chain.sent).toHaveLength(0);
  });

  it('a fuel wallet whose journal is behind the chain signs nothing', async () => {
    chain.foreignSend(FUEL.address);
    const { service } = build();
    expect(await reason(service.sweep(sweep()))).toBe('JOURNAL_BEHIND_CHAIN');
    expect(chain.sent).toHaveLength(0);
  });
});

describe('keeper and reconciliation', () => {
  it('re-broadcasts a fuel whose broadcast failed, with the same bytes', async () => {
    chain.sendFailures.push(new Error('connection reset'));
    const { service } = build();
    expect(await reason(service.sweep(sweep()))).toBe('FUEL_PENDING');
    expect(chain.mempool.size).toBe(0);
    await service.keepAll();
    expect(chain.sent[1]).toBe(chain.sent[0]);
    expect(chain.mempool.size).toBe(1);
  });

  it('bumps a stuck fuel at the same nonce, same value, within its share of the cap', async () => {
    const { service, journal } = build();
    await reason(service.sweep(sweep()));
    chain.baseFee = 2_500_000_000n; // the fuel's 2.05 gwei no longer gets in
    chain.mine();
    clock += 300_000;
    await service.keepAll();
    const record = journal.get(`fuel:${ID(1)}`)!;
    expect(record.attempts).toHaveLength(2);
    const [first, bumped] = record.attempts.map((a) => Transaction.from(a.raw));
    expect(bumped.nonce).toBe(first.nonce);
    expect(bumped.value).toBe(first.value);
    expect(canonicalAddress(bumped.to!)).toBe(canonicalAddress(DEPOSIT.address));
    expect(bumped.maxFeePerGas! * 10n).toBeGreaterThanOrEqual(first.maxFeePerGas! * 11n);
    expect(21_000n * bumped.maxFeePerGas!).toBeLessThanOrEqual(BigInt(record.feeCapWei!));
    // The bump is not a second fuel: one value, and the fee of the latest attempt only.
    expect(service.fuelSpentLast24hWei()).toBe(first.value + 21_000n * bumped.maxFeePerGas!);
    chain.mine();
    expect(chain.ethOf(DEPOSIT.address)).toBe(first.value);
  });

  it('a fee bump is not counted twice against the 24-hour fuel cap', async () => {
    // Room for exactly this fuel at its highest possible fee (its share of the cap).
    const room = ONE_FUEL + fuelCapWei(CAP_MICROS, 'SWEEP', ETH_USD);
    const { service, journal } = build({ maxDailyWei: room });
    await reason(service.sweep(sweep()));
    chain.baseFee = 2_500_000_000n;
    chain.mine();
    clock += 300_000;
    await service.keepAll();
    expect(journal.get(`fuel:${ID(1)}`)!.attempts).toHaveLength(2);
  });

  it('feeds the FuelLedger with the fuel’s hash and block when it lands, and once only', async () => {
    const { service, journal } = build();
    await reason(service.sweep(sweep()));
    chain.mine();
    await service.keepAll(); // in a block → recorded
    chain.finalized = chain.head;
    await service.keepAll(); // final → noted again: still once (keyed by hash)
    const fuel = journal.get(`fuel:${ID(1)}`)!;
    expect(fuel.mined?.final).toBe(true);
    const block = BigInt(fuel.mined!.blockNumber);
    expect(ledger.sentBetween(DEPOSIT.address, block - 1n, block)).toBe(BigInt(fuel.amountUnits));
    await service.sweep(sweep());
    expect(ledger.sentBetween(DEPOSIT.address, block - 1n, block)).toBe(BigInt(fuel.amountUnits));
  });

  it('a restart rebuilds the FuelLedger from the journal', async () => {
    const { service, journal } = build();
    await reason(service.sweep(sweep()));
    chain.mine();
    await service.keepAll();
    const fuel = journal.get(`fuel:${ID(1)}`)!;
    const fresh = new FuelLedger();
    expect(seedFuelLedger(new TxJournal(dir), fresh)).toBe(1);
    const block = BigInt(fuel.mined!.blockNumber);
    expect(fresh.sentBetween(DEPOSIT.address, block - 1n, block)).toBe(BigInt(fuel.amountUnits));
  });

  it('a fuel is reported by the status endpoint like any journalled send', async () => {
    const { service, journal } = build();
    await reason(service.sweep(sweep()));
    chain.mine();
    const hash = journal.get(`fuel:${ID(1)}`)!.attempts[0].hash;
    expect(await service.status(hash)).toMatchObject({ txHash: hash, contractRet: 'SUCCESS' });
  });

  it('the fuel wallet address comes from its key, never from configuration', () => {
    expect(build().service.fuelWalletAddress()).toBe(canonicalAddress(FUEL.address));
    expect(build({ key: null }).service.fuelWalletAddress()).toBeNull();
  });

  it('the token send that follows its fuel moves exactly the request', async () => {
    const { service } = build();
    await reason(service.sweep(sweep()));
    chain.mine();
    await service.sweep(sweep());
    expect(decodeTransferCall(tokenTxs()[0].data)).toEqual({
      to: canonicalAddress(HOT.address),
      amountUnits: 50_000_000n,
    });
  });
});

/*
 * T153 doğrulaması — bulgular (yönetici kararları). Each test names the
 * finding it pins; every one was shown red against the code it guards.
 */

describe('bulgu a — the fuel’s own fee is under the daily cap; no fuel smaller than its fee', () => {
  it('40 small fuels: value + fee hits the daily cap where value alone would not', async () => {
    const small = ONE_FUEL_FEE + 1n; // the smallest fuel that is sent: just above its own fee
    const count = 40;
    const { service } = build({ maxDailyWei: BigInt(count) * small }); // all 40 values would fit
    const reasons: Array<string | undefined> = [];
    for (let i = 0; i < count; i++) {
      const deposit = wallet.derive(100 + i);
      chain.setToken(USDT, deposit.address, 50_000_000n);
      chain.setEth(deposit.address, ONE_FUEL - small);
      reasons.push(
        await reason(
          service.sweep(sweep(100 + i, { depositIndex: 100 + i, depositAddress: deposit.address })),
        ),
      );
    }
    const fuels = fuelTxs();
    for (const fuel of fuels) expect(fuel.value).toBe(small);
    const outflow = fuels.reduce((sum, tx) => sum + tx.value + 21_000n * tx.maxFeePerGas!, 0n);
    expect(outflow).toBeLessThanOrEqual(BigInt(count) * small);
    expect(fuels).toHaveLength(Math.floor((count * Number(small)) / Number(small + ONE_FUEL_FEE)));
    expect(reasons.filter((r) => r === 'FUEL_LIMIT_REACHED')).toHaveLength(count - fuels.length);
    expect(service.fuelSpentLast24hWei()).toBe(outflow);
  });

  it('a deposit lacking less than a fuel’s own fee gets no fuel — it signs at the fee it affords', async () => {
    chain.setEth(DEPOSIT.address, ONE_FUEL - 1n); // the validator's probe: 1 wei short
    const { service } = build();
    const result = await service.sweep(sweep());
    expect(result.replayed).toBe(false);
    expect(fuelTxs()).toHaveLength(0);
    const [token] = tokenTxs();
    expect(token.maxFeePerGas).toBe((ONE_FUEL - 1n) / SWEEP_GAS_LIMIT);
    expect(service.fuelSpentLast24hWei()).toBe(0n);
  });

  it('…and waits FUEL_REQUIRED, still with no fuel, when that fee is below base + tip', async () => {
    chain.tip = 500_000_000n; // quoted 2.5 gwei; a fuel's own fee 21,000 × 2.5 gwei
    const need = SWEEP_GAS_LIMIT * 2_500_000_000n;
    const fuelFee = 21_000n * 2_500_000_000n;
    chain.setEth(DEPOSIT.address, need - fuelFee + 1n); // lacks one wei less than a fuel's fee
    const { service } = build();
    expect(await reason(service.sweep(sweep()))).toBe('FUEL_REQUIRED'); // affords ~1.47 < 1.5 gwei
    expect(chain.sent).toHaveLength(0);

    chain.baseFee = 800_000_000n; // base + tip 1.3 gwei: the deposit's own ETH is enough now
    await service.sweep(sweep());
    expect(tokenTxs()).toHaveLength(1);
    expect(fuelTxs()).toHaveLength(0);
  });
});

describe('bulgu b — the signer is given what the send lacks, not its whole budget', () => {
  it('maxForSendWei = gasLimit × maxFeePerGas − the deposit’s ETH that is this request’s', async () => {
    chain.setEth(DEPOSIT.address, ONE_FUEL / 3n);
    const { service } = build();
    expect(await reason(service.sweep(sweep()))).toBe('FUEL_PENDING');
    const [intent] = fuelIntents();
    expect(intent.maxForSendWei).toBe(ONE_FUEL - ONE_FUEL / 3n);
    expect(intent.maxForSendWei).toBe(fuelTxs()[0].value);
  });

  it('another request’s fuel in the balance is not this send’s: the bound excludes it', async () => {
    const { service } = build();
    await reason(service.sweep(sweep(1, { amount: '40' })));
    chain.mine(); // the sweep's fuel is on the deposit, its sweep not signed yet
    expect(await reason(service.refund(refund(2)))).toBe('FUEL_PENDING');
    const intents = fuelIntents();
    expect(intents).toHaveLength(2);
    expect(intents[1].maxForSendWei).toBe(ONE_FUEL);
  });
});

describe('bulgu c — money test gaps', () => {
  it('M35: when the token share of the cap binds, the fuel is the capped budget, not the quoted one', async () => {
    chain.baseFee = 1_420_000_000n; // quoted 2.89 gwei; the token share allows ~2.86
    const ceiling = feeCeilingPerGas(
      capWeiPerTransaction(CAP_MICROS, 'SWEEP', ETH_USD),
      SWEEP_GAS_LIMIT,
    );
    const quoted = 2n * 1_420_000_000n + 50_000_000n;
    expect(ceiling).toBeLessThan(quoted);
    const { service } = build();
    expect(await reason(service.sweep(sweep()))).toBe('FUEL_PENDING');
    expect(fuelTxs()[0].value).toBe(SWEEP_GAS_LIMIT * ceiling);
  });

  describe('M36: every fuel wait reaches the backend as 503 { deferred: true }, not only its code', () => {
    let server: Server | null = null;
    afterEach(async () => {
      const s = server;
      server = null;
      if (s) await new Promise((resolve) => s.close(resolve));
    });

    async function postSweep(service: TransferService) {
      const app = createApp({
        internalKey: 'k'.repeat(64),
        chainId: 11155111,
        wallet,
        finality: () => ({ state: 'ok', snapshot: null, ageSeconds: null, lastError: null }),
        transfers: service,
        feeEstimator: {} as EthFeeEstimationService,
      });
      const s = app.listen(0, '127.0.0.1');
      server = s;
      await new Promise((resolve) => s.once('listening', resolve));
      const res = await fetch(
        `http://127.0.0.1:${(s.address() as AddressInfo).port}/api/transfer/sweep`,
        {
          method: 'POST',
          headers: { 'content-type': 'application/json', 'x-internal-key': 'k'.repeat(64) },
          body: JSON.stringify({ network: 'ETHEREUM', ...sweep() }),
        },
      );
      return { status: res.status, body: (await res.json()) as Record<string, unknown> };
    }

    it.each<[string, () => Partial<FuelDeps> | null, () => void]>([
      ['FUEL_PENDING', () => ({}), () => {}],
      ['FUEL_REQUIRED', () => ({ key: null }), () => {}],
      ['FUEL_LIMIT_REACHED', () => ({ maxSingleWei: ONE_FUEL - 1n }), () => {}],
      ['FUEL_LIMIT_REACHED', () => ({ maxDailyWei: ONE_FUEL_OUTFLOW - 1n }), () => {}],
      ['FUEL_WALLET_LOW', () => ({}), () => chain.setEth(FUEL.address, ONE_FUEL)],
      [
        'FEE_ABOVE_CAP',
        () => ({}),
        () => {
          chain.estimate = 20_000n; // the fuel's own share binds, the token's does not
          chain.baseFee = 4_000_000_000n;
        },
      ],
    ])('%s', async (expected, fuel, arrange) => {
      arrange();
      const { status, body } = await postSweep(build(fuel()).service);
      expect(status).toBe(503);
      expect(body).toMatchObject({ error: expected, deferred: true, retryable: true });
      if (expected !== 'FUEL_PENDING') expect(fuelTxs()).toHaveLength(0);
    });
  });

  it('M44: another request’s fuel not yet in a block is not reserved — it is not in the balance', async () => {
    const { service } = build();
    expect(await reason(service.sweep(sweep(1, { amount: '40' })))).toBe('FUEL_PENDING');
    // Sweep's fuel still in the mempool; the deposit holds half a send's gas of its own.
    chain.setEth(DEPOSIT.address, ONE_FUEL / 2n);
    expect(await reason(service.refund(refund(2)))).toBe('FUEL_PENDING');
    expect(fuelTxs()[1].value).toBe(ONE_FUEL - ONE_FUEL / 2n);
  });
});

describe('bulgu d — small test gaps', () => {
  it('M40: the 24-hour window — a fuel signed exactly 24 h ago still counts, one ms later it does not', async () => {
    const { service } = build({ maxDailyWei: ONE_FUEL_OUTFLOW });
    expect(await reason(service.sweep(sweep(1)))).toBe('FUEL_PENDING');
    const second = sweep(2, { depositIndex: 6, depositAddress: DEPOSIT_B.address });
    expect(await reason(service.sweep(second))).toBe('FUEL_LIMIT_REACHED');
    clock += 24 * 60 * 60 * 1000;
    expect(await reason(service.sweep(second))).toBe('FUEL_LIMIT_REACHED');
    clock += 1;
    expect(await reason(service.sweep(second))).toBe('FUEL_PENDING');
  });

  it('M27/M29: a reverted fuel never reaches the FuelLedger — neither live nor rebuilt at startup', async () => {
    chain.revertEthTo.add(canonicalAddress(DEPOSIT.address));
    const { service, journal } = build();
    expect(await reason(service.sweep(sweep()))).toBe('FUEL_PENDING');
    chain.mine();
    expect(await reason(service.sweep(sweep()))).toBe('FUEL_REQUIRED'); // fuelFor sees the revert
    await service.keepAll(); // the keeper records it as mined, status 0
    const fuel = journal.get(`fuel:${ID(1)}`)!;
    expect(fuel.mined?.status).toBe(0);
    expect(ledger.sentBetween(DEPOSIT.address, 0n, chain.head + 1n)).toBe(0n);
    const fresh = new FuelLedger();
    expect(seedFuelLedger(new TxJournal(dir), fresh)).toBe(0);
    expect(fresh.sentBetween(DEPOSIT.address, 0n, chain.head + 1n)).toBe(0n);
  });

  it('M02: the single-send cap is inclusive — a fuel of exactly the cap is sent', async () => {
    const { service } = build({ maxSingleWei: ONE_FUEL });
    expect(await reason(service.sweep(sweep()))).toBe('FUEL_PENDING');
    expect(fuelTxs()[0].value).toBe(ONE_FUEL);
  });
});

describe('bulgu e — an id that holds only its fuel is taken', () => {
  it.each<[string, (s: TransferService) => Promise<unknown>]>([
    [
      'another deposit',
      (s) => s.sweep(sweep(1, { depositIndex: 6, depositAddress: DEPOSIT_B.address })),
    ],
    ['another index for the same deposit', (s) => s.sweep(sweep(1, { depositIndex: 6 }))],
    ['another amount', (s) => s.sweep(sweep(1, { amount: '40' }))],
    ['another flow on the same deposit', (s) => s.refund(refund(1))],
  ])('%s → IDEMPOTENCY_CONFLICT (refused for good), no second fuel', async (_label, ask) => {
    const { service } = build();
    expect(await reason(service.sweep(sweep(1)))).toBe('FUEL_PENDING');
    const err = await ask(service).then(
      () => undefined,
      (e: unknown) => e as { code?: string; retryable?: boolean },
    );
    expect(err).toMatchObject({ code: 'IDEMPOTENCY_CONFLICT', retryable: false });
    // After a restart too: the fuel record keeps the request it was sent for.
    const err2 = await ask(build().service).then(
      () => undefined,
      (e: unknown) => e as { code?: string },
    );
    expect(err2?.code).toBe('IDEMPOTENCY_CONFLICT');
    expect(distinctFuelSignatures()).toBe(1);
    // The same request is still the fuel's own: it keeps waiting for it.
    expect(await reason(service.sweep(sweep(1)))).toBe('FUEL_PENDING');
  });
});
