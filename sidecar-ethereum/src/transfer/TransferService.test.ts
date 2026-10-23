import { cpSync, mkdtempSync, readFileSync, readdirSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { Transaction } from 'ethers';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { canonicalAddress } from '../address/canonicalAddress.js';
import { MAX_SIGNABLE_GAS_LIMIT } from '../fee/feeMath.js';
import { RpcError } from '../rpc/RpcClient.js';
import { FakeSendChain, SEPOLIA_T159_GAS_TARIFF } from '../testing/FakeEthereumChain.js';
import { EthereumHdWallet } from '../wallet/HdWalletService.js';
import { MAINNET_TOKEN_CONTRACTS } from '../network.js';
import { decodeTransferCall } from './erc20.js';
import { FeeWaitTracker } from './FeeWaitTracker.js';
import { TransferDeferredError } from './FeePolicy.js';
import { TransferGuard } from './TransferGuard.js';
import { TransferService, type PayoutRequest } from './TransferService.js';
import { TxJournal } from './TxJournal.js';
import { DepositRefundPermitStore, type PermitEvent } from './DepositRefundPermits.js';
import { checksummedEvmAddress } from '../address/evmAddress.js';

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
let chain: FakeSendChain;
let clock: number;
let price: bigint | null;

beforeEach(() => {
  dir = mkdtempSync(join(tmpdir(), 'eth-transfer-'));
  chain = new FakeSendChain(USDT, USDC);
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
    maxDepositRefund: bigint | null;
    permits: DepositRefundPermitStore | null;
    hotKey: string | null;
    hotAddress: string | null;
    coldAddress: string | null;
    acknowledged: ReadonlyMap<string, number>;
  }> = {},
) {
  const now = () => clock;
  const journal = new TxJournal(dir);
  const hotAddress =
    'hotAddress' in overrides ? overrides.hotAddress! : canonicalAddress(HOT.address);
  const service = new TransferService({
    rpc: chain.rpc,
    journal,
    feeWaits: new FeeWaitTracker(dir, now),
    guard: new TransferGuard({
      rpc: chain.rpc,
      hotWalletAddress: hotAddress,
      coldWalletAddress:
        'coldAddress' in overrides ? overrides.coldAddress! : canonicalAddress(COLD),
      tokenContracts: [USDT, USDC],
      maxSingleTransferUnits: 'maxSingle' in overrides ? overrides.maxSingle! : 5_000_000_000n,
      maxDailyOutflowUnits: 'maxDaily' in overrides ? overrides.maxDaily! : 20_000_000_000n,
      maxDailyDepositRefundUnits:
        'maxDepositRefund' in overrides ? overrides.maxDepositRefund! : 1_000_000_000_000n,
      depositRefundPermits: overrides.permits ?? null,
      now,
    }),
    wallet,
    policy: {
      chainId: 11_155_111n,
      paymentToken: USDT,
      wrongTokenRefundToken: USDC,
      maxGasLimit: MAX_SIGNABLE_GAS_LIMIT,
    },
    hotWalletKey: 'hotKey' in overrides ? overrides.hotKey! : HOT.privateKey,
    hotWalletAddress: hotAddress,
    tokenContracts: { USDT, USDC },
    prices: { ethUsdMicros: () => Promise.resolve(price) },
    finalizedHead: () => chain.finalized,
    bumpAfterMs: 300_000,
    conflictConfirmMs: 30_000,
    acknowledgedNonces: overrides.acknowledged,
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

  it('a nonce used outside the journal STOPS signing (journal behind chain); an operator acknowledgement moves the next send past it', async () => {
    chain.foreignSend(HOT.address);
    chain.foreignSend(HOT.address);
    const { service } = build();
    await expect(service.payout(payout())).rejects.toMatchObject({
      code: 'JOURNAL_BEHIND_CHAIN',
      retryable: true,
    });
    expect(chain.sent).toHaveLength(0);
    expect(service.journalBehindChain()).toEqual([
      expect.objectContaining({
        from: canonicalAddress(HOT.address),
        chainNonce: 2,
        journalNext: 0,
      }),
    ]);

    const acknowledged = build({
      acknowledged: new Map([[canonicalAddress(HOT.address), 2]]),
    });
    expect((await acknowledged.service.payout(payout())).nonce).toBe(2);
    expect(acknowledged.service.journalBehindChain()).toEqual([]);
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
    const signed = journal.get(`transfer:${ID(1)}`)!;
    const cap = BigInt(signed.feeCapWei!) / BigInt(signed.gasLimit); // highest maxFee per gas
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
      realizedFeeWei: null,
      platformSpendWei: null,
      nativePriceUsdt: null,
    });
  });

  it('T160c düzeltme turu — a final hot-wallet send reports its receipt fee as the platform spend; no price → null price, the status still answers', async () => {
    chain.receiptGasUsed = 63_209n;
    chain.receiptEffectiveGasPrice = 3_000_000_011n;
    const { service } = build();
    const { txHash } = await service.payout(payout());
    chain.mine();
    finalizeAll();
    price = null; // the feed is down when the final status is read
    const status = await service.status(txHash);
    expect(status.contractRet).toBe('SUCCESS');
    expect(status.platformSpendWei).toBe((63_209n * 3_000_000_011n).toString());
    expect(status.platformSpendWei).toBe(status.realizedFeeWei);
    expect(status.nativePriceUsdt).toBeNull();
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

describe('T158 (g) — status of a hash the journal does not hold (lost journal) — fail closed', () => {
  /** A payout signed and finalized by one service, then asked of a service whose journal is gone. */
  async function lostJournal() {
    const { service } = build();
    const { txHash } = await service.payout(payout());
    chain.mine();
    finalizeAll();
    rmSync(dir, { recursive: true, force: true });
    dir = mkdtempSync(join(tmpdir(), 'eth-transfer-lost-'));
    const { service: fresh } = build();
    return { fresh, txHash };
  }
  const expected = {
    contract: USDT,
    from: canonicalAddress(HOT.address),
    to: canonicalAddress(SELLER),
    amountUnits: 12_345_678n,
  };

  it('the Transfer log matches the backend row (contract, sender, recipient, amount) → SUCCESS', async () => {
    const { fresh, txHash } = await lostJournal();
    expect(await fresh.status(txHash, expected)).toMatchObject({
      contractRet: 'SUCCESS',
      finalized: true,
    });
  });

  it.each([
    ['no expectation sent', null],
    ['another amount', { ...expected, amountUnits: 12_345_679n }],
    ['another recipient', { ...expected, to: canonicalAddress(BUYER) }],
    ['another sender', { ...expected, from: canonicalAddress(DEPOSIT.address) }],
    ['another (known) contract', { ...expected, contract: USDC }],
    ['a contract this service does not know', { ...expected, contract: canonicalAddress(SELLER) }],
  ])('%s → UNKNOWN (never SUCCESS) and counted for the alarm', async (_label, exp) => {
    const { fresh, txHash } = await lostJournal();
    const client = (await import('prom-client')).default;
    const read = async () =>
      (
        (
          await client.register
            .getSingleMetric('skinora_ethereum_transfer_status_unverified_total')
            ?.get()
        )?.values ?? []
      ).reduce((s, v) => s + v.value, 0);
    const before = await read();
    expect(await fresh.status(txHash, exp)).toMatchObject({
      contractRet: 'UNKNOWN',
      finalized: true,
    });
    expect(await read()).toBe(before + 1);
  });

  it('the UNKNOWN answer has the shape of the contract example the backend maps to Unverified', async () => {
    const { fresh, txHash } = await lostJournal();
    const answer = await fresh.status(txHash, null);
    const sample = JSON.parse(
      readFileSync(
        resolve(__dirname, '../../contracts/transfer-status/unknown.response.json'),
        'utf8',
      ),
    ) as Record<string, unknown>;
    expect(Object.keys(answer).sort()).toEqual(Object.keys(sample).sort());
    expect(answer.contractRet).toBe(sample.contractRet);
    expect(answer.finalized).toBe(true);
  });

  it('a reverted unjournalled hash stays REVERT (the chain says nothing moved)', async () => {
    const { service } = build();
    const { txHash } = await service.payout(payout());
    chain.setToken(USDT, HOT.address, 0n);
    chain.mine();
    finalizeAll();
    rmSync(dir, { recursive: true, force: true });
    dir = mkdtempSync(join(tmpdir(), 'eth-transfer-lost-'));
    const { service: fresh } = build();
    expect(await fresh.status(txHash, expected)).toMatchObject({ contractRet: 'REVERT' });
  });

  it('a journalled hash ignores the expectation: the record is authoritative (existing NO_TRANSFER_LOG rule unchanged)', async () => {
    const { service } = build();
    const { txHash } = await service.payout(payout());
    chain.mine();
    finalizeAll();
    expect(await service.status(txHash, { ...expected, amountUnits: 1n })).toMatchObject({
      contractRet: 'SUCCESS',
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
        feeCapUsd: '0.50',
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
  it('signs with gasLimit × maxFeePerGas at or under the cap computed from 0.50 $ and the ETH price', async () => {
    const { service, journal } = build();
    await service.payout(payout());
    const record = journal.get(`transfer:${ID(1)}`)!;
    // Per transaction (yönetici kararı, T152 doğrulaması): 0.50 $ / 2,364.48 $ = 211,462,985,519,014 wei.
    expect(record.feeCapWei).toBe('211462985519014');
    expect(record.gasLimit).toBe('50822'); // 46,201 × 1.1, rounded up
    expect(BigInt(record.gasLimit) * BigInt(record.attempts[0].maxFeePerGas)).toBeLessThanOrEqual(
      211_462_985_519_014n,
    );
  });

  it('above the cap → deferred, nothing signed; the wait clock survives retries and restarts', async () => {
    chain.baseFee = 5_000_000_000n; // 50,822 × 5.05 gwei > 211,462,985,519,014 wei
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

  it('base + tip under the cap but 2 × base + tip over it → signed with maxFeePerGas = ⌊cap ÷ gasLimit⌋', async () => {
    chain.baseFee = 3_000_000_000n; // 50,822 × 3.05 gwei < cap; × 6.05 gwei > cap
    const { service, journal } = build();
    await service.payout(payout());
    const record = journal.get(`transfer:${ID(1)}`)!;
    expect(record.attempts[0].maxFeePerGas).toBe(
      String(BigInt(record.feeCapWei!) / BigInt(record.gasLimit)),
    );
  });

  // Yönetici kararı (T152 doğrulaması): the cap is per TRANSACTION —
  // gasLimit × maxFeePerGas ≤ cap (wei). Payout: 211,462,985,519,014 wei;
  // gas limit 50,822 → ⌊cap ÷ gasLimit⌋ = 4,160,855,250 wei/gas.
  const PAYOUT_CAP_WEI = 211_462_985_519_014n;
  const CEILING_50822 = PAYOUT_CAP_WEI / 50_822n;

  it('boundary: gasLimit × (base + tip) EXACTLY the cap is signed, worst case = cap or less', async () => {
    expect(CEILING_50822).toBe(4_160_855_250n);
    chain.baseFee = CEILING_50822 - chain.tip;
    const { service, journal } = build();
    await service.payout(payout());
    const record = journal.get(`transfer:${ID(1)}`)!;
    expect(BigInt(record.attempts[0].maxFeePerGas)).toBe(CEILING_50822);
    expect(50_822n * CEILING_50822).toBeLessThanOrEqual(PAYOUT_CAP_WEI);
  });

  it('boundary: 1 wei per gas over the cap → waits (FEE_ABOVE_CAP), nothing signed', async () => {
    chain.baseFee = CEILING_50822 - chain.tip + 1n;
    const { service } = build();
    await expect(service.payout(payout())).rejects.toMatchObject({ reason: 'FEE_ABOVE_CAP' });
    expect(chain.sent).toHaveLength(0);
  });

  it('a 136,000-gas estimate stays within 0.50 $ in the worst case (the per-gas cap paid ~0.725 $)', async () => {
    chain.estimate = 136_000n; // gas limit 149,600
    const { service, journal } = build();
    await service.payout(payout());
    const record = journal.get(`transfer:${ID(1)}`)!;
    expect(record.gasLimit).toBe('149600');
    const worstCase = BigInt(record.gasLimit) * BigInt(record.attempts[0].maxFeePerGas);
    expect(worstCase).toBeLessThanOrEqual(PAYOUT_CAP_WEI);
    // In dollars: worst case × ETH price ≤ 0.50 $ (micro-dollars).
    expect((worstCase * ETH_USD) / 10n ** 18n).toBeLessThanOrEqual(500_000n);
    // The old per-gas cap would have let 2 × 1 + 0.05 = 2.05 gwei through: 0.725 $.
    expect((149_600n * 2_050_000_000n * ETH_USD) / 10n ** 18n).toBe(725_138n);
  });

  it('T159a — a payout to a seller who never held USDT on the Sepolia tariff (153,000 estimated → limit 168,300) signs and lands; the old 150,000 bound refused it', async () => {
    chain.tariff = SEPOLIA_T159_GAS_TARIFF;
    const { service, journal } = build();
    await service.payout(payout());
    const record = journal.get(`transfer:${ID(1)}`)!;
    expect(record.gasLimit).toBe('168300');
    expect(BigInt(record.gasLimit) * BigInt(record.attempts[0].maxFeePerGas)).toBeLessThanOrEqual(
      PAYOUT_CAP_WEI,
    );
    chain.mine();
    expect(chain.receipts.get(record.attempts[0].hash)!.status).toBe(1);
    expect(chain.token(USDT, SELLER)).toBe(12_345_678n);
  });

  it('T159a — a payout whose estimate is implausible (1,000,001) waits GAS_ESTIMATE_ABNORMAL, nothing signed', async () => {
    chain.estimate = 1_000_001n;
    const { service } = build();
    await expect(service.payout(payout())).rejects.toMatchObject({
      reason: 'GAS_ESTIMATE_ABNORMAL',
      retryable: true,
    });
    expect(chain.sent).toHaveLength(0);
  });

  it('a fee bump stays within the same per-transaction cap', async () => {
    const { service, journal } = build();
    await service.payout(payout());
    chain.baseFee = 2_100_000_000n; // the bump wants 2 × 2.1 + tip > ceiling: clamped
    clock += 300_000;
    await service.keepAll();
    const record = journal.get(`transfer:${ID(1)}`)!;
    expect(record.attempts).toHaveLength(2);
    for (const attempt of record.attempts) {
      expect(BigInt(record.gasLimit) * BigInt(attempt.maxFeePerGas)).toBeLessThanOrEqual(
        PAYOUT_CAP_WEI,
      );
    }
    expect(BigInt(record.attempts[1].maxFeePerGas)).toBe(CEILING_50822);
  });

  it('a sweep keeps the fuel send’s share of the cap: its own transaction gets its gas limit / (its + the fuel’s) of 0.50 $ (T159a)', async () => {
    chain.setToken(USDT, DEPOSIT.address, 50_000_000n);
    chain.setEth(DEPOSIT.address, 10n ** 16n);
    const { service, journal } = build();
    await service.sweep({
      blockchainTransactionId: ID(30),
      depositIndex: 5,
      depositAddress: DEPOSIT.address,
      toHotWalletAddress: HOT.address,
      amount: '50',
      token: 'USDT',
      feeCapUsd: '0.50',
    });
    // T159a: shared by ESTIMATED gas — the sweep's limit 50,822 and (no fuel
    // wallet in this suite) the intrinsic 21,000 — no longer 46,201 / 67,201.
    expect(journal.get(`transfer:${ID(30)}`)?.feeCapWei).toBe(
      ((500_000n * 10n ** 18n * 50_822n) / (71_822n * ETH_USD)).toString(),
    );
    expect(journal.get(`transfer:${ID(30)}`)?.feeCapWei).toBe('149633424995786');
  });

  it('no ETH price → ETH_PRICE_UNAVAILABLE (fail closed)', async () => {
    price = null;
    const { service } = build();
    await expect(service.payout(payout())).rejects.toMatchObject({
      reason: 'ETH_PRICE_UNAVAILABLE',
    });
    expect(chain.sent).toHaveLength(0);
  });

  it('a refund is capped too (yönetici kararı 2026-10-18): above the cap or without a price it waits; under it, it goes', async () => {
    chain.setToken(USDT, DEPOSIT.address, 50_000_000n);
    chain.setEth(DEPOSIT.address, 10n ** 17n);
    const refund = {
      blockchainTransactionId: ID(8),
      depositIndex: 5,
      depositAddress: DEPOSIT.address,
      toBuyerAddress: BUYER,
      amount: '49.5',
      token: 'USDT' as const,
      refundType: 'EXCESS_REFUND' as const,
      feeCapUsd: '0.50',
    };
    chain.baseFee = 50_000_000_000n;
    const { service, journal } = build();
    await expect(service.refund(refund)).rejects.toMatchObject({ reason: 'FEE_ABOVE_CAP' });
    price = null;
    chain.baseFee = 1_000_000_000n;
    await expect(service.refund(refund)).rejects.toMatchObject({ reason: 'ETH_PRICE_UNAVAILABLE' });
    expect(chain.sent).toHaveLength(0);

    price = ETH_USD;
    await service.refund(refund);
    // T159a: 0.50 $ / 2,364.48 $ × 50,822 / (50,822 + 21,000) — the fuel send
    // keeps its share by its own gas; the refund transaction gets the rest.
    expect(journal.get(`transfer:${ID(8)}`)?.feeCapWei).toBe('149633424995786');
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

  it('T160c: a USDC sweep (the fee a wrong-token refund withheld) moves USDC, only to the pinned hot wallet', async () => {
    chain.setToken(USDC, DEPOSIT.address, 370_000n);
    const { service, journal } = build();
    await expect(
      service.sweep({
        ...sweepRequest(70),
        amount: '0.37',
        token: 'USDC',
        toHotWalletAddress: SELLER,
      }),
    ).rejects.toMatchObject({ code: 'DESTINATION_NOT_ALLOWED' });
    expect(chain.sent).toHaveLength(0);

    await service.sweep({ ...sweepRequest(71), amount: '0.37', token: 'USDC' });
    const tx = Transaction.from(chain.sent[0]);
    expect(canonicalAddress(tx.to!)).toBe(USDC);
    expect(canonicalAddress(tx.from!)).toBe(canonicalAddress(DEPOSIT.address));
    expect(decodeTransferCall(tx.data)).toEqual({
      to: canonicalAddress(HOT.address),
      amountUnits: 370_000n,
    });
    expect(journal.get(`transfer:${ID(71)}`)?.token).toBe('USDC');
  });

  it('T160c: a USDC sweep that waits 5 min is re-signed by the fee bump (the bump keeps the flow, so USDC stays allowed)', async () => {
    chain.setToken(USDC, DEPOSIT.address, 370_000n);
    const { service, journal } = build();
    await service.sweep({ ...sweepRequest(73), amount: '0.37', token: 'USDC' });
    const original = Transaction.from(chain.sent[0]);

    chain.baseFee = 1_300_000_000n;
    clock += 300_000;
    await service.keepAll();

    const record = journal.get(`transfer:${ID(73)}`)!;
    expect(record.attempts).toHaveLength(2);
    const bumped = Transaction.from(record.attempts[1].raw);
    expect(bumped.nonce).toBe(original.nonce);
    expect(canonicalAddress(bumped.to!)).toBe(USDC);
    expect(decodeTransferCall(bumped.data)).toEqual(decodeTransferCall(original.data));
  });

  it('T160c: USDC is still refused on every other deposit flow but a wrong-token refund', async () => {
    chain.setToken(USDC, DEPOSIT.address, 5_000_000n);
    const { service } = build();
    await expect(
      service.refund({
        blockchainTransactionId: ID(72),
        depositIndex: 5,
        depositAddress: DEPOSIT.address,
        toBuyerAddress: BUYER,
        amount: '1',
        token: 'USDC',
        refundType: 'EXCESS_REFUND',
        feeCapUsd: '0.50',
      }),
    ).rejects.toMatchObject({ code: 'TOKEN_NOT_ALLOWED' });
    expect(chain.sent).toHaveLength(0);
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
      feeCapUsd: '0.50',
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

// ---- T152 düzeltme turu 2 ------------------------------------------------------

/** A second hot wallet (another key) — "the hot key was rotated". */
const OTHER_HOT = wallet.deriveSigner(40);

function refundRequest(n: number) {
  return {
    blockchainTransactionId: ID(n),
    depositIndex: 5,
    depositAddress: DEPOSIT.address,
    toBuyerAddress: BUYER,
    amount: '10',
    token: 'USDT' as const,
    refundType: 'BUYER_REFUND' as const,
    feeCapUsd: '0.50',
  };
}

function sweepRequest(n: number) {
  return {
    blockchainTransactionId: ID(n),
    depositIndex: 5,
    depositAddress: DEPOSIT.address,
    toHotWalletAddress: HOT.address,
    amount: '50',
    token: 'USDT' as const,
    feeCapUsd: '0.50',
  };
}

function coldRequest(n: number) {
  return {
    coldTransferId: ID(n),
    toColdAddress: COLD,
    amount: '1',
    token: 'USDT' as const,
    feeCapUsd: '0.50',
  };
}

describe('invariant: a journalled id never gets a 400 because a setting changed (bulgu 1)', () => {
  beforeEach(() => {
    chain.setToken(USDT, DEPOSIT.address, 100_000_000n);
    chain.setEth(DEPOSIT.address, 10n ** 17n);
  });

  type Change = [string, () => ReturnType<typeof build>, Partial<PayoutRequest>?];
  const changes: Change[] = [
    ['single-transfer cap lowered below the amount', () => build({ maxSingle: 1n })],
    ['single-transfer cap 0', () => build({ maxSingle: 0n })],
    ['single-transfer cap removed', () => build({ maxSingle: null })],
    ['daily cap 0', () => build({ maxDaily: 0n })],
    [
      'hot key rotated (another key and address)',
      () =>
        build({ hotKey: OTHER_HOT.privateKey, hotAddress: canonicalAddress(OTHER_HOT.address) }),
    ],
    ['hot key removed', () => build({ hotKey: null, hotAddress: null })],
    ['cold wallet changed', () => build({ coldAddress: canonicalAddress(SELLER) })],
    ['fee cap in the request lowered to 0.01 $', () => build(), { feeCapUsd: '0.01' }],
    ['fee cap in the request 0', () => build(), { feeCapUsd: '0' }],
    [
      'no ETH price',
      () => {
        price = null;
        return build();
      },
    ],
    [
      'fee far above the cap',
      () => {
        chain.baseFee = 500_000_000_000n;
        return build();
      },
    ],
    [
      'USDT paused',
      () => {
        chain.usdtState.paused = true;
        return build();
      },
    ],
  ];

  it.each(changes)(
    'payout — %s → the recorded hash, nothing new signed',
    async (_l, after, ask) => {
      const first = await build().service.payout(payout());
      const sentBefore = new Set(chain.sent);
      const { service } = after();
      const again = await service.payout(payout(1, ask ?? {}));
      expect(again).toEqual({ txHash: first.txHash, nonce: first.nonce, replayed: true });
      expect(new Set(chain.sent)).toEqual(sentBefore);
    },
  );

  it.each(changes)('refund — %s → the recorded hash', async (_l, after, ask) => {
    const first = await build().service.refund(refundRequest(2));
    const { service } = after();
    const again = await service.refund({ ...refundRequest(2), ...(ask ?? {}) });
    expect(again).toMatchObject({ txHash: first.txHash, replayed: true });
  });

  it.each(changes)('sweep — %s → the recorded hash', async (_l, after, ask) => {
    const first = await build().service.sweep(sweepRequest(3));
    const { service } = after();
    const again = await service.sweep({ ...sweepRequest(3), ...(ask ?? {}) });
    expect(again).toMatchObject({ txHash: first.txHash, replayed: true });
  });

  it.each(changes)('cold transfer — %s → the recorded hash', async (_l, after, ask) => {
    const first = await build().service.coldWalletTransfer(coldRequest(4));
    const { service } = after();
    const again = await service.coldWalletTransfer({ ...coldRequest(4), ...(ask ?? {}) });
    expect(again).toMatchObject({ txHash: first.txHash, replayed: true });
  });

  it('a sweep id signed before the hot wallet setting changed still replays', async () => {
    const first = await build().service.sweep(sweepRequest(5));
    // The guard's pinned hot wallet is now another address; the request is the recorded one.
    const { service } = build({ hotAddress: canonicalAddress(OTHER_HOT.address) });
    expect(await service.sweep(sweepRequest(5))).toMatchObject({
      txHash: first.txHash,
      replayed: true,
    });
  });

  it('a different recipient / amount / flow on a journalled id is still IDEMPOTENCY_CONFLICT', async () => {
    await build().service.payout(payout());
    const { service } = build({ maxSingle: 0n });
    await expect(service.payout(payout(1, { toAddress: BUYER }))).rejects.toMatchObject({
      code: 'IDEMPOTENCY_CONFLICT',
    });
    await expect(service.payout(payout(1, { amount: '1' }))).rejects.toMatchObject({
      code: 'IDEMPOTENCY_CONFLICT',
    });
    await expect(service.refund({ ...refundRequest(1) })).rejects.toMatchObject({
      code: 'IDEMPOTENCY_CONFLICT',
    });
  });
});

describe('replay identity — every recorded field is compared (M3–M6, T152 doğrulaması)', () => {
  const OTHER_DEPOSIT = wallet.derive(6);

  beforeEach(() => {
    chain.setToken(USDT, DEPOSIT.address, 100_000_000n);
    chain.setEth(DEPOSIT.address, 10n ** 17n);
  });

  it.each([
    ['refundType (M3)', { refundType: 'EXCESS_REFUND' as const }],
    ['depositAddress (M4)', { depositAddress: OTHER_DEPOSIT.address }],
    ['depositIndex (M5)', { depositIndex: 6 }],
    ['token (M6)', { token: 'USDC' as const }],
  ])(
    'a refund replayed with another %s → IDEMPOTENCY_CONFLICT, nothing new signed',
    async (_l, change) => {
      const { service } = build();
      await service.refund(refundRequest(2));
      const sentBefore = chain.sent.length;
      await expect(service.refund({ ...refundRequest(2), ...change })).rejects.toMatchObject({
        code: 'IDEMPOTENCY_CONFLICT',
        retryable: false,
      });
      expect(chain.sent.length).toBe(sentBefore);
    },
  );

  it('a payout replayed with another token → IDEMPOTENCY_CONFLICT (M6)', async () => {
    const { service } = build();
    await service.payout(payout());
    await expect(service.payout(payout(1, { token: 'USDC' }))).rejects.toMatchObject({
      code: 'IDEMPOTENCY_CONFLICT',
    });
  });

  it('a replay after the OLD attempt was mined answers the hash on chain, not the latest attempt (M36)', async () => {
    const { service, journal } = build();
    const first = await service.payout(payout());
    chain.baseFee = 1_300_000_000n;
    clock += 300_000;
    chain.sendFailures.push(new RpcError('fake transport failure', 'transport', 'fake'));
    await service.keepAll(); // bump signed, its broadcast lost
    const record = journal.get(`transfer:${ID(1)}`)!;
    expect(record.attempts).toHaveLength(2);
    expect(record.attempts[1].hash).not.toBe(first.txHash);
    chain.mine(); // only the first attempt is in the mempool
    await service.keepAll(); // the keeper records the mined attempt

    const sentBefore = chain.sent.length;
    expect(await service.payout(payout())).toEqual({
      txHash: first.txHash,
      nonce: first.nonce,
      replayed: true,
    });
    expect(chain.sent.length).toBe(sentBefore);
  });
});

describe('one id, one signature across flows (bulgu 4)', () => {
  it('a payout and a refund with the SAME id at the same time: one signs, the other waits and is refused', async () => {
    chain.setToken(USDT, DEPOSIT.address, 100_000_000n);
    chain.setEth(DEPOSIT.address, 10n ** 17n);
    const { service, journal } = build();
    const results = await Promise.allSettled([
      service.payout(payout(7)),
      service.refund(refundRequest(7)),
    ]);
    expect(new Set(chain.sent).size).toBe(1);
    expect(results.filter((r) => r.status === 'fulfilled')).toHaveLength(1);
    const refused = results.find((r) => r.status === 'rejected') as PromiseRejectedResult;
    expect(refused.reason).toMatchObject({ code: 'IDEMPOTENCY_CONFLICT' });
    expect(journal.get(`transfer:${ID(7)}`)?.purpose).toBe('PAYOUT');
  });

  it('the same id in another letter case is the same id — also when both arrive at once', async () => {
    const lower = 'abcdef00-0000-0000-0000-00000000000a';
    const { service } = build();
    const results = await Promise.all([
      service.payout({ ...payout(), blockchainTransactionId: lower }),
      service.payout({ ...payout(), blockchainTransactionId: lower.toUpperCase() }),
    ]);
    expect(new Set(chain.sent).size).toBe(1);
    expect(new Set(results.map((r) => r.txHash)).size).toBe(1);
    expect(results.filter((r) => r.replayed)).toHaveLength(1);
  });
});

describe('a node error is not a refusal (bulgu 2)', () => {
  it.each([
    ['header not found', -32000],
    ['missing trie node 3c2a', -32000],
    ['internal error', -32603],
    ['revision id 3 cannot be reverted', -32000],
    ['upstream block reverted (reorg)', -32000],
  ])(
    'the gas estimate answered "%s" → the send WAITS (GAS_ESTIMATE_UNAVAILABLE, 503 deferred — T159a, proje sahibi kararı), nothing signed; the retry signs',
    async (message, code) => {
      chain.simulationFailures.push(
        new RpcError(`eth_estimateGas: ${message}`, 'rpc', 'fake', code),
      );
      const { service } = build();
      const err = await service.payout(payout()).catch((e: unknown) => e);
      expect(err).toBeInstanceOf(TransferDeferredError);
      expect(err).toMatchObject({ reason: 'GAS_ESTIMATE_UNAVAILABLE', retryable: true });
      expect(chain.sent).toHaveLength(0);
      expect((await service.payout(payout())).replayed).toBe(false);
    },
  );

  it('the eth_call simulation answered "header not found" → transient RPC_UNAVAILABLE (502), nothing signed', async () => {
    // The estimate succeeds; the second simulation read (eth_call) fails.
    const estimate = chain.rpc.estimateGas;
    let calls = 0;
    chain.rpc.estimateGas = async (tx, block) => {
      calls++;
      return estimate(tx, block);
    };
    const callFrom = chain.rpc.callFrom;
    chain.rpc.callFrom = async () => {
      chain.rpc.callFrom = callFrom;
      throw new RpcError('eth_call: header not found', 'rpc', 'fake', -32000);
    };
    const { service } = build();
    await expect(service.payout(payout())).rejects.toMatchObject({
      code: 'RPC_UNAVAILABLE',
      retryable: true,
    });
    expect(calls).toBe(1);
    expect(chain.sent).toHaveLength(0);
  });

  it('USDT state read answered "header not found" → transient, not TOKEN_CONTRACT_UNSAFE / RPC_ERROR', async () => {
    chain.stateReadFailures.push(new RpcError('eth_call: header not found', 'rpc', 'fake', -32000));
    const { service } = build();
    await expect(service.payout(payout())).rejects.toMatchObject({
      code: 'RPC_UNAVAILABLE',
      retryable: true,
    });
  });

  it.each([
    [
      'execution reverted (code 3 + data)',
      new RpcError('eth_estimateGas: execution reverted', 'rpc', 'fake', 3, '0x08c379a0'),
    ],
    [
      'revert data without the word',
      new RpcError('eth_estimateGas: vm error', 'rpc', 'fake', -32000, '0x08c379a0ffff'),
    ],
  ])('a real revert — %s — stays a permanent refusal', async (_label, err) => {
    chain.simulationFailures.push(err);
    const { service } = build();
    await expect(service.payout(payout())).rejects.toMatchObject({
      code: 'TRANSFER_SIMULATION_REVERTED',
      retryable: false,
    });
  });

  it('a USDC transfer whose simulation returns false is refused (M34)', async () => {
    chain.setToken(USDC, DEPOSIT.address, 7_000_000n);
    chain.setEth(DEPOSIT.address, 10n ** 17n);
    chain.usdcReturnsFalse = true;
    const { service } = build();
    await expect(
      service.refund({
        ...refundRequest(9),
        amount: '7',
        token: 'USDC',
        refundType: 'WRONG_TOKEN_REFUND',
      }),
    ).rejects.toMatchObject({ code: 'TRANSFER_SIMULATION_REVERTED', retryable: false });
    expect(chain.sent).toHaveLength(0);
  });
});

describe('realizedFeeWei (bulgu 3, T154 karar (vi))', () => {
  const EXAMPLE = JSON.parse(
    readFileSync(
      resolve(__dirname, '../../contracts/transfer-status/confirmed.response.json'),
      'utf8',
    ),
  ) as Record<string, unknown>;

  it('the final status is the contract example: same fields, same types, the fee as gasUsed × effectiveGasPrice text', async () => {
    const fee = BigInt(EXAMPLE.realizedFeeWei as string);
    chain.receiptGasUsed = 63_209n; // T142's measured payout gas
    expect(fee % 63_209n).toBe(0n);
    chain.receiptEffectiveGasPrice = fee / 63_209n;
    chain.head = BigInt(EXAMPLE.blockNumber as number) - 1n; // mined in the example's block
    const { service } = build();
    const { txHash } = await service.payout(payout());
    chain.mine();
    chain.finalized = chain.head + BigInt((EXAMPLE.confirmations as number) - 1);

    const status = await service.status(txHash);
    expect(Object.keys(status).sort()).toEqual(Object.keys(EXAMPLE).sort());
    expect({ ...status, txHash: EXAMPLE.txHash }).toEqual(EXAMPLE);
    expect(status.txHash).toBe(txHash);
    expect(typeof status.realizedFeeWei).toBe('string');
  });

  it('no realizedFeeWei until final', async () => {
    const { service } = build();
    const { txHash } = await service.payout(payout());
    chain.mine();
    expect((await service.status(txHash)).realizedFeeWei).toBeNull();
    finalizeAll();
    expect((await service.status(txHash)).realizedFeeWei).toBe(
      String(41_273n * (chain.baseFee + chain.tip)),
    );
  });
});

describe('journal behind chain — signing stops (bulgu 5, yönetici kararı)', () => {
  it('a lost journal: the same id is NOT signed again with a new nonce', async () => {
    const first = build();
    await first.service.payout(payout());
    chain.mine(); // nonce 0 used on chain
    rmSync(dir, { recursive: true, force: true });
    dir = mkdtempSync(join(tmpdir(), 'eth-transfer-lost-'));

    const { service } = build();
    await expect(service.payout(payout())).rejects.toMatchObject({
      code: 'JOURNAL_BEHIND_CHAIN',
    });
    expect(chain.sent).toHaveLength(1);
    expect(chain.token(USDT, SELLER)).toBe(12_345_678n);
  });

  it('an old backup: a record signed after the backup is missing → stop', async () => {
    const live = build();
    await live.service.payout(payout(1));
    chain.mine();
    const backup = mkdtempSync(join(tmpdir(), 'eth-transfer-backup-'));
    cpSync(dir, backup, { recursive: true });
    await live.service.payout(payout(2)); // nonce 1, after the backup
    chain.mine();

    rmSync(dir, { recursive: true, force: true });
    dir = backup;
    const restored = build();
    await expect(restored.service.payout(payout(2))).rejects.toMatchObject({
      code: 'JOURNAL_BEHIND_CHAIN',
    });
    await expect(restored.service.payout(payout(3))).rejects.toMatchObject({
      code: 'JOURNAL_BEHIND_CHAIN',
    });
    expect(chain.sent).toHaveLength(2);
  });

  it('a foreign transaction only in the mempool (pending) stops signing too; once it is gone the account is clear again', async () => {
    chain.foreignPending(HOT.address);
    const { service } = build();
    await expect(service.payout(payout())).rejects.toMatchObject({
      code: 'JOURNAL_BEHIND_CHAIN',
    });
    expect(chain.sent).toHaveLength(0);
    expect(service.journalBehindChain()).toHaveLength(1);

    chain.pendingForeign.clear(); // dropped from the mempool
    expect((await service.payout(payout())).nonce).toBe(0);
    expect(service.journalBehindChain()).toEqual([]);
  });

  it('its own journalled pending send is not "behind": the next send takes the next nonce', async () => {
    const { service } = build();
    await service.payout(payout(1));
    expect((await service.payout(payout(2))).nonce).toBe(1);
    expect(service.journalBehindChain()).toEqual([]);
  });

  it('another account being behind does not stop this one', async () => {
    chain.foreignSend(DEPOSIT.address);
    const { service } = build();
    expect((await service.payout(payout())).replayed).toBe(false);
  });

  it('an acknowledgement ABOVE the chain nonce (typo) is ignored: after a lost journal the same id is NOT signed again (yönetici kararı (a))', async () => {
    const first = build();
    await first.service.payout(payout());
    chain.mine(); // nonce 0 used on chain
    rmSync(dir, { recursive: true, force: true });
    dir = mkdtempSync(join(tmpdir(), 'eth-transfer-lost-'));

    const { service } = build({ acknowledged: new Map([[canonicalAddress(HOT.address), 1000]]) });
    await expect(service.payout(payout())).rejects.toMatchObject({
      code: 'JOURNAL_BEHIND_CHAIN',
    });
    expect(chain.sent).toHaveLength(1);
    expect(chain.token(USDT, SELLER)).toBe(12_345_678n);
    expect(service.journalBehindChain()).toEqual([
      expect.objectContaining({ chainNonce: 1, journalNext: 0 }),
    ]);
    expect(service.acknowledgementsAheadOfChain()).toEqual([
      expect.objectContaining({
        from: canonicalAddress(HOT.address),
        acknowledged: 1000,
        chainNonce: 1,
      }),
    ]);
  });

  it('an acknowledgement equal to the chain nonce opens the account and is not reported as ahead', async () => {
    chain.foreignSend(HOT.address);
    chain.foreignSend(HOT.address);
    const { service } = build({ acknowledged: new Map([[canonicalAddress(HOT.address), 2]]) });
    expect((await service.payout(payout())).nonce).toBe(2);
    expect(service.acknowledgementsAheadOfChain()).toEqual([]);
  });

  it('an acknowledgement above the chain nonce with an intact journal signs normally, is reported, and clears once the chain reaches it', async () => {
    const { service } = build({ acknowledged: new Map([[canonicalAddress(HOT.address), 1]]) });
    expect((await service.payout(payout(1))).nonce).toBe(0);
    expect(service.journalBehindChain()).toEqual([]);
    expect(service.acknowledgementsAheadOfChain()).toHaveLength(1);

    chain.mine(); // chain nonce 1 = the acknowledgement
    expect((await service.payout(payout(2))).nonce).toBe(1);
    expect(service.acknowledgementsAheadOfChain()).toEqual([]);
  });

  it('an acknowledgement below the chain nonce does not open it', async () => {
    chain.foreignSend(HOT.address);
    chain.foreignSend(HOT.address);
    const { service } = build({ acknowledged: new Map([[canonicalAddress(HOT.address), 1]]) });
    await expect(service.payout(payout())).rejects.toMatchObject({
      code: 'JOURNAL_BEHIND_CHAIN',
    });
  });
});

describe('a success without the asked Transfer event is not a payment (M31)', () => {
  it('status 1 but no Transfer log → NO_TRANSFER_LOG, final', async () => {
    const { service } = build();
    const { txHash } = await service.payout(payout());
    chain.dropTransferLogs = true;
    chain.mine();
    finalizeAll();
    expect(await service.status(txHash)).toMatchObject({
      contractRet: 'NO_TRANSFER_LOG',
      finalized: true,
    });
  });

  it.each([
    [
      'another amount',
      (l: Record<string, unknown>) => ({ ...l, data: '0x' + 1n.toString(16).padStart(64, '0') }),
    ],
    [
      'another recipient',
      (l: Record<string, unknown>) => ({
        ...l,
        topics: [
          (l.topics as string[])[0],
          (l.topics as string[])[1],
          '0x' + '0'.repeat(24) + '2'.repeat(40),
        ],
      }),
    ],
    [
      'another sender (M31)',
      (l: Record<string, unknown>) => ({
        ...l,
        topics: [
          (l.topics as string[])[0],
          '0x' + '0'.repeat(24) + '3'.repeat(40),
          (l.topics as string[])[2],
        ],
      }),
    ],
    ['another contract', (l: Record<string, unknown>) => ({ ...l, address: USDC })],
    ['a removed log', (l: Record<string, unknown>) => ({ ...l, removed: true })],
  ])('a Transfer log with %s does not count', async (_label, tamper) => {
    const { service } = build();
    const { txHash } = await service.payout(payout());
    chain.mine();
    finalizeAll();
    const receipt = chain.receipts.get(txHash)!;
    chain.receipts.set(txHash, {
      ...receipt,
      logs: receipt.logs.map((l) => tamper(l as unknown as Record<string, unknown>)) as never,
    });
    expect((await service.status(txHash)).contractRet).toBe('NO_TRANSFER_LOG');
  });
});

describe('daily cap window boundary — 7,200 blocks (M19)', () => {
  it('an outflow 7,199 blocks before the head is inside the window; 7,200 blocks before is outside', async () => {
    chain.head = 10_000n;
    chain.addOutflowLog(USDT, HOT.address, SELLER, 15_000_000_000n, 10_000n - 7_199n);
    const inside = build({ maxDaily: 20_000_000_000n, maxSingle: 20_000_000_000n });
    await expect(inside.service.payout(payout(1, { amount: '5000.000001' }))).rejects.toMatchObject(
      { code: 'DAILY_OUTFLOW_LIMIT_EXCEEDED' },
    );

    chain.logs.length = 0;
    chain.addOutflowLog(USDT, HOT.address, SELLER, 15_000_000_000n, 10_000n - 7_200n);
    const outside = build({ maxDaily: 20_000_000_000n, maxSingle: 20_000_000_000n });
    expect((await outside.service.payout(payout(2, { amount: '20000' }))).replayed).toBe(false);
  });
});

// ---- T158 (f) — refund after the sweep, from the hot wallet ------------------

describe('T158 (f) — a refund after the sweep leaves the hot wallet (08 §3.7.6 "Kaynaklar")', () => {
  function hotRefund(n: number, overrides: Record<string, unknown> = {}) {
    return {
      blockchainTransactionId: ID(n),
      source: 'HOT_WALLET' as const,
      depositIndex: null,
      depositAddress: null,
      toBuyerAddress: BUYER,
      amount: '10',
      token: 'USDT' as const,
      refundType: 'BUYER_REFUND' as const,
      feeCapUsd: '0.50',
      ...overrides,
    };
  }

  it('the hot key signs a USDT transfer to the buyer, with the whole cap (no fuel) — the empty deposit is not touched', async () => {
    const { service, journal } = build();
    const result = await service.refund(hotRefund(60));
    expect(result.replayed).toBe(false);
    const tx = Transaction.from(chain.sent[0]);
    expect(canonicalAddress(tx.from!)).toBe(canonicalAddress(HOT.address));
    expect(canonicalAddress(tx.to!)).toBe(USDT);
    expect(decodeTransferCall(tx.data)).toEqual({ to: BUYER, amountUnits: 10_000_000n });
    const record = journal.get(`transfer:${ID(60)}`)!;
    expect(record).toMatchObject({ purpose: 'REFUND', signer: 'HOT', depositIndex: null });
    // 0.50 $ / 2,364.48 $ — the payout's whole budget (64,031 / 64,031), not the deposit refund's share.
    expect(record.feeCapWei).toBe('211462985519014');
    expect(chain.sent).toHaveLength(1); // no fuel send
  });

  it('a retry with the same id replays; the same id as a DEPOSIT refund is IDEMPOTENCY_CONFLICT (no second signature)', async () => {
    chain.setToken(USDT, DEPOSIT.address, 50_000_000n);
    chain.setEth(DEPOSIT.address, 10n ** 16n);
    const { service } = build();
    const first = await service.refund(hotRefund(61));
    expect((await service.refund(hotRefund(61))).txHash).toBe(first.txHash);
    await expect(service.refund({ ...refundRequest(61) })).rejects.toMatchObject({
      code: 'IDEMPOTENCY_CONFLICT',
    });
    expect(new Set(chain.sent.map((raw) => Transaction.from(raw).hash)).size).toBe(1);
  });

  it('a DEPOSIT refund id asked again as HOT_WALLET is IDEMPOTENCY_CONFLICT', async () => {
    chain.setToken(USDT, DEPOSIT.address, 50_000_000n);
    chain.setEth(DEPOSIT.address, 10n ** 16n);
    const { service } = build();
    await service.refund(refundRequest(62));
    await expect(service.refund(hotRefund(62))).rejects.toMatchObject({
      code: 'IDEMPOTENCY_CONFLICT',
    });
  });

  it('a wrong-token refund never leaves the hot wallet (USDC is not swept) and USDC is refused', async () => {
    const { service } = build();
    await expect(
      service.refund(hotRefund(63, { refundType: 'WRONG_TOKEN_REFUND', token: 'USDC' })),
    ).rejects.toMatchObject({ code: 'TOKEN_NOT_ALLOWED' });
    await expect(service.refund(hotRefund(64, { token: 'USDC' }))).rejects.toMatchObject({
      code: 'TOKEN_NOT_ALLOWED',
    });
    expect(chain.sent).toHaveLength(0);
  });

  it('T158 düzeltme turu (S14): a WRONG_TOKEN_REFUND naming USDT is refused too — the refund type alone keeps it off the hot wallet', async () => {
    const { service, journal } = build();
    await expect(
      service.refund(hotRefund(68, { refundType: 'WRONG_TOKEN_REFUND', token: 'USDT' })),
    ).rejects.toMatchObject({ code: 'TOKEN_NOT_ALLOWED' });
    expect(chain.sent).toHaveLength(0);
    expect(journal.get(`transfer:${ID(68)}`)).toBeUndefined();
  });

  it('a HOT_WALLET refund naming a deposit is a bad request', async () => {
    const { service } = build();
    await expect(
      service.refund(hotRefund(65, { depositIndex: 5, depositAddress: DEPOSIT.address })),
    ).rejects.toMatchObject({ code: 'INVALID_TRANSFER_REQUEST' });
  });

  it("counts against the hot wallet's daily cap (chain + signed-not-seen), like a payout", async () => {
    const { service } = build({ maxDaily: 15_000_000n, maxSingle: 15_000_000n });
    await service.refund(hotRefund(66)); // 10 — signed, not mined
    await expect(service.payout(payout(67, { amount: '6' }))).rejects.toMatchObject({
      code: 'DAILY_OUTFLOW_LIMIT_EXCEEDED',
    });
    await expect(service.refund(hotRefund(68, { amount: '6' }))).rejects.toMatchObject({
      code: 'DAILY_OUTFLOW_LIMIT_EXCEEDED',
    });
  });

  it('respects the single-transfer cap and waits above the fee cap (no FAILED for a fee)', async () => {
    const capped = build({ maxSingle: 5_000_000n });
    await expect(capped.service.refund(hotRefund(69))).rejects.toMatchObject({
      code: 'TRANSFER_AMOUNT_ABOVE_LIMIT',
    });
    chain.baseFee = 50_000_000_000n;
    const { service } = build();
    await expect(service.refund(hotRefund(70))).rejects.toMatchObject({ reason: 'FEE_ABOVE_CAP' });
    expect(chain.sent).toHaveLength(0);
  });

  it('a hot wallet without ETH waits (HOT_WALLET_GAS_LOW) — no fuel is sent to it', async () => {
    chain.setEth(HOT.address, 0n);
    const { service } = build();
    await expect(service.refund(hotRefund(71))).rejects.toMatchObject({
      reason: 'HOT_WALLET_GAS_LOW',
    });
    expect(chain.sent).toHaveLength(0);
  });

  it('status checks the Transfer from the hot wallet to the buyer (NO_TRANSFER_LOG rule applies)', async () => {
    const { service } = build();
    const { txHash } = await service.refund(hotRefund(72));
    chain.mine();
    finalizeAll();
    expect(await service.status(txHash)).toMatchObject({ contractRet: 'SUCCESS', finalized: true });
  });
});

/**
 * T172b — the signer's 24-hour cap on refunds the DEPOSIT key signs (proje
 * sahibi kararı 2026-10-23; ETHEREUM_DEPOSIT_REFUND_DAILY_LIMIT_USDT). Counted
 * from the journal (on disk before the broadcast), one lock across deposits.
 */
describe('T172b — deposit refund daily cap', () => {
  const DEPOSIT_B = wallet.derive(6);
  const CAP = 100_000_000n; // 100 USDT

  function depositRefund(n: number, amount: string, deposit: 'A' | 'B' = 'A') {
    return {
      ...refundRequest(n),
      depositIndex: deposit === 'A' ? 5 : 6,
      depositAddress: deposit === 'A' ? DEPOSIT.address : DEPOSIT_B.address,
      amount,
    };
  }

  beforeEach(() => {
    for (const d of [DEPOSIT.address, DEPOSIT_B.address]) {
      chain.setToken(USDT, d, 500_000_000n);
      chain.setEth(d, 10n ** 17n);
    }
  });

  it('a refund that brings the 24 h total exactly to the cap is signed; one unit more waits — nothing signed, no fuel', async () => {
    const { service } = build({ maxDepositRefund: CAP });
    await service.refund(depositRefund(1, '60'));
    await service.refund(depositRefund(2, '40', 'B'));
    expect(chain.sent).toHaveLength(2);

    const err = await service.refund(depositRefund(3, '0.000001')).then(
      () => null,
      (e: unknown) => e,
    );
    expect(err).toBeInstanceOf(TransferDeferredError);
    expect(err).toMatchObject({
      reason: 'DEPOSIT_REFUND_DAILY_LIMIT_EXCEEDED',
      retryable: true,
    });
    expect((err as TransferDeferredError).waitingSince).toBe(clock);
    expect(chain.sent).toHaveLength(2);
  });

  it('a single deposit refund above the cap never passes on its own', async () => {
    const { service } = build({ maxDepositRefund: CAP });
    await expect(service.refund(depositRefund(1, '100.000001'))).rejects.toMatchObject({
      reason: 'DEPOSIT_REFUND_DAILY_LIMIT_EXCEEDED',
    });
    expect(chain.sent).toHaveLength(0);
  });

  it('room comes back only when the earlier refund turns 24 hours old (rolling, by journal time)', async () => {
    const { service } = build({ maxDepositRefund: CAP });
    await service.refund(depositRefund(1, '80'));
    clock += 24 * 60 * 60 * 1000 - 1;
    await expect(service.refund(depositRefund(2, '30', 'B'))).rejects.toMatchObject({
      reason: 'DEPOSIT_REFUND_DAILY_LIMIT_EXCEEDED',
    });
    clock += 2;
    await service.refund(depositRefund(2, '30', 'B'));
    expect(chain.sent).toHaveLength(2);
  });

  it('a restart does not reset it: the journal on disk still counts', async () => {
    await build({ maxDepositRefund: CAP }).service.refund(depositRefund(1, '90'));
    const restarted = build({ maxDepositRefund: CAP }).service; // same journal directory
    await expect(restarted.refund(depositRefund(2, '10.000001', 'B'))).rejects.toMatchObject({
      reason: 'DEPOSIT_REFUND_DAILY_LIMIT_EXCEEDED',
    });
    await restarted.refund(depositRefund(3, '10', 'B'));
  });

  it('two refunds from two deposits at once cannot both pass (60 + 60 against 100)', async () => {
    const { service, journal } = build({ maxDepositRefund: CAP });
    const results = await Promise.allSettled([
      service.refund(depositRefund(1, '60')),
      service.refund(depositRefund(2, '60', 'B')),
    ]);
    expect(results.map((r) => r.status).sort()).toEqual(['fulfilled', 'rejected']);
    const rejected = results.find((r) => r.status === 'rejected') as PromiseRejectedResult;
    expect(rejected.reason).toMatchObject({ reason: 'DEPOSIT_REFUND_DAILY_LIMIT_EXCEEDED' });
    expect(chain.sent).toHaveLength(1);
    expect(journal.all().filter((r) => r.purpose === 'REFUND')).toHaveLength(1);
  });

  it('an id already signed is replayed even when the window is full (it is already counted)', async () => {
    const { service } = build({ maxDepositRefund: CAP });
    const first = await service.refund(depositRefund(1, '100'));
    const again = await service.refund(depositRefund(1, '100'));
    expect(again).toEqual({ ...first, replayed: true });
  });

  it('sweeps and hot-wallet refunds are not deposit refunds; the hot-wallet one stays under the hot cap', async () => {
    const { service, journal } = build({ maxDepositRefund: CAP });
    await service.sweep({ ...sweepRequest(1), amount: '400' });
    await service.refund({
      ...refundRequest(2),
      source: 'HOT_WALLET' as const,
      depositIndex: null,
      depositAddress: null,
      amount: '400',
    });
    expect(journal.all()).toHaveLength(2);
    // The deposit refund window is still empty.
    await service.refund(depositRefund(3, '100', 'B'));
  });

  it('cap unset → every deposit refund waits (DEPOSIT_REFUND_LIMIT_NOT_CONFIGURED), nothing signed; sweeps and payouts unaffected', async () => {
    const { service } = build({ maxDepositRefund: null });
    await expect(service.refund(depositRefund(1, '1'))).rejects.toMatchObject({
      reason: 'DEPOSIT_REFUND_LIMIT_NOT_CONFIGURED',
      retryable: true,
    });
    expect(chain.sent).toHaveLength(0);
    await service.sweep(sweepRequest(2));
    await service.payout(payout(3));
  });
});

describe('T172b — what the deposit refund window counts (TransferGuard)', () => {
  const NOW = 1_800_000_000_000;
  const guardAt = () =>
    new TransferGuard({
      rpc: chain.rpc,
      hotWalletAddress: canonicalAddress(HOT.address),
      coldWalletAddress: null,
      tokenContracts: [USDT, USDC],
      maxSingleTransferUnits: null,
      maxDailyOutflowUnits: null,
      maxDailyDepositRefundUnits: 100_000_000n,
      now: () => NOW,
    });
  const record = (overrides: Record<string, unknown>) =>
    ({
      id: `transfer:${ID(1)}`,
      purpose: 'REFUND',
      refundType: 'BUYER_REFUND',
      signer: 'DEPOSIT',
      from: canonicalAddress(DEPOSIT.address),
      depositIndex: 5,
      contract: USDT,
      token: 'USDT',
      recipient: BUYER,
      amountUnits: '10000000',
      nonce: 0,
      gasLimit: '60000',
      attempts: [],
      createdAt: NOW - 1000,
      lastBroadcastAt: null,
      lastBroadcastError: null,
      mined: null,
      conflictSuspectedAt: null,
      conflict: null,
      ...overrides,
    }) as never;

  it('counts deposit-key REFUND records of the last 24 h only; reverted / conflicted moved nothing', () => {
    const journal = [
      record({}), // 10 — counts
      record({ token: 'USDC', amountUnits: '5000000' }), // 5 — USDC 1:1
      record({ mined: { status: 1, final: false } }), // 10 — mined OK, counts
      record({ signer: 'HOT' }), // hot-wallet refund — the hot cap's
      record({ purpose: 'SWEEP' }), // pinned to the hot wallet
      record({ createdAt: NOW - 24 * 60 * 60 * 1000 - 1 }), // left the window
      record({ mined: { status: 0, final: true } }), // reverted
      record({ conflict: { detectedAt: NOW, chainNonce: 1 } }), // nonce used elsewhere
    ];
    expect(guardAt().depositRefundsLast24h(journal)).toBe(25_000_000n);
  });
});

/**
 * T172b — the operator's one-time permit past the deposit refund cap (proje
 * sahibi kararı 2026-10-23, modal). Consumed only when the refund is signed
 * and journalled (a fuel / fee wait does not burn it); still counted.
 */
describe('T172b — a one-time permit past the deposit refund cap', () => {
  const CAP = 100_000_000n;
  const DEPOSIT_B = wallet.derive(6);
  let events: PermitEvent[];

  beforeEach(() => {
    events = [];
    for (const d of [DEPOSIT.address, DEPOSIT_B.address]) {
      chain.setToken(USDT, d, 5_000_000_000n);
      chain.setEth(d, 10n ** 17n);
    }
  });

  function permits(network: 'TRON' | 'ETHEREUM' = 'ETHEREUM') {
    return new DepositRefundPermitStore({
      baseDir: dir,
      network,
      isValidAddress: (a) => checksummedEvmAddress(a) !== null,
      now: () => clock,
      onEvent: (e) => events.push(e),
    });
  }

  function grant(overrides: Record<string, unknown> = {}) {
    return permits().create({
      blockchainTransactionId: ID(1),
      recipient: BUYER,
      amount: '500',
      createdBy: 'operator',
      reason: 'single refund above the cap',
      ...overrides,
    });
  }

  function refund500(n = 1, overrides: Record<string, unknown> = {}) {
    return { ...refundRequest(n), amount: '500', ...overrides };
  }

  it('the matching refund is signed once and counted; other refunds still meet the cap', async () => {
    const created = grant();
    const { service, journal } = build({ maxDepositRefund: CAP, permits: permits() });
    await service.refund(refund500());
    expect(chain.sent).toHaveLength(1);
    expect(journal.get(`transfer:${ID(1)}`)?.depositRefundPermitId).toBe(created.id);
    expect(permits().list()[0].consumedBy).toBe(`transfer:${ID(1)}`);
    expect(events).toEqual(expect.arrayContaining(['created', 'seen', 'used']));
    // The window now holds 500: a 1-unit refund from another deposit waits.
    await expect(
      service.refund({
        ...refundRequest(2),
        depositIndex: 6,
        depositAddress: DEPOSIT_B.address,
        amount: '0.000001',
      }),
    ).rejects.toMatchObject({ reason: 'DEPOSIT_REFUND_DAILY_LIMIT_EXCEEDED' });
  });

  it('single use: a second refund id with the same amount and buyer is not covered', async () => {
    grant();
    const { service } = build({ maxDepositRefund: CAP, permits: permits() });
    await service.refund(refund500());
    await expect(
      service.refund(refund500(2, { depositIndex: 6, depositAddress: DEPOSIT_B.address })),
    ).rejects.toMatchObject({ reason: 'DEPOSIT_REFUND_DAILY_LIMIT_EXCEEDED' });
    expect(chain.sent).toHaveLength(1);
  });

  it.each([
    ['another recipient', { toBuyerAddress: SELLER }],
    ['another amount', { amount: '499.999999' }],
    ['a larger amount', { amount: '500.000001' }],
  ])('%s is not covered — waits, nothing signed', async (_label, overrides) => {
    grant();
    const { service } = build({ maxDepositRefund: CAP, permits: permits() });
    await expect(service.refund(refund500(1, overrides))).rejects.toMatchObject({
      reason: 'DEPOSIT_REFUND_DAILY_LIMIT_EXCEEDED',
    });
    expect(chain.sent).toHaveLength(0);
  });

  it('another refund id (same deposit, buyer and amount) is not covered', async () => {
    grant();
    const { service } = build({ maxDepositRefund: CAP, permits: permits() });
    await expect(service.refund(refund500(2))).rejects.toMatchObject({
      reason: 'DEPOSIT_REFUND_DAILY_LIMIT_EXCEEDED',
    });
    expect(chain.sent).toHaveLength(0);
    expect(permits().list()[0].consumedAt).toBeNull();
  });

  it('a TRON permit is never honoured by the Ethereum signer', async () => {
    new DepositRefundPermitStore({
      baseDir: dir,
      network: 'TRON',
      isValidAddress: () => true,
      now: () => clock,
    }).create({
      blockchainTransactionId: ID(1),
      recipient: BUYER,
      amount: '500',
      createdBy: 'op',
      reason: 'wrong service',
    });
    const { service } = build({ maxDepositRefund: CAP, permits: permits() });
    await expect(service.refund(refund500())).rejects.toMatchObject({
      reason: 'DEPOSIT_REFUND_DAILY_LIMIT_EXCEEDED',
    });
  });

  it('an expired permit is refused and reported', async () => {
    grant({ validForMs: 60 * 60 * 1000 });
    clock += 60 * 60 * 1000;
    const { service } = build({ maxDepositRefund: CAP, permits: permits() });
    await expect(service.refund(refund500())).rejects.toMatchObject({
      reason: 'DEPOSIT_REFUND_DAILY_LIMIT_EXCEEDED',
    });
    expect(events).toContain('expired');
    expect(chain.sent).toHaveLength(0);
  });

  it('a wait before the signature (fee above the cap) does not burn the permit', async () => {
    grant();
    const { service } = build({ maxDepositRefund: CAP, permits: permits() });
    chain.baseFee = 50_000_000_000n;
    await expect(service.refund(refund500())).rejects.toMatchObject({ reason: 'FEE_ABOVE_CAP' });
    expect(permits().list()[0].consumedAt).toBeNull();
    chain.baseFee = 1_000_000_000n;
    await service.refund(refund500());
    expect(permits().list()[0].consumedAt).not.toBeNull();
  });

  it('the same refund asked twice at once: one signature, replayed for the second', async () => {
    grant();
    const { service } = build({ maxDepositRefund: CAP, permits: permits() });
    const [a, b] = await Promise.all([service.refund(refund500()), service.refund(refund500())]);
    expect(a.txHash).toBe(b.txHash);
    expect(new Set(chain.sent.map((raw) => Transaction.from(raw).hash)).size).toBe(1);
  });

  it('two different refunds at once with one permit: only the permitted one passes', async () => {
    grant();
    const { service } = build({ maxDepositRefund: CAP, permits: permits() });
    const results = await Promise.allSettled([
      service.refund(refund500()),
      service.refund(refund500(2, { depositIndex: 6, depositAddress: DEPOSIT_B.address })),
    ]);
    expect(results[0].status).toBe('fulfilled');
    expect(results[1].status).toBe('rejected');
    expect(chain.sent).toHaveLength(1);
  });
});

describe('T172b — the permit store and command are one shared file (parity)', () => {
  const read = (path: string) => readFileSync(path, 'utf8').replace(/\r\n/g, '\n');
  it.each([['transfer/DepositRefundPermits.ts'], ['cli/permitCli.ts']])(
    '%s is byte-identical in the TRON and the Ethereum service',
    (file) => {
      expect(read(resolve(__dirname, '..', file))).toBe(
        read(resolve(__dirname, '../../../sidecar-blockchain/src', file)),
      );
    },
  );

  it('no HTTP route can create a permit: the API layer never touches the permit store', () => {
    const apiDir = resolve(__dirname, '../api');
    for (const file of readdirSync(apiDir)) {
      if (!file.endsWith('.ts') || file.endsWith('.test.ts')) continue;
      expect(readFileSync(join(apiDir, file), 'utf8'), file).not.toMatch(
        /DepositRefundPermit|permitCli/,
      );
    }
  });
});
