import { describe, expect, it } from 'vitest';
import { EthereumRpcClient, RpcError } from './RpcClient.js';
import { SidecarError } from '../errors/SidecarError.js';
import type { RpcProviderSettings } from '../config/index.js';

/** T154 — the two reads the fee estimate adds to the client. */

const SEPOLIA = 11155111;
const PRIMARY: RpcProviderSettings = {
  name: 'alchemy',
  url: 'https://primary.example/v2/key',
  maxBlockRange: 10,
  maxAddressesPerQuery: 1000,
};
const BACKUP: RpcProviderSettings = {
  name: 'chainstack',
  url: 'https://backup.example/xyz',
  maxBlockRange: 100,
  maxAddressesPerQuery: 1000,
};

type Answer = { result: unknown } | { error: { code: number; message: string } } | Error;

function chain(answers: Record<string, (method: string, params: unknown[]) => Answer>) {
  const calls: Array<{ provider: string; method: string; params: unknown[] }> = [];
  const fetchFn = (async (input: string | URL | Request, init?: RequestInit) => {
    const provider = String(input).startsWith('https://primary') ? 'alchemy' : 'chainstack';
    const body = JSON.parse(String(init?.body)) as {
      id: number;
      method: string;
      params: unknown[];
    };
    calls.push({ provider, method: body.method, params: body.params });
    const answer: Answer =
      body.method === 'eth_chainId'
        ? { result: '0x' + SEPOLIA.toString(16) }
        : answers[provider](body.method, body.params);
    if (answer instanceof Error) throw answer;
    return new Response(JSON.stringify({ jsonrpc: '2.0', id: body.id, ...answer }), {
      status: 200,
      headers: { 'content-type': 'application/json' },
    });
  }) as typeof fetch;
  const client = new EthereumRpcClient({
    providers: [PRIMARY, BACKUP],
    chainId: SEPOLIA,
    timeoutMs: 1000,
    failoverCooldownMs: 30_000,
    fetchFn,
  });
  return { client, calls };
}

const refused = () =>
  Object.assign(new TypeError('fetch failed'), { cause: { code: 'ECONNREFUSED' } });
const TX = {
  from: '0x9858effd232b4033e47d90003d41ec34ecaeda94',
  to: '0xdac17f958d2ee523a2206206994597c13d831ec7',
  data: '0xa9059cbb',
};

describe('estimateGas', () => {
  it('sends the call with an explicit block number and no gas price', async () => {
    const { client, calls } = chain({
      alchemy: () => ({ result: '0xb479' }),
      chainstack: () => refused(),
    });
    expect(await client.estimateGas(TX, 26_215_000n)).toBe(46_201n);
    const call = calls.find((c) => c.method === 'eth_estimateGas');
    expect(call?.params).toEqual([
      { from: TX.from, to: TX.to, data: TX.data },
      '0x' + (26_215_000).toString(16),
    ]);
  });

  it('refuses a block tag', async () => {
    const { client } = chain({
      alchemy: () => ({ result: '0x1' }),
      chainstack: () => ({ result: '0x1' }),
    });
    await expect(client.estimateGas(TX, 'latest' as unknown as bigint)).rejects.toSatisfy(
      (e: unknown) => e instanceof SidecarError && e.code === 'RPC_IMPLICIT_BLOCK',
    );
  });

  it('a revert is the answer (rpc kind) — the backup is not asked', async () => {
    const { client, calls } = chain({
      alchemy: () => ({ error: { code: 3, message: 'execution reverted' } }),
      chainstack: () => ({ result: '0x1' }),
    });
    await expect(client.estimateGas(TX, 1n)).rejects.toSatisfy(
      (e: unknown) => e instanceof RpcError && e.kind === 'rpc',
    );
    expect(calls.some((c) => c.provider === 'chainstack')).toBe(false);
  });

  it('falls over to the backup on a transport failure', async () => {
    const { client } = chain({
      alchemy: () => refused(),
      chainstack: () => ({ result: '0x5208' }),
    });
    expect(await client.estimateGas(TX, 1n)).toBe(21_000n);
  });

  it.each([[12345], ['12345'], [null], ['0x']])(
    'a malformed quantity %j is not a number of gas — the backup answers',
    async (bad) => {
      const { client } = chain({
        alchemy: () => ({ result: bad }),
        chainstack: () => ({ result: '0xb479' }),
      });
      expect(await client.estimateGas(TX, 1n)).toBe(46_201n);
    },
  );
});

describe('feeHistory', () => {
  it('decodes base fees (n + 1) and rewards to bigints, exactly', async () => {
    const { client, calls } = chain({
      alchemy: () => ({
        result: {
          oldestBlock: '0x1',
          baseFeePerGas: ['0x9502f900', '0x9f437f40', '0x1fffffffffffff1'],
          gasUsedRatio: [0.5, 0.4],
          reward: [['0x2faf080'], ['0x1c9c380']],
        },
      }),
      chainstack: () => refused(),
    });
    const h = await client.feeHistory(2, 100n, [50]);
    expect(h.baseFeePerGas).toEqual([2_500_000_000n, 2_672_000_832n, 0x1fffffffffffff1n]);
    expect(h.reward).toEqual([[50_000_000n], [30_000_000n]]);
    expect(calls.find((c) => c.method === 'eth_feeHistory')?.params).toEqual(['0x2', '0x64', [50]]);
  });

  it('an answer without base fees is a provider fault — the backup answers', async () => {
    const { client } = chain({
      alchemy: () => ({ result: { baseFeePerGas: [] } }),
      chainstack: () => ({ result: { baseFeePerGas: ['0x1', '0x2'], reward: [['0x3']] } }),
    });
    const h = await client.feeHistory(1, 5n, [50]);
    expect(h.baseFeePerGas).toEqual([1n, 2n]);
  });
});
