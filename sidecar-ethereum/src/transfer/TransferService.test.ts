import { mkdtempSync, readdirSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { Transaction } from 'ethers';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { canonicalAddress } from '../address/canonicalAddress.js';
import { RpcError } from '../rpc/RpcClient.js';
import { FakeEthereumChain } from '../testing/FakeEthereumChain.js';
import { EthereumHdWallet } from '../wallet/HdWalletService.js';
import { MAINNET_TOKEN_CONTRACTS } from '../network.js';
import { decodeTransferCall } from './erc20.js';
import { FeeWaitTracker } from './FeeWaitTracker.js';
import { TransferDeferredError } from './FeePolicy.js';
import { TransferGuard } from './TransferGuard.js';
import { TransferService, type PayoutRequest } from './TransferService.js';
import { TxJournal } from './TxJournal.js';

/**
 * T152 — the Ethereum send path against an in-memory chain. The money rules
 * (08 §3.7.6, §3.7.7) each have a test here: one nonce per id, bytes on disk
 * before broadcast, retry = same bytes, fee bump = same nonce, foreign use of
 * the nonce = conflict and nothing re-signed, right recipient / amount /
 * chain, USDT switches and the recipient blacklist, the fee cap and its wait.
 */

const MNEMONIC = `${'abandon '.repeat(11)}about`;
const wallet = new EthereumHdWallet(MNEMONIC);
const HOT = wallet.deriveSigner(30);
const COLD = wallet.derive(31).address;
const DEPOSIT = wallet.derive(5);
const USDT = canonicalAddress(MAINNET_TOKEN_CONTRACTS.USDT);
const USDC = canonicalAddress(MAINNET_TOKEN_CONTRACTS.USDC);
const SELLER = '0x1111111111111111111111111111111111111111';
const BUYER = '0x2222222222222222222222222222222222222222';
const ETH_USD = 2_364_480_000n; // 2,364.48 $ in micro-dollars (T142's price)

const ID = (n: number) => `00000000-0000-0000-0000-${n.toString().padStart(12, '0')}`;

let dir: string;
let chain: FakeEthereumChain;
let clock: number;
let price: bigint | null;

beforeEach(() => {
  dir = mkdtempSync(join(tmpdir(), 'eth-transfer-'));
  chain = new FakeEthereumChain(USDT, USDC);
  chain.setToken(USDT, HOT.address, 1_000_000_000_000n);
  chain.setEth(HOT.address, 10n ** 18n);
  clock = 1_800_000_000_000;
  price = ETH_USD;
});

afterEach(() => rmSync(dir, { recursive: true, force: true }));

function build(
  overrides: Partial<{
    maxSingle: bigint | null;
    maxDaily: bigint | null;
    hotKey: string | null;
  }> = {},
) {
  const now = () => clock;
  const journal = new TxJournal(dir);
  const service = new TransferService({
    rpc: chain.rpc,
    journal,
    feeWaits: new FeeWaitTracker(dir, now),
    guard: new TransferGuard({
      rpc: chain.rpc,
      hotWalletAddress: canonicalAddress(HOT.address),
      coldWalletAddress: canonicalAddress(COLD),
      tokenContracts: [USDT, USDC],
      maxSingleTransferUnits: 'maxSingle' in overrides ? overrides.maxSingle! : 5_000_000_000n,
      maxDailyOutflowUnits: 'maxDaily' in overrides ? overrides.maxDaily! : 20_000_000_000n,
      now,
    }),
    wallet,
    policy: {
      chainId: 11_155_111n,
      paymentToken: USDT,
      wrongTokenRefundToken: USDC,
      maxTokenGasLimit: 150_000n,
    },
    hotWalletKey: 'hotKey' in overrides ? overrides.hotKey! : HOT.privateKey,
    hotWalletAddress: canonicalAddress(HOT.address),
    tokenContracts: { USDT, USDC },
    prices: { ethUsdMicros: () => Promise.resolve(price) },
    finalizedHead: () => chain.finalized,
    bumpAfterMs: 300_000,
    conflictConfirmMs: 30_000,
    now,
  });
  return { service, journal };
}

function payout(n = 1, overrides: Partial<PayoutRequest> = {}): PayoutRequest {
  return {
    blockchainTransactionId: ID(n),
    toAddress: SELLER,
    amount: '12.345678',
    token: 'USDT',
    feeCapUsd: '0.50',
    ...overrides,
  };
}

function finalizeAll(): void {
  chain.finalized = chain.head;
}

describe('payout — what gets signed', () => {
  it('signs a type-2 USDT transfer on the configured chain with the exact recipient and 6-decimal amount', async () => {
    const { service } = build();
    const result = await service.payout(payout());

    const tx = Transaction.from(chain.sent[0]);
    expect(tx.type).toBe(2);
    expect(tx.chainId).toBe(11_155_111n);
    expect(canonicalAddress(tx.to!)).toBe(USDT);
    expect(tx.value).toBe(0n);
    expect(canonicalAddress(tx.from!)).toBe(canonicalAddress(HOT.address));
    expect(decodeTransferCall(tx.data)).toEqual({ to: SELLER, amountUnits: 12_345_678n });
    expect(tx.nonce).toBe(0);
    expect(result).toEqual({ txHash: tx.hash!.toLowerCase(), nonce: 0, replayed: false });
  });

  it.each([
    ['0.000001', 1n],
    ['1', 1_000_000n],
    ['5000', 5_000_000_000n],
  ])('amount %s → %s raw units', async (amount, units) => {
    const { service } = build();
    await service.payout(payout(1, { amount }));
    expect(decodeTransferCall(Transaction.from(chain.sent[0]).data)?.amountUnits).toBe(units);
  });

  it.each(['1.0000001', '1e3', '-1', '0', '0.000000', ' 1'])(
    'refuses amount %j, signs nothing',
    async (amount) => {
      const { service } = build();
      await expect(service.payout(payout(1, { amount }))).rejects.toMatchObject({
        code: 'INVALID_TRANSFER_AMOUNT',
      });
      expect(chain.sent).toHaveLength(0);
    },
  );

  it('refuses USDC on a payout (USDC only for a wrong-token refund)', async () => {
    const { service } = build();
    await expect(service.payout(payout(1, { token: 'USDC' }))).rejects.toMatchObject({
      code: 'TOKEN_NOT_ALLOWED',
    });
    expect(chain.sent).toHaveLength(0);
  });

  it('refuses without a provisioned hot key', async () => {
    const { service } = build({ hotKey: null });
    await expect(service.payout(payout())).rejects.toMatchObject({
      code: 'HOT_WALLET_NOT_CONFIGURED',
    });
  });
});

describe('double payment is impossible (08 §3.7.6)', () => {
  it('journals the signed bytes BEFORE the broadcast reaches a node', async () => {
    const { service } = build();
    let onDiskAtBroadcast: string | undefined;
    chain.onSend = (raw) => {
      onDiskAtBroadcast = new TxJournal(dir).findByHash(Transaction.from(raw).hash!)?.attempts[0]
        .raw;
    };
    await service.payout(payout());
    expect(onDiskAtBroadcast).toBe(chain.sent[0]);
  });

  it('a second call with the same id signs nothing new and re-broadcasts the SAME bytes', async () => {
    const { service } = build();
    const first = await service.payout(payout());
    const second = await service.payout(payout());

    expect(second).toEqual({ txHash: first.txHash, nonce: 0, replayed: true });
    expect(chain.sent).toHaveLength(2);
    expect(chain.sent[1]).toBe(chain.sent[0]);
    chain.mine();
    expect(chain.token(USDT, SELLER)).toBe(12_345_678n);
  });

  it('the same id with another recipient or amount is refused (IDEMPOTENCY_CONFLICT)', async () => {
    const { service } = build();
    await service.payout(payout());
    await expect(service.payout(payout(1, { toAddress: BUYER }))).rejects.toMatchObject({
      code: 'IDEMPOTENCY_CONFLICT',
    });
    await expect(service.payout(payout(1, { amount: '12.345679' }))).rejects.toMatchObject({
      code: 'IDEMPOTENCY_CONFLICT',
    });
    expect(chain.sent).toHaveLength(1);
  });

  it('concurrent calls with one id produce one signature', async () => {
    const { service } = build();
    const results = await Promise.all([
      service.payout(payout()),
      service.payout(payout()),
      service.payout(payout()),
    ]);
    expect(new Set(results.map((r) => r.txHash)).size).toBe(1);
    expect(new Set(chain.sent).size).toBe(1);
    expect(results.filter((r) => !r.replayed)).toHaveLength(1);
  });

  it('concurrent payouts with different ids get consecutive nonces from one place', async () => {
    const { service } = build();
    const results = await Promise.all([1, 2, 3].map((n) => service.payout(payout(n))));
    expect(results.map((r) => r.nonce).sort()).toEqual([0, 1, 2]);
    chain.mine();
    expect(chain.token(USDT, SELLER)).toBe(3n * 12_345_678n);
  });

  it('a restart (new process, same journal) re-sends the SAME bytes, never a new nonce', async () => {
    const first = build();
    chain.sendFailures.push(new RpcError('fake transport failure', 'transport', 'fake'));
    const signed = await first.service.payout(payout());
    expect(chain.mempool.size).toBe(0);

    const restarted = build();
    expect(await restarted.service.payout(payout())).toMatchObject({
      txHash: signed.txHash,
      replayed: true,
    });
    await restarted.service.keepAll();
    expect(new Set(chain.sent)).toEqual(new Set([chain.sent[0]]));
    chain.mine();
    expect(chain.token(USDT, SELLER)).toBe(12_345_678n);
  });

  it('a failed broadcast still answers the hash; the keeper delivers the same bytes', async () => {
    const { service } = build();
    chain.sendFailures.push(new RpcError('insufficient funds for gas', 'rpc', 'fake'));
    const result = await service.payout(payout());
    expect(chain.mempool.size).toBe(0);

    await service.keepAll();
    expect(chain.sent[1]).toBe(chain.sent[0]);
    chain.mine();
    expect(chain.receipts.get(result.txHash)?.status).toBe(1);
  });

  it('"already known" from a node counts as accepted', async () => {
    const { service, journal } = build();
    chain.sendFailures.push(new RpcError('already known', 'rpc', 'fake'));
    await service.payout(payout());
    expect(journal.get(`transfer:${ID(1)}`)?.lastBroadcastError).toBeNull();
  });

  it('nonce = max(chain count, journal highest + 1): a pending journalled send is not reused', async () => {
    const { service } = build();
    await service.payout(payout(1)); // nonce 0, pending (not mined)
    const second = await service.payout(payout(2));
    expect(second.nonce).toBe(1);
  });

  it('a nonce used outside the journal moves the next send past it', async () => {
    chain.foreignSend(HOT.address);
    chain.foreignSend(HOT.address);
    const { service } = build();
    expect((await service.payout(payout())).nonce).toBe(2);
  });
});

describe('keeper — fee bump, finality, conflicts', () => {
  it('bumps a head-of-line send after 5 min with the SAME nonce and both fees +10 % or more; only one lands', async () => {
    const { service, journal } = build();
    const result = await service.payout(payout());
    const original = Transaction.from(chain.sent[0]);

    chain.baseFee = 1_300_000_000n;
    clock += 300_000;
    await service.keepAll();

    const record = journal.get(`transfer:${ID(1)}`)!;
    expect(record.attempts).toHaveLength(2);
    const bumped = Transaction.from(record.attempts[1].raw);
    expect(bumped.nonce).toBe(original.nonce);
    expect(bumped.maxFeePerGas! * 10n).toBeGreaterThanOrEqual(original.maxFeePerGas! * 11n);
    expect(bumped.maxPriorityFeePerGas! * 10n).toBeGreaterThanOrEqual(
      original.maxPriorityFeePerGas! * 11n,
    );
    expect(decodeTransferCall(bumped.data)).toEqual(decodeTransferCall(original.data));

    chain.mine();
    expect(chain.token(USDT, SELLER)).toBe(12_345_678n);
    finalizeAll();
    // Asked by the ORIGINAL hash, the status names the attempt that is in the block.
    const status = await service.status(result.txHash);
    expect(status).toMatchObject({
      txHash: bumped.hash!.toLowerCase(),
      contractRet: 'SUCCESS',
      finalized: true,
    });
  });

  it('when the OLD attempt is the one mined, every hash of the record reports it and the record settles', async () => {
    const { service, journal } = build();
    const first = await service.payout(payout());
    chain.baseFee = 1_300_000_000n;
    clock += 300_000;
    chain.sendFailures.push(new RpcError('fake transport failure', 'transport', 'fake'));
    await service.keepAll(); // bump signed, its broadcast lost
    const bumpHash = journal.get(`transfer:${ID(1)}`)!.attempts[1].hash;
    chain.mine(); // only the first attempt is in the mempool
    finalizeAll();

    expect(await service.status(bumpHash)).toMatchObject({
      txHash: first.txHash,
      finalized: true,
      contractRet: 'SUCCESS',
    });
    await service.keepAll();
    expect(journal.get(`transfer:${ID(1)}`)?.mined).toMatchObject({
      hash: first.txHash,
      final: true,
    });
    expect(chain.token(USDT, SELLER)).toBe(12_345_678n);
  });

  it('a capped send is never bumped above its cap — it waits instead', async () => {
    const { service, journal } = build();
    await service.payout(payout());
    const cap = BigInt(journal.get(`transfer:${ID(1)}`)!.feeCapWeiPerGas!);
    // Push the first attempt to the cap, then ask for a bump.
    chain.baseFee = cap; // 2 × base + tip is far above the cap now
    clock += 300_000;
    await service.keepAll(); // first bump clamps to the cap
    clock += 300_000;
    await service.keepAll(); // nothing above the cap is possible
    const attempts = journal.get(`transfer:${ID(1)}`)!.attempts;
    // Exactly one bump, clamped to the cap (not refused by the signer, not above it).
    expect(attempts).toHaveLength(2);
    expect(attempts[1].maxFeePerGas).toBe(cap.toString());
    expect(chain.mempool.size).toBe(1);
  });

  it('marks the record final once its block is at or below finalized and still canonical', async () => {
    const { service, journal } = build();
    await service.payout(payout());
    chain.mine();
    await service.keepAll();
    expect(journal.get(`transfer:${ID(1)}`)?.mined?.final).toBe(false);
    finalizeAll();
    await service.keepAll();
    expect(journal.get(`transfer:${ID(1)}`)?.mined?.final).toBe(true);
    expect(journal.unsettled()).toHaveLength(0);
  });

  it('a nonce used by a transaction the journal does not know → conflict after a second look; nothing re-signed', async () => {
    const { service, journal } = build();
    chain.sendFailures.push(new RpcError('fake transport failure', 'transport', 'fake'));
    const result = await service.payout(payout());
    chain.foreignSend(HOT.address); // someone else used nonce 0

    await service.keepAll();
    expect(journal.get(`transfer:${ID(1)}`)?.conflict).toBeNull();
    clock += 30_000;
    await service.keepAll();
    expect(journal.get(`transfer:${ID(1)}`)?.conflict).not.toBeNull();
    expect(service.nonceConflictCount()).toBe(1);

    const sentBefore = chain.sent.length;
    await service.keepAll();
    await expect(service.payout(payout())).rejects.toMatchObject({
      code: 'TRANSFER_NONCE_CONFLICT',
    });
    await expect(service.status(result.txHash)).rejects.toMatchObject({
      code: 'TRANSFER_NONCE_CONFLICT',
    });
    expect(chain.sent.length).toBe(sentBefore);
    expect(chain.token(USDT, SELLER)).toBe(0n);
  });

  it('its own mined attempt is never mistaken for a conflict', async () => {
    const { service, journal } = build();
    await service.payout(payout());
    chain.mine();
    clock += 60_000;
    await service.keepAll();
    await service.keepAll();
    expect(journal.get(`transfer:${ID(1)}`)?.conflict).toBeNull();
  });
});

describe('status (GET /api/transfer/status/:txHash)', () => {
  it('pending → confirmations -1, not finalized', async () => {
    const { service } = build();
    const { txHash } = await service.payout(payout());
    expect(await service.status(txHash)).toEqual({
      txHash,
      blockNumber: null,
      contractRet: null,
      confirmations: -1,
      finalized: false,
    });
  });

  it('mined above finalized → not final; at finalized → SUCCESS, final', async () => {
    const { service } = build();
    const { txHash } = await service.payout(payout());
    chain.mine();
    expect(await service.status(txHash)).toMatchObject({
      contractRet: 'SUCCESS',
      finalized: false,
      confirmations: 0,
    });
    finalizeAll();
    expect(await service.status(txHash)).toMatchObject({
      contractRet: 'SUCCESS',
      finalized: true,
      confirmations: 1,
    });
  });

  it('a block whose hash changed under finalized is not final', async () => {
    const { service } = build();
    const { txHash } = await service.payout(payout());
    chain.mine();
    finalizeAll();
    chain.replaceBlock(chain.head);
    expect(await service.status(txHash)).toMatchObject({ finalized: false });
  });

  it('a reverted transfer reports REVERT', async () => {
    const { service } = build();
    const { txHash } = await service.payout(payout());
    chain.setToken(USDT, HOT.address, 0n); // balance gone between signing and mining
    chain.mine();
    finalizeAll();
    expect(await service.status(txHash)).toMatchObject({ contractRet: 'REVERT', finalized: true });
  });

  it('an unknown hash is TRANSFER_NOT_FOUND', async () => {
    const { service } = build();
    await expect(service.status('0x' + 'ab'.repeat(32))).rejects.toMatchObject({
      code: 'TRANSFER_NOT_FOUND',
    });
  });
});

describe('USDT switches and the blacklist (08 §3.7.1, §3.7.6)', () => {
  it.each([
    ['paused', () => (chain.usdtState.paused = true)],
    ['deprecated', () => (chain.usdtState.deprecated = true)],
    ['fee switch on', () => (chain.usdtState.bps = 1n)],
    ['maximum fee set', () => (chain.usdtState.maxFee = 1n)],
  ])('%s → TOKEN_CONTRACT_UNSAFE, nothing signed', async (_label, flip) => {
    flip();
    const { service } = build();
    await expect(service.payout(payout())).rejects.toMatchObject({ code: 'TOKEN_CONTRACT_UNSAFE' });
    expect(chain.sent).toHaveLength(0);
  });

  it('the check runs before EVERY send, not once', async () => {
    const { service } = build();
    await service.payout(payout(1));
    chain.usdtState.paused = true;
    await expect(service.payout(payout(2))).rejects.toMatchObject({
      code: 'TOKEN_CONTRACT_UNSAFE',
    });
  });

  it('a blacklisted RECIPIENT is refused before signing (the simulation would not see it)', async () => {
    chain.blacklist.add(canonicalAddress(SELLER));
    const { service } = build();
    await expect(service.payout(payout())).rejects.toMatchObject({ code: 'RECIPIENT_BLACKLISTED' });
    expect(chain.sent).toHaveLength(0);
  });

  it('a refund to a blacklisted buyer is refused too', async () => {
    chain.setToken(USDT, DEPOSIT.address, 50_000_000n);
    chain.setEth(DEPOSIT.address, 10n ** 16n);
    chain.blacklist.add(canonicalAddress(BUYER));
    const { service } = build();
    await expect(
      service.refund({
        blockchainTransactionId: ID(7),
        depositIndex: 5,
        depositAddress: DEPOSIT.address,
        toBuyerAddress: BUYER,
        amount: '10',
        token: 'USDT',
        refundType: 'BUYER_REFUND',
      }),
    ).rejects.toMatchObject({ code: 'RECIPIENT_BLACKLISTED' });
  });

  it('a blacklisted SENDER shows as a simulation revert', async () => {
    chain.blacklist.add(canonicalAddress(HOT.address));
    const { service } = build();
    await expect(service.payout(payout())).rejects.toMatchObject({
      code: 'TRANSFER_SIMULATION_REVERTED',
    });
    expect(chain.sent).toHaveLength(0);
  });
});

describe('fee cap and wait (08 §3.7.7)', () => {
  it('signs with maxFeePerGas at or under the cap computed from 0.50 $ and the ETH price', async () => {
    const { service, journal } = build();
    await service.payout(payout());
    const record = journal.get(`transfer:${ID(1)}`)!;
    // 0.50 $ / (64,031 gas × 2,364.48 $) = 3.30 gwei (08 §3.7.7 "~3,3 gwei").
    expect(record.feeCapWeiPerGas).toBe('3302509495');
    expect(BigInt(record.attempts[0].maxFeePerGas)).toBeLessThanOrEqual(3_302_509_495n);
  });

  it('above the cap → deferred, nothing signed; the wait clock survives retries and restarts', async () => {
    chain.baseFee = 4_000_000_000n;
    const first = build();
    const err1 = await first.service.payout(payout()).catch((e: unknown) => e);
    expect(err1).toBeInstanceOf(TransferDeferredError);
    expect(err1).toMatchObject({ reason: 'FEE_ABOVE_CAP', waitingSince: clock });
    const started = clock;

    clock += 60_000;
    const restarted = build();
    const err2 = await restarted.service.payout(payout()).catch((e: unknown) => e);
    expect(err2).toMatchObject({ reason: 'FEE_ABOVE_CAP', waitingSince: started });
    expect(chain.sent).toHaveLength(0);
    expect(readdirSync(dir).filter((f) => f.startsWith('transfer-'))).toHaveLength(0);

    chain.baseFee = 1_000_000_000n;
    clock += 60_000;
    await restarted.service.payout(payout());
    expect(chain.sent).toHaveLength(1);
  });

  it('base + tip under the cap but 2 × base + tip over it → signed with maxFeePerGas exactly the cap', async () => {
    chain.baseFee = 2_000_000_000n; // 2 × 2 + 0.05 = 4.05 gwei > cap 3.30; 2.05 < cap
    const { service, journal } = build();
    await service.payout(payout());
    const record = journal.get(`transfer:${ID(1)}`)!;
    expect(record.attempts[0].maxFeePerGas).toBe(record.feeCapWeiPerGas);
  });

  it('no ETH price → ETH_PRICE_UNAVAILABLE (fail closed)', async () => {
    price = null;
    const { service } = build();
    await expect(service.payout(payout())).rejects.toMatchObject({
      reason: 'ETH_PRICE_UNAVAILABLE',
    });
    expect(chain.sent).toHaveLength(0);
  });

  it('a refund is not capped: it goes out at a fee above the cap and without a price', async () => {
    price = null;
    chain.baseFee = 50_000_000_000n;
    chain.setToken(USDT, DEPOSIT.address, 50_000_000n);
    chain.setEth(DEPOSIT.address, 10n ** 17n);
    const { service } = build();
    await service.refund({
      blockchainTransactionId: ID(8),
      depositIndex: 5,
      depositAddress: DEPOSIT.address,
      toBuyerAddress: BUYER,
      amount: '49.5',
      token: 'USDT',
      refundType: 'EXCESS_REFUND',
    });
    const tx = Transaction.from(chain.sent[0]);
    expect(canonicalAddress(tx.from!)).toBe(canonicalAddress(DEPOSIT.address));
    expect(decodeTransferCall(tx.data)).toEqual({ to: BUYER, amountUnits: 49_500_000n });
  });

  it('a deposit without ETH for gas waits for fuel (FUEL_REQUIRED), nothing signed', async () => {
    chain.setToken(USDT, DEPOSIT.address, 50_000_000n);
    const { service } = build();
    await expect(
      service.sweep({
        blockchainTransactionId: ID(9),
        depositIndex: 5,
        depositAddress: DEPOSIT.address,
        toHotWalletAddress: HOT.address,
        amount: '50',
        token: 'USDT',
        feeCapUsd: '0.50',
      }),
    ).rejects.toMatchObject({ reason: 'FUEL_REQUIRED' });
    expect(chain.sent).toHaveLength(0);
  });
});

describe('pinned destinations and limits (§3.3a twin)', () => {
  beforeEach(() => {
    chain.setToken(USDT, DEPOSIT.address, 50_000_000n);
    chain.setEth(DEPOSIT.address, 10n ** 16n);
  });

  it('a sweep goes only to the configured hot wallet', async () => {
    const { service } = build();
    await expect(
      service.sweep({
        blockchainTransactionId: ID(10),
        depositIndex: 5,
        depositAddress: DEPOSIT.address,
        toHotWalletAddress: SELLER,
        amount: '50',
        token: 'USDT',
        feeCapUsd: '0.50',
      }),
    ).rejects.toMatchObject({ code: 'DESTINATION_NOT_ALLOWED' });
    const ok = await service.sweep({
      blockchainTransactionId: ID(11),
      depositIndex: 5,
      depositAddress: DEPOSIT.address,
      toHotWalletAddress: HOT.address.toLowerCase(),
      amount: '50',
      token: 'USDT',
      feeCapUsd: '0.50',
    });
    expect(decodeTransferCall(Transaction.from(chain.sent[0]).data)?.to).toBe(
      canonicalAddress(HOT.address),
    );
    expect(ok.nonce).toBe(0);
  });

  it('the deposit key must derive the named deposit address', async () => {
    const { service } = build();
    await expect(
      service.sweep({
        blockchainTransactionId: ID(12),
        depositIndex: 6,
        depositAddress: DEPOSIT.address,
        toHotWalletAddress: HOT.address,
        amount: '50',
        token: 'USDT',
        feeCapUsd: '0.50',
      }),
    ).rejects.toMatchObject({ code: 'DEPOSIT_ADDRESS_MISMATCH' });
  });

  it('a cold transfer goes only to the configured cold wallet', async () => {
    const { service } = build();
    await expect(
      service.coldWalletTransfer({
        coldTransferId: ID(13),
        toColdAddress: SELLER,
        amount: '1',
        token: 'USDT',
        feeCapUsd: '0.50',
      }),
    ).rejects.toMatchObject({ code: 'DESTINATION_NOT_ALLOWED' });
    const ok = await service.coldWalletTransfer({
      coldTransferId: ID(14),
      toColdAddress: COLD,
      amount: '1',
      token: 'USDT',
      feeCapUsd: '0.50',
    });
    expect(ok.replayed).toBe(false);
  });

  it('single-transfer cap on payouts and refunds; unset cap refuses (fail closed)', async () => {
    const { service } = build();
    await expect(service.payout(payout(1, { amount: '5000.000001' }))).rejects.toMatchObject({
      code: 'TRANSFER_AMOUNT_ABOVE_LIMIT',
    });
    const unset = build({ maxSingle: null });
    await expect(unset.service.payout(payout(2))).rejects.toMatchObject({
      code: 'TRANSFER_LIMIT_NOT_CONFIGURED',
    });
    expect(chain.sent).toHaveLength(0);
  });

  it('daily cap counts chain outflow of the last 7,200 blocks plus signed-not-seen journal payouts', async () => {
    // 15,000 USDT already left the hot wallet on chain inside the window; a
    // consolidation to cold and an older outflow do not count.
    chain.addOutflowLog(USDT, HOT.address, SELLER, 15_000_000_000n, chain.head - 100n);
    chain.addOutflowLog(USDT, HOT.address, COLD, 9_000_000_000n, chain.head - 50n);
    chain.head = 10_000n;
    chain.addOutflowLog(USDT, HOT.address, SELLER, 9_000_000_000n, 1_000n); // > 7,200 blocks ago
    chain.logs[0] = { ...chain.logs[0], blockNumber: 9_000n };
    chain.logs[1] = { ...chain.logs[1], blockNumber: 9_100n };
    const { service } = build();
    await service.payout(payout(1, { amount: '4000' })); // 19,000 — allowed, pending (not on chain)
    await expect(service.payout(payout(2, { amount: '1000.000001' }))).rejects.toMatchObject({
      code: 'DAILY_OUTFLOW_LIMIT_EXCEEDED',
    });
    await service.payout(payout(3, { amount: '1000' })); // exactly 20,000
    const unset = build({ maxDaily: null });
    await expect(unset.service.payout(payout(4, { amount: '1' }))).rejects.toMatchObject({
      code: 'TRANSFER_LIMIT_NOT_CONFIGURED',
    });
  });

  it('USDC moves only in a WRONG_TOKEN_REFUND', async () => {
    chain.setToken(USDC, DEPOSIT.address, 7_000_000n);
    const { service } = build();
    const base = {
      depositIndex: 5,
      depositAddress: DEPOSIT.address,
      toBuyerAddress: BUYER,
      amount: '7',
      token: 'USDC' as const,
    };
    await expect(
      service.refund({ ...base, blockchainTransactionId: ID(20), refundType: 'BUYER_REFUND' }),
    ).rejects.toMatchObject({ code: 'TOKEN_NOT_ALLOWED' });
    await service.refund({
      ...base,
      blockchainTransactionId: ID(21),
      refundType: 'WRONG_TOKEN_REFUND',
    });
    expect(canonicalAddress(Transaction.from(chain.sent[0]).to!)).toBe(USDC);
  });

  it('a recipient equal to the sender or the zero address is refused', async () => {
    const { service } = build();
    await expect(service.payout(payout(1, { toAddress: HOT.address }))).rejects.toMatchObject({
      code: 'INVALID_TRANSFER_REQUEST',
    });
    await expect(
      service.payout(payout(2, { toAddress: '0x0000000000000000000000000000000000000000' })),
    ).rejects.toMatchObject({ code: 'INVALID_TRANSFER_REQUEST' });
  });
});
