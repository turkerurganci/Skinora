import { describe, it, expect, beforeEach } from 'vitest';
import TronWeb from 'tronweb';
import {
  TronGridClient,
  TronGridHttpError,
  TronGridRateLimitError,
  extractTransferLogEntries,
} from './TronGridClient.js';

/** Skip real backoff timers in tests. */
const noSleep = async (): Promise<void> => {};

interface FetchInvocation {
  url: string;
  init?: RequestInit;
}

function apiKeyOf(call: FetchInvocation): string | undefined {
  return (call.init?.headers as Record<string, string> | undefined)?.['TRON-PRO-API-KEY'];
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
  it('raises TronGridRateLimitError when every key stays 429 after the retry budget', async () => {
    const { mock } = buildFetchMock([
      { status: 429, body: { error: 'rate' }, statusText: 'Too Many Requests' },
    ]);
    const client = new TronGridClient('https://full', 'https://solid', '', mock, {
      maxRetries: 2,
      sleepFn: noSleep,
    });
    const error = await client.listTrc20({ address: 'TDeposit' }).catch((e: unknown) => e);
    expect(error).toBeInstanceOf(TronGridRateLimitError);
    // TronGridRateLimitError extends TronGridHttpError so legacy checks still match.
    expect(error).toBeInstanceOf(TronGridHttpError);
  });

  it('raises a non-retryable TronGridHttpError immediately on 4xx (not 429/403)', async () => {
    const { mock, calls } = buildFetchMock([{ status: 400, body: {}, statusText: 'Bad Request' }]);
    const client = new TronGridClient('https://full', 'https://solid', '', mock, {
      sleepFn: noSleep,
    });
    const error = await client.listTrc20({ address: 'TDeposit' }).catch((e: unknown) => e);
    expect(error).toBeInstanceOf(TronGridHttpError);
    expect(error).not.toBeInstanceOf(TronGridRateLimitError);
    expect(calls).toHaveLength(1); // no retry on a non-retryable 4xx
  });
});

describe('TronGridClient resilience — 429/secondary-key failover + retry (WP10, 08 §3.5/§3.6)', () => {
  it('fails over to the secondary key immediately on a 429 (separate pool)', async () => {
    const { mock, calls } = buildFetchMock([
      { status: 429, body: { error: 'rate' }, statusText: 'Too Many Requests' },
      { status: 200, body: { data: [], meta: { fingerprint: null } } },
    ]);
    const client = new TronGridClient('https://full', 'https://solid', 'primary-key', mock, {
      apiKeySecondary: 'secondary-key',
      sleepFn: noSleep,
    });
    const result = await client.listTrc20({ address: 'TDeposit' });
    expect(result.records).toEqual([]);
    expect(calls).toHaveLength(2);
    expect(apiKeyOf(calls[0])).toBe('primary-key'); // first try, throttled
    expect(apiKeyOf(calls[1])).toBe('secondary-key'); // immediate failover
  });

  it('retries a 5xx on the same key with bounded backoff', async () => {
    const { mock, calls } = buildFetchMock([
      { status: 503, body: {}, statusText: 'Unavailable' },
      { status: 200, body: { block_header: { raw_data: { number: 1000 } } } },
    ]);
    const client = new TronGridClient('https://full', 'https://solid', 'only-key', mock, {
      maxRetries: 2,
      sleepFn: noSleep,
    });
    const block = await client.getNowSolidBlock();
    expect(block).toBe(1000);
    expect(calls).toHaveLength(2);
    expect(apiKeyOf(calls[0])).toBe('only-key'); // 5xx does NOT rotate keys
    expect(apiKeyOf(calls[1])).toBe('only-key');
  });

  it('gives up with TronGridRateLimitError once both keys are throttled past the budget', async () => {
    const { mock } = buildFetchMock([{ status: 429, body: {}, statusText: 'Too Many Requests' }]);
    const client = new TronGridClient('https://full', 'https://solid', 'k1', mock, {
      apiKeySecondary: 'k2',
      maxRetries: 1,
      sleepFn: noSleep,
    });
    await expect(client.getNowSolidBlock()).rejects.toBeInstanceOf(TronGridRateLimitError);
  });
});

describe('extractTransferLogEntries — on-chain event index resolution (WP10, 08 §3.4)', () => {
  const CONTRACT = 'TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t';
  const DEPOSIT = 'TJRyWwFs9wTFGZg3JbrVriFbNfCug5tDeC';
  const OTHER = 'TNPeeaaFB7K9cmo4uQpcU32zGK8G1NYqeL';
  const TRANSFER_TOPIC = 'ddf252ad1be2c89b69c2b068fc378daa952ba7f163c4a11628f55a4df523b3ef';

  function toTopicAddress(base58: string): string {
    return '0'.repeat(24) + TronWeb.address.toHex(base58).slice(2).toLowerCase();
  }

  function transferLog(contract: string, to: string, value: bigint) {
    return {
      address: TronWeb.address.toHex(contract),
      topics: [TRANSFER_TOPIC, toTopicAddress(OTHER), toTopicAddress(to)],
      data: value.toString(16).padStart(64, '0'),
    };
  }

  it('resolves index 0 + decoded value for a single matching transfer', () => {
    const entries = extractTransferLogEntries(
      [transferLog(CONTRACT, DEPOSIT, 100_500_000n)],
      CONTRACT,
      DEPOSIT,
    );
    expect(entries).toEqual([{ index: 0, value: '100500000' }]);
  });

  it('assigns the real log-array position when other events precede the transfer', () => {
    const entries = extractTransferLogEntries(
      [
        // index 0: an Approval (different topic) — ignored
        { address: TronWeb.address.toHex(CONTRACT), topics: ['deadbeef'], data: '00' },
        // index 1: a transfer to a different address — ignored
        transferLog(CONTRACT, OTHER, 5n),
        // index 2: our transfer
        transferLog(CONTRACT, DEPOSIT, 42n),
      ],
      CONTRACT,
      DEPOSIT,
    );
    expect(entries).toEqual([{ index: 2, value: '42' }]);
  });

  it('returns every matching transfer for a multi-transfer transaction', () => {
    const entries = extractTransferLogEntries(
      [transferLog(CONTRACT, DEPOSIT, 10n), transferLog(CONTRACT, DEPOSIT, 20n)],
      CONTRACT,
      DEPOSIT,
    );
    expect(entries).toEqual([
      { index: 0, value: '10' },
      { index: 1, value: '20' },
    ]);
  });

  it('ignores transfers from a different contract', () => {
    const entries = extractTransferLogEntries(
      [transferLog(OTHER, DEPOSIT, 99n)],
      CONTRACT,
      DEPOSIT,
    );
    expect(entries).toEqual([]);
  });
});

