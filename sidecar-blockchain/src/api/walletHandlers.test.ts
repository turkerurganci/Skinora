import { describe, it, expect, vi } from 'vitest';
import type { Request, Response } from 'express';
import { walletBalancesHandler } from './walletHandlers.js';
import type { TronGridClient } from '../tron/TronGridClient.js';
const TEST_USDT_CONTRACT = 'TUSDTContractFixture';
const TEST_USDC_CONTRACT = 'TUSDCContractFixture';
const TEST_CONTRACT_MAP: Record<string, string> = {
  [TEST_USDT_CONTRACT]: 'USDT',
  [TEST_USDC_CONTRACT]: 'USDC',
};

interface MockResponse {
  statusCode: number;
  body: unknown;
}

function buildResponse(): { res: Response; captured: MockResponse } {
  const captured: MockResponse = { statusCode: 0, body: undefined };
  const res = {
    status(code: number) {
      captured.statusCode = code;
      return this;
    },
    json(body: unknown) {
      captured.body = body;
      return this;
    },
  } as unknown as Response;
  return { res, captured };
}

function buildRequest(body: unknown): Request {
  return { body } as unknown as Request;
}

function buildTronClientMock(
  overrides: Partial<{
    getNowSolidBlock: () => Promise<number>;
    getAccountBalances: (address: string) => Promise<{
      address: string;
      trx: string;
      trc20: Record<string, string>;
    }>;
  }> = {},
): TronGridClient {
  return {
    getNowSolidBlock: overrides.getNowSolidBlock ?? vi.fn(async () => 100),
    getAccountBalances:
      overrides.getAccountBalances ??
      vi.fn(async (address: string) => ({
        address,
        trx: '1000000',
        trc20: {
          [TEST_USDT_CONTRACT]: '5000000',
          [TEST_USDC_CONTRACT]: '0',
        },
      })),
  } as unknown as TronGridClient;
}

describe('walletBalancesHandler — T76 reconciliation snapshot', () => {
  it('returns 400 INVALID_BALANCES_REQUEST when addresses missing', async () => {
    const { res, captured } = buildResponse();
    await walletBalancesHandler(buildTronClientMock(), TEST_CONTRACT_MAP)(
      buildRequest({}),
      res,
    );
    expect(captured.statusCode).toBe(400);
    expect((captured.body as { error: string }).error).toBe('INVALID_BALANCES_REQUEST');
  });

  it('returns 400 when addresses array is empty', async () => {
    const { res, captured } = buildResponse();
    await walletBalancesHandler(buildTronClientMock(), TEST_CONTRACT_MAP)(
      buildRequest({ addresses: [] }),
      res,
    );
    expect(captured.statusCode).toBe(400);
  });

  it('returns 400 when addresses array exceeds the 100 cap', async () => {
    const { res, captured } = buildResponse();
    const tooMany = Array.from({ length: 101 }, (_, i) => `T${i}`);
    await walletBalancesHandler(buildTronClientMock(), TEST_CONTRACT_MAP)(
      buildRequest({ addresses: tooMany }),
      res,
    );
    expect(captured.statusCode).toBe(400);
  });

  it('returns 400 when an entry is not a non-empty string', async () => {
    const { res, captured } = buildResponse();
    await walletBalancesHandler(buildTronClientMock(), TEST_CONTRACT_MAP)(
      buildRequest({ addresses: ['TValid', 42] }),
      res,
    );
    expect(captured.statusCode).toBe(400);
  });

  it('snapshots block height once for the batch and per-address tokens', async () => {
    const getNowSolidBlock = vi.fn(async () => 80_000_500);
    const getAccountBalances = vi.fn(
      async (
        address: string,
      ): Promise<{ address: string; trx: string; trc20: Record<string, string> }> => ({
        address,
        trx: address === 'TDeposit1' ? '500' : '0',
        trc20: address === 'TDeposit1' ? { [TEST_USDT_CONTRACT]: '100000000' } : {},
      }),
    );
    const client = buildTronClientMock({ getNowSolidBlock, getAccountBalances });

    const { res, captured } = buildResponse();
    await walletBalancesHandler(client, TEST_CONTRACT_MAP)(
      buildRequest({ addresses: ['TDeposit1', 'TDeposit2'] }),
      res,
    );

    expect(captured.statusCode).toBe(200);
    expect(getNowSolidBlock).toHaveBeenCalledTimes(1);
    expect(getAccountBalances).toHaveBeenCalledTimes(2);

    const body = captured.body as {
      blockNumber: number;
      balances: Array<{ address: string; tokens: Record<string, string> }>;
    };
    expect(body.blockNumber).toBe(80_000_500);
    expect(body.balances).toHaveLength(2);
    expect(body.balances[0]).toEqual({
      address: 'TDeposit1',
      tokens: { TRX: '500', USDT: '100000000', USDC: '0' },
    });
    expect(body.balances[1]).toEqual({
      address: 'TDeposit2',
      tokens: { TRX: '0', USDT: '0', USDC: '0' },
    });
  });

  it('returns 502 BALANCE_SNAPSHOT_FAILED on upstream failure', async () => {
    const client = buildTronClientMock({
      getNowSolidBlock: vi.fn(async () => {
        throw new Error('TronGrid timeout');
      }),
    });
    const { res, captured } = buildResponse();
    await walletBalancesHandler(client, TEST_CONTRACT_MAP)(
      buildRequest({ addresses: ['TX'] }),
      res,
    );
    expect(captured.statusCode).toBe(502);
    expect((captured.body as { error: string }).error).toBe('BALANCE_SNAPSHOT_FAILED');
  });
});
