import { describe, expect, it } from 'vitest';
import {
  AllProvidersFailedError,
  EthereumRpcClient,
  RpcError,
  isExecutionRevert,
} from './RpcClient.js';
import type { RpcProviderSettings } from '../config/index.js';

const SEPOLIA = 11155111;
const URL_TOKEN = 'tokenInUrlPath42';
const PRIMARY: RpcProviderSettings = {
  name: 'alchemy',
  url: `https://primary.example/v2/${URL_TOKEN}`,
  maxBlockRange: 10,
  maxAddressesPerQuery: 1000,
};
const BACKUP: RpcProviderSettings = {
  name: 'chainstack',
  url: 'https://backup.example/xyz',
  maxBlockRange: 100,
  maxAddressesPerQuery: 1000,
};

interface Call {
  provider: string;
  method: string;
  params: unknown[];
}

type Behaviour = (method: string, params: unknown[]) => unknown;

/**
 * A fake chain behind two fake providers. Each provider's behaviour decides
 * per call: return a JSON-RPC result, or throw / answer like a failing node.
 * Every call is recorded with the provider that received it.
 */
function fakeChain(behaviours: Record<string, Behaviour>, chainIds: Record<string, number> = {}) {
  const calls: Call[] = [];
  const fetchFn = (async (input: string | URL | Request, init?: RequestInit) => {
    const url = String(input);
    const provider = url.startsWith('https://primary') ? PRIMARY.name : BACKUP.name;
    const body = JSON.parse(String(init?.body)) as {
      id: number;
      method: string;
      params: unknown[];
    };
    calls.push({ provider, method: body.method, params: body.params });
    if (body.method === 'eth_chainId') {
      return jsonResponse({
        id: body.id,
        result: '0x' + (chainIds[provider] ?? SEPOLIA).toString(16),
      });
    }
    const outcome = behaviours[provider](body.method, body.params);
    if (outcome instanceof Response) return outcome;
    if (outcome instanceof Error) throw outcome;
    if (outcome && typeof outcome === 'object' && 'rpcError' in outcome) {
      return jsonResponse({ id: body.id, error: (outcome as { rpcError: unknown }).rpcError });
    }
    return jsonResponse({ id: body.id, result: outcome });
  }) as typeof fetch;
  return { calls, fetchFn };
}

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify({ jsonrpc: '2.0', ...(body as object) }), {
    status,
    headers: { 'content-type': 'application/json' },
  });
}

function connectionRefused(): Error {
  return Object.assign(new TypeError('fetch failed'), { cause: { code: 'ECONNREFUSED' } });
}

function client(fetchFn: typeof fetch, now: () => number = () => 1_000_000) {
  return new EthereumRpcClient({
    providers: [PRIMARY, BACKUP],
    chainId: SEPOLIA,
    timeoutMs: 1000,
    failoverCooldownMs: 30_000,
    fetchFn,
    now,
  });
}

const BLOCK = { number: '0x10', hash: '0xabc', timestamp: '0x65000000' };
const nonChainCalls = (calls: Call[]) => calls.filter((c) => c.method !== 'eth_chainId');

