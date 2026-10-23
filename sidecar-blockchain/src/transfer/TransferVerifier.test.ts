import { describe, expect, it } from 'vitest';
import TronWeb from 'tronweb';
import { TRANSFER_TOPIC, TransferVerifier, tronHex20 } from './TransferVerifier.js';

/**
 * T172a — the TRON chain check behind the two-person closure tool (07 §9.32,
 * DEPLOY_RUNBOOK §N.8). ⚠ PARA: only an exact, solidified, successful TRC-20
 * Transfer of the row's token, to the row's recipient, for the row's amount,
 * from the row's sender is a MATCH. Each rule has a case that breaks it.
 */

const USDT = 'TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t';
const USDC = 'TEkxiTehnzSmSe2XqrBj4w32RUN966rdz8';
const HOT = 'TLa2f6VPqDgRE67v1736s7bJ8Ray5wYjU7';
const SELLER = 'TNPeeaaFB7K9cmo4uQpcU32zGK8G1NYqeL';
const OTHER = 'TJRabPrwbZy45sbavfcjinPJC18kjpRTv8';
const ID = 'ab'.repeat(32);
const AMOUNT = 98_200_000n;

function topic(base58: string): string {
  return '0'.repeat(24) + TronWeb.address.toHex(base58).slice(2).toLowerCase();
}

function word(value: bigint): string {
  return value.toString(16).padStart(64, '0');
}

function log(
  overrides: Partial<{
    address: string;
    from: string;
    to: string;
    value: bigint;
    topics: string[];
    data: string;
  }> = {},
) {
  return {
    // TronGrid gives the emitting contract as 20-byte hex, without the 41 prefix.
    address: TronWeb.address
      .toHex(overrides.address ?? USDT)
      .slice(2)
      .toLowerCase(),
    topics: overrides.topics ?? [
      TRANSFER_TOPIC,
      topic(overrides.from ?? HOT),
      topic(overrides.to ?? SELLER),
    ],
    data: overrides.data ?? word(overrides.value ?? AMOUNT),
  };
}

function info(overrides: Record<string, unknown> = {}) {
  return {
    id: ID,
    blockNumber: 61_234_567,
    receipt: { result: 'SUCCESS' },
    log: [log()],
    ...overrides,
  };
}

function verifier(answer: unknown, status = 200, calls: string[] = []) {
  const fetchFn = (async (url: string, init?: RequestInit) => {
    calls.push(`${url} ${String(init?.body)}`);
    return new Response(JSON.stringify(answer), { status });
  }) as unknown as typeof fetch;
  return new TransferVerifier({
    solidityUrl: 'https://solidity.test',
    apiKey: 'k',
    tokenContracts: { USDT, USDC },
    fetchFn,
  });
}

const request = { txHash: ID, token: 'USDT' as const, from: HOT, to: SELLER, amountUnits: AMOUNT };

