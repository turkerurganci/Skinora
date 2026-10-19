import { describe, expect, it } from 'vitest';
import { id } from 'ethers';
import { TransferVerifier, type VerifyRequest } from './TransferVerifier.js';
import { TRANSFER_TOPIC, addressTopic } from './erc20.js';
import type { RpcLog, TransactionReceipt } from '../rpc/RpcClient.js';

/**
 * T157 — what makes a chain hash "the payment this row stands for": a
 * successful transaction with a non-removed Transfer log from the token's
 * contract, to the expected recipient, for exactly the expected amount (and
 * from the expected sender when it is known). Every other shape is a refusal;
 * an unreadable node is an error, never a pass.
 */

const USDT = '0xdac17f958d2ee523a2206206994597c13d831ec7';
const USDC = '0xa0b86991c6218b36c1d19d4a2e9eb0ce3606eb48';
const HOT = '0x1111111111111111111111111111111111111111';
const SELLER = '0x2222222222222222222222222222222222222222';
const STRANGER = '0x3333333333333333333333333333333333333333';
const HASH = '0x' + 'ab'.repeat(32);
const AMOUNT = 98_200_000n; // 98.2 USDT, 6 decimals

function log(over: Partial<RpcLog> & { from?: string; to?: string; value?: bigint } = {}): RpcLog {
  return {
    address: over.address ?? USDT,
    topics: over.topics ?? [
      TRANSFER_TOPIC,
      addressTopic(over.from ?? HOT),
      addressTopic(over.to ?? SELLER),
    ],
    data: over.data ?? '0x' + (over.value ?? AMOUNT).toString(16).padStart(64, '0'),
    blockNumber: 100n,
    blockHash: '0x' + '01'.repeat(32),
    transactionHash: HASH,
    logIndex: 0,
    removed: over.removed ?? false,
  };
}

function receipt(logs: RpcLog[], status: number | null = 1): TransactionReceipt {
  return {
    transactionHash: HASH,
    blockNumber: 100n,
    blockHash: '0x' + '01'.repeat(32),
    status,
    from: HOT,
    to: USDT,
    gasUsed: 50_000n,
    effectiveGasPrice: 1n,
    logs,
  };
}

function verifier(result: TransactionReceipt | null | Error) {
  const reads: string[] = [];
  const v = new TransferVerifier({
    rpc: {
      getTransactionReceipt: async (hash: string) => {
        reads.push(hash);
        if (result instanceof Error) throw result;
        return result;
      },
    },
    tokenContracts: { USDT, USDC },
  });
  return { v, reads };
}

const BASE: VerifyRequest = {
  txHash: HASH,
  token: 'USDT',
  from: HOT,
  to: SELLER,
  amountUnits: AMOUNT,
};

describe('TransferVerifier', () => {
  it('matches the exact transfer, whatever the address letter case', async () => {
    const { v, reads } = verifier(receipt([log()]));

    const result = await v.verify({
      ...BASE,
      from: HOT.toUpperCase().replace('0X', '0x'),
      to: '0x2222222222222222222222222222222222222222',
    });

    expect(result).toEqual({ outcome: 'MATCH', blockNumber: 100, matchingTransfers: 1 });
    expect(reads).toEqual([HASH]);
  });

  it('finds the transfer among unrelated logs of the same transaction', async () => {
    const { v } = verifier(
      receipt([log({ to: STRANGER }), log({ address: USDC }), log(), log({ value: 1n })]),
    );

    expect((await v.verify(BASE)).outcome).toBe('MATCH');
  });

  it.each([
    ['another recipient', { to: STRANGER }],
    ['another sender', { from: STRANGER }],
    ['one unit less', { value: AMOUNT - 1n }],
    ['one unit more', { value: AMOUNT + 1n }],
    ['another token contract', { address: USDC }],
    ['a removed (reorged-out) log', { removed: true }],
    ['data that is not one word', { data: '0x' + '00'.repeat(31) }],
    ['a log with a non-Transfer topic', { topics: ['0x' + 'ee'.repeat(32)] }],
    [
      'an ERC-721-shaped Transfer (four topics)',
      {
        topics: [
          TRANSFER_TOPIC,
          addressTopic(HOT),
          addressTopic(SELLER),
          '0x' + '00'.repeat(31) + '01',
        ],
      },
    ],
  ] as const)('refuses %s', async (_label, over) => {
    const { v } = verifier(receipt([log(over)]));

    const result = await v.verify(BASE);

    expect(result.outcome).toBe('NO_MATCHING_TRANSFER');
    expect(result.matchingTransfers).toBe(0);
  });

  it('does not check the sender when none is given (the backend may not know it)', async () => {
    const { v } = verifier(receipt([log({ from: STRANGER })]));

    expect((await v.verify({ ...BASE, from: null })).outcome).toBe('MATCH');
    expect((await v.verify(BASE)).outcome).toBe('NO_MATCHING_TRANSFER');
  });

  it('does not count an Approval event of the same shape as a transfer (T157 düzeltme turu, S7)', async () => {
    // Approval(owner, spender, value) has three topics and one data word, like
    // Transfer — only the event topic tells them apart. The owner approving the
    // seller for the exact amount moves nothing.
    const approval = log({
      topics: [id('Approval(address,address,uint256)'), addressTopic(HOT), addressTopic(SELLER)],
    });
    const { v } = verifier(receipt([approval]));

    const result = await v.verify(BASE);

    expect(result.outcome).toBe('NO_MATCHING_TRANSFER');
    expect(result.matchingTransfers).toBe(0);
  });

  it('reads the contract of the token asked for — a USDC log does not satisfy a USDT row', async () => {
    const { v } = verifier(receipt([log({ address: USDC })]));

    expect((await v.verify({ ...BASE, token: 'USDT' })).outcome).toBe('NO_MATCHING_TRANSFER');
    expect((await v.verify({ ...BASE, token: 'USDC' })).outcome).toBe('MATCH');
  });

  it('says REVERTED for a failed transaction even if a matching log is present', async () => {
    const { v } = verifier(receipt([log()], 0));

    expect((await v.verify(BASE)).outcome).toBe('REVERTED');
  });

  it('treats a missing status as not successful', async () => {
    const { v } = verifier(receipt([log()], null));

    expect((await v.verify(BASE)).outcome).toBe('REVERTED');
  });

  it('says NOT_FOUND when the node does not know the hash', async () => {
    const { v } = verifier(null);

    expect(await v.verify(BASE)).toEqual({
      outcome: 'NOT_FOUND',
      blockNumber: null,
      matchingTransfers: 0,
    });
  });

  it('lets a node error through — never a pass', async () => {
    const { v } = verifier(new Error('node down'));

    await expect(v.verify(BASE)).rejects.toThrow('node down');
  });
});
