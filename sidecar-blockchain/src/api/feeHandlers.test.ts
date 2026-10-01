import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, it, expect, vi } from 'vitest';
import type { Request, Response } from 'express';
import { estimateFeeHandler } from './feeHandlers.js';
import { SidecarError } from '../errors/SidecarError.js';
import type { FeeEstimationService, FeeEstimateResult } from '../fee/FeeEstimationService.js';
import { TransferService } from '../transfer/TransferService.js';

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

const SAMPLE_RESULT: FeeEstimateResult = {
  feeUsdt: '0.18',
  energyRequired: 29_650,
  energyPayableByCaller: 29_650,
  contractCallerPercent: 100,
  contractOwnerEnergyAvailable: 0,
  delegationPlan: null,
  delegationSun: null,
  energyAvailable: 100_000,
  energyShortfall: 0,
  bandwidthRequired: 350,
  bandwidthAvailable: 0,
  burnSun: 350_000,
  trxPriceUsdt: 0.5,
  priceSource: 'binance',
};

describe('estimateFeeHandler', () => {
  it('returns the estimate on a valid request', async () => {
    const estimate = vi.fn(async () => SAMPLE_RESULT);
    const handler = estimateFeeHandler({ estimate } as unknown as FeeEstimationService);
    const { res, captured } = buildResponse();

    await handler(
      buildRequest({ fromAddress: 'TDeposit', toAddress: 'TBuyer', amount: '8.20', token: 'USDT' }),
      res,
    );

    expect(captured.statusCode).toBe(200);
    expect(captured.body).toEqual(SAMPLE_RESULT);
    expect(estimate).toHaveBeenCalledWith({
      fromAddress: 'TDeposit',
      toAddress: 'TBuyer',
      amount: '8.20',
      token: 'USDT',
      correlationId: 'corr-1',
    });
  });

  it('rejects a request missing required fields', async () => {
    const estimate = vi.fn();
    const handler = estimateFeeHandler({ estimate } as unknown as FeeEstimationService);
    const { res, captured } = buildResponse();

    await handler(buildRequest({ toAddress: 'TBuyer', token: 'USDT' }), res);

    expect(captured.statusCode).toBe(400);
    expect((captured.body as { error: string }).error).toBe('INVALID_ESTIMATE_REQUEST');
    expect(estimate).not.toHaveBeenCalled();
  });

  it('rejects an unsupported token symbol', async () => {
    const handler = estimateFeeHandler({ estimate: vi.fn() } as unknown as FeeEstimationService);
    const { res, captured } = buildResponse();

    await handler(buildRequest({ toAddress: 'TBuyer', amount: '1.0', token: 'DOGE' }), res);

    expect(captured.statusCode).toBe(400);
  });

  it('maps a retryable SidecarError to 502', async () => {
    const estimate = vi.fn(async () => {
      throw new SidecarError('price down', 'TRX_PRICE_UNAVAILABLE', true);
    });
    const handler = estimateFeeHandler({ estimate } as unknown as FeeEstimationService);
    const { res, captured } = buildResponse();

    await handler(buildRequest({ toAddress: 'TBuyer', amount: '1.0', token: 'USDT' }), res);

    expect(captured.statusCode).toBe(502);
    expect((captured.body as { error: string }).error).toBe('TRX_PRICE_UNAVAILABLE');
  });

  it('maps a non-retryable SidecarError to 400', async () => {
    const estimate = vi.fn(async () => {
      throw new SidecarError('no contract', 'TOKEN_CONTRACT_NOT_CONFIGURED', false);
    });
    const handler = estimateFeeHandler({ estimate } as unknown as FeeEstimationService);
    const { res, captured } = buildResponse();

    await handler(buildRequest({ toAddress: 'TBuyer', amount: '1.0', token: 'USDT' }), res);

    expect(captured.statusCode).toBe(400);
  });

  it('maps an unexpected error to 500', async () => {
    const estimate = vi.fn(async () => {
      throw new Error('boom');
    });
    const handler = estimateFeeHandler({ estimate } as unknown as FeeEstimationService);
    const { res, captured } = buildResponse();

    await handler(buildRequest({ toAddress: 'TBuyer', amount: '1.0', token: 'USDT' }), res);

    expect(captured.statusCode).toBe(500);
  });
});