describe('EthereumRpcClient — primary + backup', () => {
  it('answers from the primary while it works', async () => {
    const chain = fakeChain({ alchemy: () => BLOCK, chainstack: () => BLOCK });
    const header = await client(chain.fetchFn).getBlockHeader('finalized');
    expect(header).toEqual({
      number: 16n,
      hash: '0xabc',
      timestamp: 0x65000000,
      provider: 'alchemy',
    });
    expect(nonChainCalls(chain.calls).map((c) => c.provider)).toEqual(['alchemy']);
  });

  it.each([
    ['connection refused', () => connectionRefused()],
    ['HTTP 503', () => new Response('upstream down', { status: 503 })],
    ['HTTP 429', () => new Response('Too Many Requests', { status: 429 })],
    ['-32005 rate limit', () => ({ rpcError: { code: -32005, message: 'rate limit exceeded' } })],
    [
      'archive refusal',
      () => new Response('Archive requests require a personal token', { status: 403 }),
    ],
  ])('falls over to the backup on %s', async (_label, failure) => {
    const chain = fakeChain({ alchemy: failure, chainstack: () => BLOCK });
    const header = await client(chain.fetchFn).getBlockHeader('finalized');
    expect(header.provider).toBe('chainstack');
  });

  it('does not fall over on an answer about the request (execution reverted)', async () => {
    const chain = fakeChain({
      alchemy: () => ({ rpcError: { code: 3, message: 'execution reverted' } }),
      chainstack: () => '0x',
    });
    await expect(
      client(chain.fetchFn).call({ to: '0x' + '1'.repeat(40), data: '0x' }, 5n),
    ).rejects.toMatchObject({
      kind: 'rpc',
      provider: 'alchemy',
    });
    expect(nonChainCalls(chain.calls).map((c) => c.provider)).toEqual(['alchemy']);
  });

  it('skips a failed primary for the cooldown, then tries it again', async () => {
    let now = 1_000_000;
    let primaryUp = false;
    const chain = fakeChain({
      alchemy: () => (primaryUp ? BLOCK : connectionRefused()),
      chainstack: () => BLOCK,
    });
    const rpc = client(chain.fetchFn, () => now);

    await rpc.getBlockHeader('finalized'); // primary fails, backup answers
    primaryUp = true;
    await rpc.getBlockHeader('finalized'); // inside the cooldown: backup first
    expect(nonChainCalls(chain.calls).map((c) => c.provider)).toEqual([
      'alchemy',
      'chainstack',
      'chainstack',
    ]);

    now += 30_001;
    const header = await rpc.getBlockHeader('finalized');
    expect(header.provider).toBe('alchemy');
  });

  it('throws AllProvidersFailedError when neither answers — without the URL or its key', async () => {
    const chain = fakeChain({
      alchemy: () => connectionRefused(),
      chainstack: () => connectionRefused(),
    });
    const err = await client(chain.fetchFn)
      .getBlockHeader('finalized')
      .catch((e: unknown) => e);
    expect(err).toBeInstanceOf(AllProvidersFailedError);
    expect((err as AllProvidersFailedError).failures.map((f) => f.provider)).toEqual([
      'alchemy',
      'chainstack',
    ]);
    expect((err as Error).message).not.toContain(URL_TOKEN);
    expect((err as Error).message).not.toContain('primary.example');
  });

  it('never uses a provider that is on another chain', async () => {
    const chain = fakeChain({ alchemy: () => BLOCK, chainstack: () => BLOCK }, { alchemy: 1 });
    const rpc = client(chain.fetchFn);
    expect((await rpc.getBlockHeader('finalized')).provider).toBe('chainstack');
    expect((await rpc.getBlockHeader('finalized')).provider).toBe('chainstack');
    // The mismatch is remembered: the primary is asked its chain once and
    // nothing else.
    expect(chain.calls.filter((c) => c.provider === 'alchemy').map((c) => c.method)).toEqual([
      'eth_chainId',
    ]);
  });
});

describe('EthereumRpcClient — explicit block numbers', () => {
  it('sends state reads at the given block number, never a tag', async () => {
    const chain = fakeChain({ alchemy: () => '0x2a', chainstack: () => '0x2a' });
    const rpc = client(chain.fetchFn);
    expect(await rpc.getBalance('0x' + 'a'.repeat(40), 26_214_908n)).toBe(42n);
    await rpc.call({ to: '0x' + 'b'.repeat(40), data: '0x1234' }, 255n);
    const params = nonChainCalls(chain.calls).map((c) => c.params);
    expect(params[0]).toEqual(['0x' + 'a'.repeat(40), '0x' + (26_214_908).toString(16)]);
    expect(params[1]).toEqual([{ to: '0x' + 'b'.repeat(40), data: '0x1234' }, '0xff']);
  });

  it('refuses a tag or a number where an explicit block number is required', async () => {
    const chain = fakeChain({ alchemy: () => '0x0', chainstack: () => '0x0' });
    const rpc = client(chain.fetchFn);
    // Callers outside TypeScript's reach (and casts) are refused at runtime.
    await expect(
      rpc.getBalance('0x' + 'a'.repeat(40), 'latest' as unknown as bigint),
    ).rejects.toMatchObject({
      code: 'RPC_IMPLICIT_BLOCK',
    });
    await expect(
      rpc.getBalance('0x' + 'a'.repeat(40), 5 as unknown as bigint),
    ).rejects.toMatchObject({
      code: 'RPC_IMPLICIT_BLOCK',
    });
    expect(nonChainCalls(chain.calls)).toEqual([]);
  });

  it('only accepts the finalized and latest tags for block headers', async () => {
    const chain = fakeChain({ alchemy: () => BLOCK, chainstack: () => BLOCK });
    const rpc = client(chain.fetchFn);
    await rpc.getBlockHeader(100n);
    expect(nonChainCalls(chain.calls)[0].params).toEqual(['0x64', false]);
    await expect(rpc.getBlockHeader('pending' as unknown as 'latest')).rejects.toMatchObject({
      code: 'RPC_IMPLICIT_BLOCK',
    });
  });
});

