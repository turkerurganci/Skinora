import { describe, it, expect, beforeEach } from 'vitest';
import { TronGridClient, TronGridHttpError } from './TronGridClient.js';

interface FetchInvocation {
  url: string;
  init?: RequestInit;
}

function buildFetchMock(scripted: Array<{ status: number; body: unknown; statusText?: string }>): {
  mock: typeof fetch;
  calls: FetchInvocation[];
} {
  const calls: FetchInvocation[] = [];
  let index = 0;
  const mock: typeof fetch = async (input, init) => {
    const url = typeof input === 'string' ? input : (input as URL).toString();
    calls.push({ url, init });
    const step = scripted[Math.min(index, scripted.length - 1)];
    index += 1;
    return new Response(JSON.stringify(step.body), {
      status: step.status,
      statusText: step.statusText ?? 'OK',
      headers: { 'content-type': 'application/json' },
    });
  };
  return { mock, calls };
}

describe('TronGridClient.listTrc20()', () => {
  let client: TronGridClient;
  let fetchMock: ReturnType<typeof buildFetchMock>;

  beforeEach(() => {
    fetchMock = buildFetchMock([
      {
        status: 200,
        body: {
          data: [
            {
              transaction_id: 'tx-1',
              token_info: { address: 'TUSDT', decimals: 6, symbol: 'USDT' },
              block_timestamp: 1_778_000_000_000,
              from: 'TFrom',
              to: 'TDeposit',
              type: 'Transfer',
              value: '100000000',
            },
          ],
          meta: { fingerprint: 'fp-1' },
        },
      },
    ]);
    client = new TronGridClient(
      'https://full.example',
      'https://solid.example',
      'apikey-fixture',
      fetchMock.mock,
    );
  });

  it('builds the phase 1 URL with contract_address filter and fingerprint', async () => {
    await client.listTrc20({
      address: 'TDeposit',
      contractAddress: 'TUSDT',
      fingerprint: 'cursor-A',
      limit: 20,
    });
    expect(fetchMock.calls).toHaveLength(1);
    const url = fetchMock.calls[0].url;
    expect(url).toContain('https://full.example/v1/accounts/TDeposit/transactions/trc20?');
    expect(url).toContain('only_confirmed=true');
    expect(url).toContain('limit=20');
    expect(url).toContain('contract_address=TUSDT');
    expect(url).toContain('fingerprint=cursor-A');
  });

  it('builds the phase 2 URL without contract_address', async () => {
    await client.listTrc20({ address: 'TDeposit', limit: 20 });
    const url = fetchMock.calls[0].url;
    expect(url).not.toContain('contract_address');
  });

  it('returns records and meta.fingerprint', async () => {
    const result = await client.listTrc20({ address: 'TDeposit', contractAddress: 'TUSDT' });
    expect(result.records).toHaveLength(1);
    expect(result.records[0].transaction_id).toBe('tx-1');
    expect(result.fingerprint).toBe('fp-1');
  });

  it('attaches the TRON-PRO-API-KEY header when configured', async () => {
    await client.listTrc20({ address: 'TDeposit' });
    const headers = (fetchMock.calls[0].init?.headers ?? {}) as Record<string, string>;
    expect(headers['TRON-PRO-API-KEY']).toBe('apikey-fixture');
  });

  it('omits the api key header when not configured', async () => {
    const noKeyMock = buildFetchMock([{ status: 200, body: { data: [], meta: {} } }]);
    const noKeyClient = new TronGridClient(
      'https://full.example',
      'https://solid.example',
      '',
      noKeyMock.mock,
    );
    await noKeyClient.listTrc20({ address: 'TDeposit' });
    const headers = (noKeyMock.calls[0].init?.headers ?? {}) as Record<string, string>;
    expect(headers['TRON-PRO-API-KEY']).toBeUndefined();
  });
});

describe('TronGridClient.getNowSolidBlock()', () => {
  it('returns the block_header.raw_data.number from walletsolidity/getnowblock', async () => {
    const { mock, calls } = buildFetchMock([
      {
        status: 200,
        body: { block_header: { raw_data: { number: 82_000_000 } } },
      },
    ]);
    const client = new TronGridClient('https://full', 'https://solid', '', mock);
    const block = await client.getNowSolidBlock();
    expect(block).toBe(82_000_000);
    expect(calls[0].url).toBe('https://solid/walletsolidity/getnowblock');
    expect(calls[0].init?.method).toBe('POST');
  });

  it('throws when the response is malformed', async () => {
    const { mock } = buildFetchMock([{ status: 200, body: { not: 'expected' } }]);
    const client = new TronGridClient('https://full', 'https://solid', '', mock);
    await expect(client.getNowSolidBlock()).rejects.toThrow(/no block number/);
  });
});

describe('TronGridClient.getTransactionInfoById()', () => {
  it('returns blockNumber and receipt result on success', async () => {
    const { mock } = buildFetchMock([
      {
        status: 200,
        body: {
          id: 'tx-info-1',
          blockNumber: 1_500_000,
          receipt: { result: 'SUCCESS' },
        },
      },
    ]);
    const client = new TronGridClient('https://full', 'https://solid', '', mock);
    const info = await client.getTransactionInfoById('tx-info-1');
    expect(info).toEqual({ blockNumber: 1_500_000, contractRet: 'SUCCESS' });
  });

  it('returns null when the solidity node has no record (empty body)', async () => {
    const { mock } = buildFetchMock([{ status: 200, body: {} }]);
    const client = new TronGridClient('https://full', 'https://solid', '', mock);
    expect(await client.getTransactionInfoById('tx-missing')).toBeNull();
  });

  it('returns undefined blockNumber when tx is still pending on solid', async () => {
    const { mock } = buildFetchMock([{ status: 200, body: { id: 'tx-pending' } }]);
    const client = new TronGridClient('https://full', 'https://solid', '', mock);
    const info = await client.getTransactionInfoById('tx-pending');
    expect(info?.blockNumber).toBeUndefined();
  });
});

describe('TronGridClient error paths', () => {
  it('raises TronGridHttpError on non-2xx responses', async () => {
    const { mock } = buildFetchMock([
      { status: 429, body: { error: 'rate' }, statusText: 'Too Many Requests' },
    ]);
    const client = new TronGridClient('https://full', 'https://solid', '', mock);
    await expect(client.listTrc20({ address: 'TDeposit' })).rejects.toBeInstanceOf(
      TronGridHttpError,
    );
  });
});
