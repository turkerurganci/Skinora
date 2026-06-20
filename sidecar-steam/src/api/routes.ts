import { Router, type Request, type Response } from 'express';
import { botStatusFactory, healthCheckFactory } from '../health/HealthController.js';
import { metricsHandler } from '../metrics.js';
import { internalKeyAuth } from './middleware.js';
import type { BotManager } from '../bot/BotManager.js';
import type { TradeOfferService } from '../trade/TradeOfferService.js';
import {
  InventoryPrivateError,
  SteamUnavailableError,
  type InventoryService,
} from '../trade/InventoryService.js';
import { SteamApiKeyMissingError, type TradeHoldService } from '../trade/TradeHoldService.js';
import { SteamApiError } from '../errors/SidecarError.js';
import type { SendTradeOfferRequest, TradeDirection } from '../trade/types.js';

const TRADE_DIRECTIONS: ReadonlySet<TradeDirection> = new Set([
  'SELLER_TO_BOT',
  'BOT_TO_BUYER',
  'BOT_TO_SELLER_REFUND',
]);

/** SteamID64 is a 17-digit decimal — looser regex catches obvious garbage early. */
const STEAM_ID64_REGEX = /^7656119[0-9]{10}$/;

export interface RouterDeps {
  botManager?: BotManager;
  tradeOfferService?: TradeOfferService;
  inventoryService?: InventoryService;
  tradeHoldService?: TradeHoldService;
}

export function buildRouter(deps: BotManager | RouterDeps = {}): Router {
  const { botManager, tradeOfferService, inventoryService, tradeHoldService } = normalizeDeps(deps);
  const router = Router();

  // Health check — no auth required
  router.get('/health', healthCheckFactory(botManager));

  // Prometheus metrics — no auth required (T16)
  router.get('/metrics', metricsHandler);

  // Authenticated API routes
  const apiRouter = Router();
  apiRouter.use(internalKeyAuth);

  apiRouter.post('/trade-offers/send', tradeOfferSendHandler(tradeOfferService));

  // T66 delivers offer status changes via push webhooks (sentOfferChanged →
  // trade_offer.{accepted,declined,expired,countered,invalid_items}). An ad-hoc
  // pull endpoint is not part of the spec; reserved for future ops tooling.
  apiRouter.get('/trade-offers/:offerId/status', (_req, res) => {
    res
      .status(501)
      .json({
        error: 'Pull status not implemented — status changes are pushed via webhook (08 §2.4)',
      });
  });

  apiRouter.get('/inventory/:steamId', inventoryGetHandler(inventoryService));
  apiRouter.delete('/inventory/:steamId/cache', inventoryInvalidateHandler(inventoryService));

  // Trade-hold / Mobile Authenticator check (08 §2.2, 07 §5.16a + §4.8).
  // Web-API-key call (no bot session) → IEconService/GetTradeHoldDurations/v1.
  apiRouter.get('/trade-hold/:steamId', tradeHoldGetHandler(tradeHoldService));

  // Bot pool status (T64)
  apiRouter.get('/bots/status', botStatusFactory(botManager));

  router.use('/api', apiRouter);
  return router;
}

function normalizeDeps(input: BotManager | RouterDeps): RouterDeps {
  // Backward-compat shim — T64's `buildRouter(botManager)` callers stay valid.
  if (input && typeof (input as BotManager).selectBot === 'function') {
    return { botManager: input as BotManager };
  }
  return input as RouterDeps;
}

function tradeOfferSendHandler(service?: TradeOfferService) {
  return async (req: Request, res: Response): Promise<void> => {
    if (!service) {
      res.status(503).json({ error: 'TradeOfferService not initialized' });
      return;
    }
    const parsed = parseSendRequest(req.body);
    if (!parsed.ok) {
      res.status(400).json({ error: parsed.error });
      return;
    }
    try {
      const result = await service.sendOffer(parsed.value);
      const httpStatus = result.status === 'failed' ? 502 : 200;
      res.status(httpStatus).json(result);
    } catch (err) {
      req.log.error({ err }, 'TradeOfferService.sendOffer threw');
      res.status(500).json({ error: (err as Error).message });
    }
  };
}

function inventoryGetHandler(service?: InventoryService) {
  return async (req: Request, res: Response): Promise<void> => {
    if (!service) {
      res.status(503).json({ error: 'InventoryService not initialized' });
      return;
    }
    const steamId = resolveSteamIdParam(req.params.steamId);
    if (!steamId) {
      res.status(400).json({ error: 'steamId must be a valid SteamID64' });
      return;
    }
    try {
      const inventory = await service.getInventory(steamId);
      res.status(200).json(inventory);
    } catch (err) {
      if (err instanceof InventoryPrivateError) {
        res.status(422).json({ code: err.code, error: err.message });
        return;
      }
      if (err instanceof SteamUnavailableError) {
        req.log.warn({ steamId, err: err.message }, 'Steam inventory upstream failure');
        res.status(503).json({ code: err.code, error: err.message });
        return;
      }
      req.log.error({ err, steamId }, 'InventoryService.getInventory threw');
      res.status(500).json({ error: (err as Error).message });
    }
  };
}