describe('EthereumRpcClient — eth_getLogs limits (08 §3.7.4)', () => {
  const TRANSFER = '0xddf252ad1be2c89b69c2b068fc378daa952ba7f163c4a11628f55a4df523b3ef';
  const USDT = '0x' + 'd'.repeat(40);

  function log(block: number, index: number) {
    return {
      address: USDT,
      topics: [TRANSFER],
      data: '0x01',
      blockNumber: '0x' + block.toString(16),
      blockHash: '0xbb',
      transactionHash: '0xtt' + block,
      logIndex: '0x' + index.toString(16),
    };
  }

  function ranges(calls: Call[], provider?: string) {
    return calls
      .filter((c) => c.method === 'eth_getLogs' && (!provider || c.provider === provider))
      .map((c) => {
        const f = c.params[0] as { fromBlock: string; toBlock: string };
        return [Number(f.fromBlock), Number(f.toBlock)];
      });
  }

  it('splits the range by the answering provider’s own limit', async () => {
    const chain = fakeChain({ alchemy: () => [], chainstack: () => [] });
    await client(chain.fetchFn).getLogs({
      address: [USDT],
      topics: [TRANSFER],
      fromBlock: 100n,
      toBlock: 131n,
    });
    // Alchemy Free: 10 blocks per request — 32 finalized blocks → 4 requests.
    expect(ranges(chain.calls)).toEqual([
      [100, 109],
      [110, 119],
      [120, 129],
      [130, 131],
    ]);
  });

  it('hands the rest of the range to the backup with the backup’s limit', async () => {
    let primaryCalls = 0;
    const chain = fakeChain({
      alchemy: () => (++primaryCalls === 1 ? [log(100, 0)] : connectionRefused()),
      chainstack: (_m, params) => {
        const f = params[0] as { fromBlock: string };
        return [log(Number(f.fromBlock), 3)];
      },
    });
    const logs = await client(chain.fetchFn).getLogs({
      address: [USDT],
      topics: [TRANSFER],
      fromBlock: 100n,
      toBlock: 250n,
    });
    expect(ranges(chain.calls, 'alchemy')).toEqual([
      [100, 109],
      [110, 119],
    ]);
    // Chainstack Developer: 100 blocks per request, from where Alchemy stopped.
    expect(ranges(chain.calls, 'chainstack')).toEqual([
      [110, 209],
      [210, 250],
    ]);
    expect(logs.map((l) => [l.blockNumber, l.logIndex])).toEqual([
      [100n, 0],
      [110n, 3],
      [210n, 3],
    ]);
  });

  it('splits a recipient list longer than 1,000 into parts of at most 1,000', async () => {
    const recipients = Array.from(
      { length: 2_345 },
      (_, i) => '0x' + i.toString(16).padStart(64, '0'),
    );
    const chain = fakeChain({ alchemy: () => [], chainstack: () => [] });
    await client(chain.fetchFn).getLogs({
      address: [USDT],
      topics: [TRANSFER, null, recipients],
      fromBlock: 1n,
      toBlock: 5n,
    });
    const parts = chain.calls
      .filter((c) => c.method === 'eth_getLogs')
      .map((c) => ((c.params[0] as { topics: unknown[] }).topics[2] as string[]).length);
    expect(parts).toEqual([1000, 1000, 345]);
    // Every recipient is asked about exactly once.
    const asked = chain.calls
      .filter((c) => c.method === 'eth_getLogs')
      .flatMap((c) => (c.params[0] as { topics: unknown[] }).topics[2] as string[]);
    expect(new Set(asked).size).toBe(2_345);
  });

  it('halves a range the provider still refuses as too large, then carries on', async () => {
    const chain = fakeChain({
      alchemy: (_m, params) => {
        const f = params[0] as { fromBlock: string; toBlock: string };
        const span = Number(f.toBlock) - Number(f.fromBlock) + 1;
        return span > 5
          ? {
              rpcError: {
                code: -32602,
                message:
                  'Log response size exceeded. You can make eth_getLogs requests with up to a 2K block range',
              },
            }
          : [];
      },
      chainstack: () => [],
    });
    await client(chain.fetchFn).getLogs({
      address: [USDT],
      topics: [TRANSFER],
      fromBlock: 0n,
      toBlock: 9n,
    });
    // [0,9] refused → halved to [0,4]; the next chunk starts again at the
    // configured width and fits.
    expect(ranges(chain.calls, 'alchemy')).toEqual([
      [0, 9],
      [0, 4],
      [5, 9],
    ]);
    expect(ranges(chain.calls, 'chainstack')).toEqual([]);
  });

  it('refuses a reversed range', async () => {
    const chain = fakeChain({ alchemy: () => [], chainstack: () => [] });
    await expect(
      client(chain.fetchFn).getLogs({ address: [USDT], topics: [], fromBlock: 10n, toBlock: 9n }),
    ).rejects.toMatchObject({ code: 'RPC_INVALID_RANGE' });
  });

  // The raw messages T142 measured (08 §3.7.4 "Sorgu", §3.7.8). Each is a limit
  // of the provider's plan, never an answer about the query: the client
  // narrows the request and, at one block, hands it to the other provider.
  it.each([
    'You can make eth_getLogs requests with up to a 10 block range',
    'eth_getLogs is limited to 0 - 50 blocks range',
    'ranges over 10000 blocks are not supported on free plan',
    'query exceeds the maximum of 1000 addresses or topics per search position',
    'Query returned more than 20000 results',
  ])('treats "%s" as a provider limit, not as an answer', async (message) => {
    const chain = fakeChain({
      alchemy: () => ({ rpcError: { code: -32602, message } }),
      chainstack: () => [log(3, 0)],
    });
    const logs = await client(chain.fetchFn).getLogs({
      address: [USDT],
      topics: [TRANSFER],
      fromBlock: 0n,
      toBlock: 9n,
    });
    expect(logs).toHaveLength(1);
    expect(ranges(chain.calls, 'chainstack')).toEqual([[0, 9]]);
    expect(new RpcError(message, 'limit', 'alchemy').retryable).toBe(true);
  });
});

