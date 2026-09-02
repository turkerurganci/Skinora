import { describe, it, expect, vi } from 'vitest';
import type { Request, Response } from 'express';
import { estimateFeeHandler } from './feeHandlers.js';
import { SidecarError } from '../errors/SidecarError.js';
import type { FeeEstimationService, FeeEstimateResult } from '../fee/FeeEstimationService.js';

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