function inventoryInvalidateHandler(service?: InventoryService) {
  return async (req: Request, res: Response): Promise<void> => {
    if (!service) {
      res.status(503).json({ error: 'InventoryService not initialized' });
      return;
    }
    const steamId = resolveSteamIdParam(req.params.steamId);
    if (!steamId) {
      res.status(400).json({ error: 'steamId must be a valid SteamID64' });
      return;
    }
    await service.invalidate(steamId);
    res.status(204).send();
  };
}

function tradeHoldGetHandler(service?: TradeHoldService) {
  return async (req: Request, res: Response): Promise<void> => {
    if (!service) {
      res.status(503).json({ error: 'TradeHoldService not initialized' });
      return;
    }
    const steamId = resolveSteamIdParam(req.params.steamId);
    if (!steamId) {
      res.status(400).json({ error: 'steamId must be a valid SteamID64' });
      return;
    }
    // 08 §2.2 — non-friend targets require the trade_offer_access_token parsed
    // from the user's trade URL; the platform always supplies it.
    const accessToken = typeof req.query.accessToken === 'string' ? req.query.accessToken : '';
    if (!accessToken) {
      res.status(400).json({ error: 'accessToken query parameter is required' });
      return;
    }
    try {
      const result = await service.getTradeHold(steamId, accessToken);
      res.status(200).json(result);
    } catch (err) {
      if (err instanceof SteamApiKeyMissingError) {
        req.log.error('Trade-hold check requested but STEAM_API_KEY is not configured');
        res.status(503).json({ code: err.code, error: err.message });
        return;
      }
      if (err instanceof SteamApiError) {
        req.log.warn({ steamId, err: err.message }, 'Trade-hold upstream failure');
        res.status(503).json({ code: err.code, error: err.message });
        return;
      }
      req.log.error({ err, steamId }, 'TradeHoldService.getTradeHold threw');
      res.status(500).json({ error: (err as Error).message });
    }
  };
}

/**
 * Express `req.params` values are typed as `string | string[]` to allow array
 * notation (`?a[]=1&a[]=2`); for a path segment Express always emits a string.
 * Narrow here so the regex/test sites stay readable.
 */
function resolveSteamIdParam(raw: string | string[] | undefined): string | null {
  if (typeof raw !== 'string') return null;
  return STEAM_ID64_REGEX.test(raw) ? raw : null;
}

function parseSendRequest(
  body: unknown,
): { ok: true; value: SendTradeOfferRequest } | { ok: false; error: string } {
  if (body === null || typeof body !== 'object') {
    return { ok: false, error: 'Request body must be a JSON object' };
  }
  const obj = body as Record<string, unknown>;
  const { transactionId, direction, partnerSteamId, items, message, botAccountName } = obj;
  if (typeof transactionId !== 'string' || !transactionId)
    return { ok: false, error: 'transactionId is required' };
  if (typeof direction !== 'string' || !TRADE_DIRECTIONS.has(direction as TradeDirection))
    return {
      ok: false,
      error: 'direction must be one of SELLER_TO_BOT/BOT_TO_BUYER/BOT_TO_SELLER_REFUND',
    };
  if (typeof partnerSteamId !== 'string' || !partnerSteamId)
    return { ok: false, error: 'partnerSteamId is required' };
  if (botAccountName !== undefined && typeof botAccountName !== 'string')
    return { ok: false, error: 'botAccountName must be a string when present' };
  if (!Array.isArray(items) || items.length === 0)
    return { ok: false, error: 'items must be a non-empty array' };
  for (const [i, item] of items.entries()) {
    if (item === null || typeof item !== 'object')
      return { ok: false, error: `items[${i}] must be an object` };
    const it = item as Record<string, unknown>;
    if (typeof it.assetid !== 'string' || !it.assetid)
      return { ok: false, error: `items[${i}].assetid is required` };
    if (typeof it.appid !== 'number')
      return { ok: false, error: `items[${i}].appid must be a number` };
    if (typeof it.contextid !== 'string' || !it.contextid)
      return { ok: false, error: `items[${i}].contextid is required` };
  }
  if (message !== undefined && typeof message !== 'string')
    return { ok: false, error: 'message must be a string when present' };

  return {
    ok: true,
    value: {
      transactionId,
      direction: direction as TradeDirection,
      partnerSteamId,
      items: items as SendTradeOfferRequest['items'],
      message: message as string | undefined,
      botAccountName: botAccountName as string | undefined,
    },
  };
}
