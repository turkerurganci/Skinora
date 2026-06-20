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
import {
  InventoryPrivateError,
  SteamUnavailableError,
  type InventoryService,
} from '../trade/InventoryService.js';
import { SteamApiKeyMissingError, type TradeHoldService } from '../trade/TradeHoldService.js';
import { SteamApiError } from '../errors/SidecarError.js';

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

// ---------------------------------------------------------------------------
// T67 — GET /api/inventory/:steamId + DELETE /api/inventory/:steamId/cache
// ---------------------------------------------------------------------------

const VALID_STEAM_ID = '76561198000000123';

async function startInventoryApp(
  service: InventoryService,
): Promise<{ url: string; close: () => Promise<void> }> {
  const app = express();
  app.use(express.json());
  app.use(correlationMiddleware);
  app.use(buildRouter({ inventoryService: service }));
  const server = await new Promise<import('http').Server>((resolve) => {
    const s = app.listen(0, () => resolve(s));
  });
  const port = (server.address() as AddressInfo).port;
  return {
    url: `http://127.0.0.1:${port}`,
    close: () => new Promise((resolve) => server.close(() => resolve())),
  };
}

describe('GET /api/inventory/:steamId (T67)', () => {
  it('returns the inventory envelope on success', async () => {
    const inventory = {
      items: [
        {
          assetId: '27348562891',
          classId: '310776959',
          instanceId: '188530139',
          name: 'AK-47 | Redline',
          marketHashName: 'AK-47 | Redline (Field-Tested)',
          type: 'Rifle',
          exterior: 'Field-Tested',
          iconUrl: 'https://cdn.test/ak.png',
          tradable: true,
          marketable: true,
        },
      ],
      totalCount: 1,
      tradeableCount: 1,
    };
    const getInventory = vi.fn().mockResolvedValue(inventory);
    const service = { getInventory, invalidate: vi.fn() } as unknown as InventoryService;

    const ctx = await startInventoryApp(service);
    try {
      const res = await fetch(`${ctx.url}/api/inventory/${VALID_STEAM_ID}`);
      expect(res.status).toBe(200);
      expect(await res.json()).toEqual(inventory);
      expect(getInventory).toHaveBeenCalledWith(VALID_STEAM_ID);
    } finally {
      await ctx.close();
    }
  });

  it('returns 422 INVENTORY_PRIVATE when the profile is private', async () => {
    const getInventory = vi.fn().mockRejectedValue(new InventoryPrivateError(VALID_STEAM_ID));
    const service = { getInventory, invalidate: vi.fn() } as unknown as InventoryService;

    const ctx = await startInventoryApp(service);
    try {
      const res = await fetch(`${ctx.url}/api/inventory/${VALID_STEAM_ID}`);
      expect(res.status).toBe(422);
      const body = (await res.json()) as { code: string };
      expect(body.code).toBe('INVENTORY_PRIVATE');
    } finally {
      await ctx.close();
    }
  });

  it('returns 503 STEAM_UNAVAILABLE on upstream failure', async () => {
    const getInventory = vi.fn().mockRejectedValue(new SteamUnavailableError('HTTP 503'));
    const service = { getInventory, invalidate: vi.fn() } as unknown as InventoryService;

    const ctx = await startInventoryApp(service);
    try {
      const res = await fetch(`${ctx.url}/api/inventory/${VALID_STEAM_ID}`);
      expect(res.status).toBe(503);
      const body = (await res.json()) as { code: string };
      expect(body.code).toBe('STEAM_UNAVAILABLE');
    } finally {
      await ctx.close();
    }
  });

  it('rejects an invalid SteamID64 with 400 without calling the service', async () => {
    const getInventory = vi.fn();
    const service = { getInventory, invalidate: vi.fn() } as unknown as InventoryService;

    const ctx = await startInventoryApp(service);
    try {
      const res = await fetch(`${ctx.url}/api/inventory/not-a-steam-id`);
      expect(res.status).toBe(400);
      expect(getInventory).not.toHaveBeenCalled();
    } finally {
      await ctx.close();
    }
  });

  it('returns 503 when InventoryService is not initialized', async () => {
    const app = express();
    app.use(express.json());
    app.use(correlationMiddleware);
    app.use(buildRouter({}));
    const server = await new Promise<import('http').Server>((resolve) => {
      const s = app.listen(0, () => resolve(s));
    });
    try {
      const port = (server.address() as AddressInfo).port;
      const res = await fetch(`http://127.0.0.1:${port}/api/inventory/${VALID_STEAM_ID}`);
      expect(res.status).toBe(503);
    } finally {
      await new Promise<void>((resolve) => server.close(() => resolve()));
    }
  });
});

