import { describe, it, expect, vi, beforeEach } from 'vitest';
import express from 'express';
import type { AddressInfo } from 'net';

vi.mock('../logger.js', () => ({
  logger: {
    info: vi.fn(),
    warn: vi.fn(),
    error: vi.fn(),
    debug: vi.fn(),
    child: vi.fn().mockReturnThis(),
  },
  loggerForRequest: vi.fn(() => ({
    logger: {
      info: vi.fn(),
      warn: vi.fn(),
      error: vi.fn(),
      debug: vi.fn(),
      child: vi.fn().mockReturnThis(),
    },
    correlationId: 'test-correlation-id',
  })),
}));

import { buildRouter } from './routes.js';
import { correlationMiddleware } from './middleware.js';
import type { TradeOfferService } from '../trade/TradeOfferService.js';
import type { SendTradeOfferResponse } from '../trade/types.js';

function buildApp(service: TradeOfferService) {
  const app = express();
  app.use(express.json());
  app.use(correlationMiddleware);
  app.use(buildRouter({ tradeOfferService: service }));
  return app;
}

async function startApp(
  service: TradeOfferService,
): Promise<{ url: string; close: () => Promise<void> }> {
  const app = buildApp(service);
  const server = await new Promise<import('http').Server>((resolve) => {
    const s = app.listen(0, () => resolve(s));
  });
  const port = (server.address() as AddressInfo).port;
  return {
    url: `http://127.0.0.1:${port}`,
    close: () => new Promise((resolve) => server.close(() => resolve())),
  };
}

const validBody = {
  transactionId: 'tx-1',
  direction: 'BOT_TO_BUYER',
  partnerSteamId: '76561198000000999',
  items: [{ assetid: 'a1', appid: 730, contextid: '2' }],
};

beforeEach(() => {
  vi.clearAllMocks();
});

describe('POST /api/trade-offers/send', () => {
  it('routes a valid request to TradeOfferService.sendOffer', async () => {
    const sendOffer = vi.fn().mockResolvedValue({
      status: 'sent',
      offerId: 'offer-1',
      attempts: 1,
    } satisfies SendTradeOfferResponse);
    const service = { sendOffer } as unknown as TradeOfferService;

    const ctx = await startApp(service);
    try {
      const res = await fetch(`${ctx.url}/api/trade-offers/send`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(validBody),
      });
      expect(res.status).toBe(200);
      const body = await res.json();
      expect(body).toMatchObject({ status: 'sent', offerId: 'offer-1' });
      expect(sendOffer).toHaveBeenCalledWith(expect.objectContaining({ transactionId: 'tx-1' }));
    } finally {
      await ctx.close();
    }
  });

  it('returns 502 when TradeOfferService reports failure', async () => {
    const sendOffer = vi.fn().mockResolvedValue({
      status: 'failed',
      reason: 'no bots',
      retryable: true,
      attempts: 0,
    } satisfies SendTradeOfferResponse);
    const service = { sendOffer } as unknown as TradeOfferService;

    const ctx = await startApp(service);
    try {
      const res = await fetch(`${ctx.url}/api/trade-offers/send`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(validBody),
      });
      expect(res.status).toBe(502);
      const body = await res.json();
      expect(body).toMatchObject({ status: 'failed', reason: 'no bots' });
    } finally {
      await ctx.close();
    }
  });

  it('rejects missing transactionId with 400', async () => {
    const sendOffer = vi.fn();
    const service = { sendOffer } as unknown as TradeOfferService;

    const ctx = await startApp(service);
    try {
      const res = await fetch(`${ctx.url}/api/trade-offers/send`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ ...validBody, transactionId: undefined }),
      });
      expect(res.status).toBe(400);
      const body = (await res.json()) as { error: string };
      expect(body.error).toMatch(/transactionId/);
      expect(sendOffer).not.toHaveBeenCalled();
    } finally {
      await ctx.close();
    }
  });

  it('rejects invalid direction with 400', async () => {
    const sendOffer = vi.fn();
    const service = { sendOffer } as unknown as TradeOfferService;

    const ctx = await startApp(service);
    try {
      const res = await fetch(`${ctx.url}/api/trade-offers/send`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ ...validBody, direction: 'BUYER_TO_BOT' }),
      });
      expect(res.status).toBe(400);
      const body = (await res.json()) as { error: string };
      expect(body.error).toMatch(/direction/);
    } finally {
      await ctx.close();
    }
  });

  it('rejects empty items with 400', async () => {
    const sendOffer = vi.fn();
    const service = { sendOffer } as unknown as TradeOfferService;

    const ctx = await startApp(service);
    try {
      const res = await fetch(`${ctx.url}/api/trade-offers/send`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ ...validBody, items: [] }),
      });
      expect(res.status).toBe(400);
      const body = (await res.json()) as { error: string };
      expect(body.error).toMatch(/items/);
    } finally {
      await ctx.close();
    }
  });

  it('returns 503 when TradeOfferService is not initialized', async () => {
    const app = express();
    app.use(express.json());
    app.use(correlationMiddleware);
    app.use(buildRouter({}));
    const server = await new Promise<import('http').Server>((resolve) => {
      const s = app.listen(0, () => resolve(s));
    });
    try {
      const port = (server.address() as AddressInfo).port;
      const res = await fetch(`http://127.0.0.1:${port}/api/trade-offers/send`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(validBody),
      });
      expect(res.status).toBe(503);
    } finally {
      await new Promise<void>((resolve) => server.close(() => resolve()));
    }
  });
});