describe('TronGridClient.resolveTransferEventIndices()', () => {
  const CONTRACT = 'TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t';
  const DEPOSIT = 'TJRyWwFs9wTFGZg3JbrVriFbNfCug5tDeC';
  const TRANSFER_TOPIC = 'ddf252ad1be2c89b69c2b068fc378daa952ba7f163c4a11628f55a4df523b3ef';

  it('fetches gettransactioninfobyid logs and returns matching entries', async () => {
    const log = {
      address: TronWeb.address.toHex(CONTRACT),
      topics: [
        TRANSFER_TOPIC,
        '0'.repeat(64),
        '0'.repeat(24) + TronWeb.address.toHex(DEPOSIT).slice(2).toLowerCase(),
      ],
      data: 123n.toString(16).padStart(64, '0'),
    };
    const { mock, calls } = buildFetchMock([{ status: 200, body: { id: 'tx-1', log: [log] } }]);
    const client = new TronGridClient('https://full', 'https://solid', '', mock, {
      sleepFn: noSleep,
    });
    const entries = await client.resolveTransferEventIndices('tx-1', CONTRACT, DEPOSIT);
    expect(entries).toEqual([{ index: 0, value: '123' }]);
    expect(calls[0].url).toBe('https://solid/walletsolidity/gettransactioninfobyid');
  });

  it('returns [] when the lookup fails so the caller falls back to index 0', async () => {
    const { mock } = buildFetchMock([{ status: 500, body: {}, statusText: 'Server Error' }]);
    const client = new TronGridClient('https://full', 'https://solid', '', mock, {
      maxRetries: 0,
      sleepFn: noSleep,
    });
    const entries = await client.resolveTransferEventIndices('tx-x', CONTRACT, DEPOSIT);
    expect(entries).toEqual([]);
  });
});

describe('TronGridClient.getAccountBalances() — T76 reconciliation', () => {
  it('returns trx balance and trc20 contract→raw map from /v1/accounts/{addr}', async () => {
    const { mock, calls } = buildFetchMock([
      {
        status: 200,
        body: {
          data: [
            {
              address: 'TDeposit',
              balance: 123_456_789,
              trc20: [{ TUSDT: '100500000' }, { TUSDC: '50000000' }],
            },
          ],
        },
      },
    ]);
    const client = new TronGridClient('https://full', 'https://solid', '', mock);
    const snapshot = await client.getAccountBalances('TDeposit');
    expect(snapshot.address).toBe('TDeposit');
    expect(snapshot.trx).toBe('123456789');
    expect(snapshot.trc20).toEqual({ TUSDT: '100500000', TUSDC: '50000000' });
    expect(calls[0].url).toBe('https://full/v1/accounts/TDeposit');
    expect(calls[0].init?.method).toBe('GET');
  });

  it('returns zero balances when TronGrid reports data: []', async () => {
    const { mock } = buildFetchMock([{ status: 200, body: { data: [] } }]);
    const client = new TronGridClient('https://full', 'https://solid', '', mock);
    const snapshot = await client.getAccountBalances('TUntouched');
    expect(snapshot.trx).toBe('0');
    expect(snapshot.trc20).toEqual({});
  });

  it('coerces string balance fields into a stable string representation', async () => {
    const { mock } = buildFetchMock([
      { status: 200, body: { data: [{ balance: '99', trc20: [] }] } },
    ]);
    const client = new TronGridClient('https://full', 'https://solid', '', mock);
    const snapshot = await client.getAccountBalances('TDeposit');
    expect(snapshot.trx).toBe('99');
  });

  it('ignores malformed trc20 entries instead of throwing', async () => {
    const { mock } = buildFetchMock([
      {
        status: 200,
        body: {
          data: [
            {
              balance: 0,
              // Mix of valid and malformed: only the string-valued entries
              // should survive into the result map.
              trc20: [{ TUSDT: '100' }, null, { TNULL: null }, 'junk'],
            },
          ],
        },
      },
    ]);
    const client = new TronGridClient('https://full', 'https://solid', '', mock);
    const snapshot = await client.getAccountBalances('TDeposit');
    expect(snapshot.trc20).toEqual({ TUSDT: '100' });
  });

  it('raises TronGridHttpError on upstream 5xx after the retry budget', async () => {
    const { mock } = buildFetchMock([{ status: 503, body: {}, statusText: 'Unavailable' }]);
    const client = new TronGridClient('https://full', 'https://solid', '', mock, {
      maxRetries: 1,
      sleepFn: noSleep,
    });
    await expect(client.getAccountBalances('TDeposit')).rejects.toBeInstanceOf(TronGridHttpError);
  });
});