describe('TransferVerifier (TRON)', () => {
  it('MATCH for the exact transfer, read from the SOLIDIFIED node', async () => {
    const calls: string[] = [];
    const result = await verifier(info(), 200, calls).verify(request);

    expect(result).toEqual({ outcome: 'MATCH', blockNumber: 61_234_567, matchingTransfers: 1 });
    expect(calls).toHaveLength(1);
    expect(calls[0]).toContain('https://solidity.test/walletsolidity/gettransactioninfobyid');
    expect(calls[0]).toContain(ID);
  });

  it('asks with the lowercase id whatever case it was given', async () => {
    const calls: string[] = [];
    await verifier(info(), 200, calls).verify({ ...request, txHash: ID.toUpperCase() });
    expect(calls[0]).toContain(ID);
  });

  it.each([
    ['one unit more', { value: AMOUNT + 1n }],
    ['one unit less', { value: AMOUNT - 1n }],
    ['another recipient', { to: OTHER }],
    ['another sender', { from: OTHER }],
    ['another token contract (USDC)', { address: USDC }],
  ])('NO_MATCHING_TRANSFER for %s', async (_name, overrides) => {
    const result = await verifier(info({ log: [log(overrides)] })).verify(request);
    expect(result.outcome).toBe('NO_MATCHING_TRANSFER');
    expect(result.matchingTransfers).toBe(0);
  });

  it('NO_MATCHING_TRANSFER when the log is not a Transfer event', async () => {
    const approval = log({
      topics: [
        '8c5be1e5ebec7d5bd14f71427d1e84f3' + 'dd0314c0f7b2291e5b200ac8c7c3b925',
        topic(HOT),
        topic(SELLER),
      ],
    });
    expect((await verifier(info({ log: [approval] })).verify(request)).outcome).toBe(
      'NO_MATCHING_TRANSFER',
    );
  });

  it('NO_MATCHING_TRANSFER when the data is not exactly one word', async () => {
    const short = log({ data: AMOUNT.toString(16) });
    expect((await verifier(info({ log: [short] })).verify(request)).outcome).toBe(
      'NO_MATCHING_TRANSFER',
    );
  });

  it('NO_MATCHING_TRANSFER when there are no logs', async () => {
    expect((await verifier(info({ log: [] })).verify(request)).outcome).toBe(
      'NO_MATCHING_TRANSFER',
    );
    expect((await verifier(info({ log: undefined })).verify(request)).outcome).toBe(
      'NO_MATCHING_TRANSFER',
    );
  });

  it('without a sender, any sender matches (the backend did not know it)', async () => {
    const result = await verifier(info({ log: [log({ from: OTHER })] })).verify({
      ...request,
      from: null,
    });
    expect(result.outcome).toBe('MATCH');
  });

  it('USDC is checked against the USDC contract', async () => {
    const result = await verifier(info({ log: [log({ address: USDC })] })).verify({
      ...request,
      token: 'USDC',
    });
    expect(result.outcome).toBe('MATCH');
  });

  it('counts several equal transfers in one transaction', async () => {
    const result = await verifier(info({ log: [log(), log()] })).verify(request);
    expect(result.matchingTransfers).toBe(2);
  });

  it.each([
    ['REVERT receipt', { receipt: { result: 'REVERT' } }],
    ['OUT_OF_ENERGY receipt', { receipt: { result: 'OUT_OF_ENERGY' } }],
    ['no receipt result', { receipt: {} }],
    ['top-level FAILED', { result: 'FAILED' }],
  ])('REVERTED for a %s — even with a matching-looking log', async (_name, overrides) => {
    const result = await verifier(info(overrides)).verify(request);
    expect(result.outcome).toBe('REVERTED');
    expect(result.matchingTransfers).toBe(0);
  });

  it('NOT_FOUND for an id the solidified node does not know ({})', async () => {
    expect(await verifier({}).verify(request)).toEqual({
      outcome: 'NOT_FOUND',
      blockNumber: null,
      matchingTransfers: 0,
    });
  });

  it('throws (→ 502, never a pass) when the node answers for another id', async () => {
    await expect(verifier(info({ id: 'cd'.repeat(32) })).verify(request)).rejects.toMatchObject({
      code: 'TRANSFER_VERIFY_MISMATCH',
      retryable: true,
    });
  });

  it('throws (→ 502) on an HTTP error', async () => {
    await expect(verifier({}, 503).verify(request)).rejects.toMatchObject({
      code: 'TRANSFER_VERIFY_HTTP_ERROR',
      retryable: true,
    });
  });

  it('refuses (→ 400) a recipient that is not a TRON address', async () => {
    await expect(
      verifier(info()).verify({ ...request, to: 'TXqH2JBkDgGWyCFg4GZzg8eUjG5JMZ7hPL' }),
    ).rejects.toMatchObject({
      code: 'INVALID_VERIFY_REQUEST',
      retryable: false,
    });
  });

  it('tronHex20 refuses a hex or an invalid base58 address', () => {
    expect(tronHex20(SELLER)).toBe(TronWeb.address.toHex(SELLER).slice(2).toLowerCase());
    expect(tronHex20('41' + '3'.repeat(40))).toBeNull();
    expect(tronHex20('TXqH2JBkDgGWyCFg4GZzg8eUjG5JMZ7hPL')).toBeNull();
  });
});
