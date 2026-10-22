import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, it, expect, vi } from 'vitest';
import type { Request, Response } from 'express';
import { refundHandler, sweepHandler } from './transferHandlers.js';
import type { TransferService } from '../transfer/TransferService.js';
import type { RefundService } from '../transfer/RefundService.js';

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
  return { body, correlationId: 'corr-1' } as unknown as Request;
}

/**
 * T160c kapanış (S4-1, ⚠ PARA) — the TRON deposit-send answer contract.
 *
 * `platformSpendSun` is the SUN a sweep / refund cost the hot wallet; the
 * backend books it as the platform's chain expense (AD38). The backend half
 * (`BlockchainSidecarTransferResponseContractTests`) reads these same files
 * through `HttpBlockchainTransferClient`; this block proves the handler emits
 * exactly them. If either side drops or renames the field the row silently
 * turns "not measured" — the pair of tests is what breaks instead.
 */
const CONTRACT_DIR = resolve(__dirname, '../../contracts');

function loadExample(folder: string): Record<string, unknown> {
  return JSON.parse(readFileSync(resolve(CONTRACT_DIR, folder, 'response.json'), 'utf8')) as Record<
    string,
    unknown
  >;
}

const DEPOSIT_BODY = {
  blockchainTransactionId: 'bt-1',
  depositIndex: 7,
  depositAddress: 'TDeposit',
  amount: '10.000000',
  token: 'USDT',
};

describe('TRON deposit-send answers — the backend response contract', () => {
  it.each(['transfer-sweep', 'transfer-refund'])(
    '%s example carries platformSpendSun as a non-negative JSON integer apart from the other SUN fields',
    (folder) => {
      const example = loadExample(folder);

      expect(Number.isSafeInteger(example.platformSpendSun)).toBe(true);
      expect(example.platformSpendSun as number).toBeGreaterThan(0);
      // A value equal to another field would let a handler that copies the
      // wrong one pass.
      expect(example.platformSpendSun).not.toBe(example.fallbackAmountSun);
      expect(example.platformSpendSun).not.toBe(example.delegationAmountSun);
    },
  );

  it('the sweep handler answers exactly contracts/transfer-sweep/response.json', async () => {
    const example = loadExample('transfer-sweep');
    const sweep = vi.fn(async () => ({ ...example }));
    const handler = sweepHandler({ sweep } as unknown as TransferService);
    const { res, captured } = buildResponse();

    await handler(buildRequest({ ...DEPOSIT_BODY, toHotWalletAddress: 'THot' }), res);

    expect(captured.statusCode).toBe(200);
    expect(captured.body).toEqual(example);
    expect(sweep).toHaveBeenCalledOnce();
  });

  it('the refund handler answers exactly contracts/transfer-refund/response.json', async () => {
    const example = loadExample('transfer-refund');
    const refund = vi.fn(async () => ({ ...example }));
    const handler = refundHandler({ refund } as unknown as RefundService);
    const { res, captured } = buildResponse();

    await handler(buildRequest({ ...DEPOSIT_BODY, toBuyerAddress: 'TBuyer' }), res);

    expect(captured.statusCode).toBe(200);
    expect(captured.body).toEqual(example);
    expect(refund).toHaveBeenCalledOnce();
  });
});