/**
 * The bodies the backend actually sends, as files both sides read
 * (PayoutGasEstimateAlwaysFallsBack). The backend's contract test
 * (`BlockchainSidecarEstimateFeeContractTests`) proves it emits exactly these;
 * this block proves that the handler accepts them, and that their amounts
 * pass the service's own amount rule (the handler checks only for a
 * non-empty string).
 * Until the 2026-09-23 rehearsal each side was tested only against its own
 * idea of the other, and the payout body — sent with `"fromAddress": null` —
 * was rejected on every call. Which account an omitted sender prices is the
 * service's job, pinned in FeeEstimationService.test.ts.
 */
const CONTRACT_DIR = resolve(__dirname, '../../contracts/estimate-fee');

function loadExample(name: string): Record<string, unknown> {
  return JSON.parse(readFileSync(resolve(CONTRACT_DIR, name), 'utf8')) as Record<string, unknown>;
}

describe('estimateFeeHandler — the backend request contract', () => {
  it('accepts the payout body and passes no sender on', async () => {
    const body = loadExample('payout.request.json');
    const estimate = vi.fn(async () => SAMPLE_RESULT);
    const handler = estimateFeeHandler({ estimate } as unknown as FeeEstimationService);
    const { res, captured } = buildResponse();

    await handler(buildRequest(body), res);

    expect(captured.statusCode).toBe(200);
    expect(estimate).toHaveBeenCalledWith({
      fromAddress: undefined,
      toAddress: body.toAddress,
      amount: body.amount,
      token: body.token,
      correlationId: 'corr-1',
    });
  });

  it('accepts the refund body and prices it from its deposit address', async () => {
    const body = loadExample('refund.request.json');
    const estimate = vi.fn(async () => SAMPLE_RESULT);
    const handler = estimateFeeHandler({ estimate } as unknown as FeeEstimationService);
    const { res, captured } = buildResponse();

    await handler(buildRequest(body), res);

    expect(captured.statusCode).toBe(200);
    expect(estimate).toHaveBeenCalledWith({
      fromAddress: body.fromAddress,
      toAddress: body.toAddress,
      amount: body.amount,
      token: body.token,
      correlationId: 'corr-1',
    });
  });

  it('rejects the payout body with an explicit null sender — why the backend omits the key', async () => {
    const estimate = vi.fn();
    const handler = estimateFeeHandler({ estimate } as unknown as FeeEstimationService);
    const { res, captured } = buildResponse();

    await handler(buildRequest({ ...loadExample('payout.request.json'), fromAddress: null }), res);

    expect(captured.statusCode).toBe(400);
    expect((captured.body as { error: string }).error).toBe('INVALID_ESTIMATE_REQUEST');
    expect(estimate).not.toHaveBeenCalled();
  });

  it.each(['payout.request.json', 'refund.request.json'])(
    '%s carries an amount the service converts to token units',
    (name) => {
      // The handler only checks that amount is a non-empty string; the real
      // syntax rule (a positive decimal, at most six fraction digits for USDT
      // and USDC) runs inside the service this block stubs. Without this, a
      // fixture — and the backend format its contract test pins to these
      // files — could drift to "-10" or "1,000" and stay green on both sides
      // while the real sidecar answers 400 INVALID_TRANSFER_AMOUNT (#327
      // validation).
      const { amount } = loadExample(name);

      expect(() => TransferService.toRawUnits(amount as string, 10n ** 6n)).not.toThrow();
    },
  );
});