/**
 * T152 düzeltme turu 2 (bulgu 2): which JSON-RPC answers are the EVM's revert
 * (permanent for a send) and which are about the node at that moment
 * (transient). The revert data a geth-style node puts in `error.data` is kept.
 */
describe('EthereumRpcClient — revert or node error (T152)', () => {
  const REVERT_DATA = '0x08c379a00000000000000000000000000000000000000000000000000000000000000020';

  it('keeps the error data of a revert answer and classifies it as a revert', async () => {
    const chain = fakeChain({
      alchemy: () => ({ rpcError: { code: 3, message: 'execution reverted', data: REVERT_DATA } }),
      chainstack: () => '0x',
    });
    const err = await client(chain.fetchFn)
      .call({ to: '0x' + '1'.repeat(40), data: '0x' }, 5n)
      .catch((e: unknown) => e);
    expect(err).toMatchObject({ kind: 'rpc', rpcCode: 3, rpcData: REVERT_DATA });
    expect(isExecutionRevert(err)).toBe(true);
  });

  it.each([
    ['code 3 alone', new RpcError('eth_call: error', 'rpc', 'p', 3), true],
    ['"execution reverted" text', new RpcError('eth_call: execution reverted', 'rpc', 'p'), true],
    [
      'Ganache / Hardhat wording',
      new RpcError(
        'eth_call: VM Exception while processing transaction: revert Pausable: paused',
        'rpc',
        'p',
        -32000,
      ),
      true,
    ],
    [
      '"revision id 3 cannot be reverted" (a node-side snapshot error)',
      new RpcError('eth_call: revision id 3 cannot be reverted', 'rpc', 'p', -32000),
      false,
    ],
    [
      '"upstream block reverted (reorg)" (a provider-side reorg)',
      new RpcError('eth_estimateGas: upstream block reverted (reorg)', 'rpc', 'p', -32000),
      false,
    ],
    [
      'revert data without the word',
      new RpcError('eth_call: vm error', 'rpc', 'p', -32000, '0x08c379a0'),
      true,
    ],
    ['header not found', new RpcError('eth_call: header not found', 'rpc', 'p', -32000), false],
    [
      'missing trie node',
      new RpcError('eth_call: missing trie node 1a2b', 'rpc', 'p', -32000),
      false,
    ],
    ['internal error', new RpcError('eth_estimateGas: internal error', 'rpc', 'p', -32603), false],
    ['empty data', new RpcError('eth_call: oops', 'rpc', 'p', -32000, '0x'), false],
    ['a transport failure', new RpcError('eth_call: execution reverted', 'transport', 'p'), false],
    ['not an RpcError', new Error('execution reverted'), false],
  ] as const)('%s → revert %s', (_label, err, revert) => {
    expect(isExecutionRevert(err)).toBe(revert);
  });

  it('getPendingTransactionCount asks for the pending count (the one read at a tag)', async () => {
    const chain = fakeChain({ alchemy: () => '0x7', chainstack: () => '0x0' });
    expect(await client(chain.fetchFn).getPendingTransactionCount('0x' + 'a'.repeat(40))).toBe(7);
    expect(nonChainCalls(chain.calls)).toEqual([
      {
        provider: 'alchemy',
        method: 'eth_getTransactionCount',
        params: ['0x' + 'a'.repeat(40), 'pending'],
      },
    ]);
  });
});