describe('DELETE /api/inventory/:steamId/cache (T67)', () => {
  it('invokes the service and returns 204', async () => {
    const invalidate = vi.fn().mockResolvedValue(undefined);
    const service = { invalidate, getInventory: vi.fn() } as unknown as InventoryService;

    const ctx = await startInventoryApp(service);
    try {
      const res = await fetch(`${ctx.url}/api/inventory/${VALID_STEAM_ID}/cache`, {
        method: 'DELETE',
      });
      expect(res.status).toBe(204);
      expect(invalidate).toHaveBeenCalledWith(VALID_STEAM_ID);
    } finally {
      await ctx.close();
    }
  });

  it('rejects an invalid SteamID64 with 400', async () => {
    const invalidate = vi.fn();
    const service = { invalidate, getInventory: vi.fn() } as unknown as InventoryService;

    const ctx = await startInventoryApp(service);
    try {
      const res = await fetch(`${ctx.url}/api/inventory/abc/cache`, { method: 'DELETE' });
      expect(res.status).toBe(400);
      expect(invalidate).not.toHaveBeenCalled();
    } finally {
      await ctx.close();
    }
  });
});

// ---------------------------------------------------------------------------
// WP6 — GET /api/trade-hold/:steamId (08 §2.2 trade-hold / MA check)
// ---------------------------------------------------------------------------

async function startTradeHoldApp(
  service: TradeHoldService,
): Promise<{ url: string; close: () => Promise<void> }> {
  const app = express();
  app.use(express.json());
  app.use(correlationMiddleware);
  app.use(buildRouter({ tradeHoldService: service }));
  const server = await new Promise<import('http').Server>((resolve) => {
    const s = app.listen(0, () => resolve(s));
  });
  const port = (server.address() as AddressInfo).port;
  return {
    url: `http://127.0.0.1:${port}`,
    close: () => new Promise((resolve) => server.close(() => resolve())),
  };
}

describe('GET /api/trade-hold/:steamId (WP6)', () => {
  it('returns the trade-hold result on success', async () => {
    const getTradeHold = vi.fn().mockResolvedValue({ active: true, escrowEndDurationSeconds: 0 });
    const service = { getTradeHold } as unknown as TradeHoldService;

    const ctx = await startTradeHoldApp(service);
    try {
      const res = await fetch(`${ctx.url}/api/trade-hold/${VALID_STEAM_ID}?accessToken=tok123`);
      expect(res.status).toBe(200);
      expect(await res.json()).toEqual({ active: true, escrowEndDurationSeconds: 0 });
      expect(getTradeHold).toHaveBeenCalledWith(VALID_STEAM_ID, 'tok123');
    } finally {
      await ctx.close();
    }
  });

  it('rejects an invalid SteamID64 with 400 without calling the service', async () => {
    const getTradeHold = vi.fn();
    const service = { getTradeHold } as unknown as TradeHoldService;

    const ctx = await startTradeHoldApp(service);
    try {
      const res = await fetch(`${ctx.url}/api/trade-hold/not-a-steam-id?accessToken=tok123`);
      expect(res.status).toBe(400);
      expect(getTradeHold).not.toHaveBeenCalled();
    } finally {
      await ctx.close();
    }
  });

  it('rejects a missing accessToken with 400 without calling the service', async () => {
    const getTradeHold = vi.fn();
    const service = { getTradeHold } as unknown as TradeHoldService;

    const ctx = await startTradeHoldApp(service);
    try {
      const res = await fetch(`${ctx.url}/api/trade-hold/${VALID_STEAM_ID}`);
      expect(res.status).toBe(400);
      const body = (await res.json()) as { error: string };
      expect(body.error).toMatch(/accessToken/);
      expect(getTradeHold).not.toHaveBeenCalled();
    } finally {
      await ctx.close();
    }
  });

  it('returns 503 STEAM_API_KEY_MISSING when the key is not configured', async () => {
    const getTradeHold = vi.fn().mockRejectedValue(new SteamApiKeyMissingError());
    const service = { getTradeHold } as unknown as TradeHoldService;

    const ctx = await startTradeHoldApp(service);
    try {
      const res = await fetch(`${ctx.url}/api/trade-hold/${VALID_STEAM_ID}?accessToken=tok123`);
      expect(res.status).toBe(503);
      const body = (await res.json()) as { code: string };
      expect(body.code).toBe('STEAM_API_KEY_MISSING');
    } finally {
      await ctx.close();
    }
  });

  it('returns 503 on a Steam upstream failure', async () => {
    const getTradeHold = vi.fn().mockRejectedValue(new SteamApiError('upstream', 429));
    const service = { getTradeHold } as unknown as TradeHoldService;

    const ctx = await startTradeHoldApp(service);
    try {
      const res = await fetch(`${ctx.url}/api/trade-hold/${VALID_STEAM_ID}?accessToken=tok123`);
      expect(res.status).toBe(503);
      const body = (await res.json()) as { code: string };
      expect(body.code).toBe('STEAM_API_ERROR');
    } finally {
      await ctx.close();
    }
  });

  it('returns 503 when TradeHoldService is not initialized', async () => {
    const app = express();
    app.use(express.json());
    app.use(correlationMiddleware);
    app.use(buildRouter({}));
    const server = await new Promise<import('http').Server>((resolve) => {
      const s = app.listen(0, () => resolve(s));
    });
    try {
      const port = (server.address() as AddressInfo).port;
      const res = await fetch(
        `http://127.0.0.1:${port}/api/trade-hold/${VALID_STEAM_ID}?accessToken=tok123`,
      );
      expect(res.status).toBe(503);
    } finally {
      await new Promise<void>((resolve) => server.close(() => resolve()));
    }
  });
});
