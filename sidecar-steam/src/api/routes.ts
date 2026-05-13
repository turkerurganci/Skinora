import { Router, type Request, type Response } from 'express';
import { botStatusFactory, healthCheckFactory } from '../health/HealthController.js';
import { metricsHandler } from '../metrics.js';
import { internalKeyAuth } from './middleware.js';
import type { BotManager } from '../bot/BotManager.js';
import type { TradeOfferService } from '../trade/TradeOfferService.js';
import type { SendTradeOfferRequest, TradeDirection } from '../trade/types.js';

const TRADE_DIRECTIONS: ReadonlySet<TradeDirection> = new Set([
  'SELLER_TO_BOT',
  'BOT_TO_BUYER',
  'BOT_TO_SELLER_REFUND',
]);

export interface RouterDeps {
  botManager?: BotManager;
  tradeOfferService?: TradeOfferService;
}

export function buildRouter(deps: BotManager | RouterDeps = {}): Router {
  const { botManager, tradeOfferService } = normalizeDeps(deps);
  const router = Router();

  // Health check — no auth required
  router.get('/health', healthCheckFactory(botManager));

  // Prometheus metrics — no auth required (T16)
  router.get('/metrics', metricsHandler);

  // Authenticated API routes
  const apiRouter = Router();
  apiRouter.use(internalKeyAuth);

  apiRouter.post('/trade-offers/send', tradeOfferSendHandler(tradeOfferService));

  apiRouter.get('/trade-offers/:offerId/status', (_req, res) => {
    res.status(501).json({ error: 'Not implemented — see T66' });
  });

  apiRouter.get('/inventory/:steamId', (_req, res) => {
    res.status(501).json({ error: 'Not implemented — see T67' });
  });

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

function parseSendRequest(
  body: unknown,
): { ok: true; value: SendTradeOfferRequest } | { ok: false; error: string } {
  if (body === null || typeof body !== 'object') {
    return { ok: false, error: 'Request body must be a JSON object' };
  }
  const obj = body as Record<string, unknown>;
  const { transactionId, direction, partnerSteamId, items, message } = obj;
  if (typeof transactionId !== 'string' || !transactionId)
    return { ok: false, error: 'transactionId is required' };
  if (typeof direction !== 'string' || !TRADE_DIRECTIONS.has(direction as TradeDirection))
    return {
      ok: false,
      error: 'direction must be one of SELLER_TO_BOT/BOT_TO_BUYER/BOT_TO_SELLER_REFUND',
    };
  if (typeof partnerSteamId !== 'string' || !partnerSteamId)
    return { ok: false, error: 'partnerSteamId is required' };
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
    },
  };
}
