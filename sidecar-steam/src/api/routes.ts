import { Router, type Request, type Response } from 'express';
import { healthCheckFactory } from '../health/HealthController.js';
import { metricsHandler } from '../metrics.js';
import { internalKeyAuth } from './middleware.js';
import type { InventoryService } from '../trade/InventoryService.js';
import { SteamApiKeyMissingError, type TradeHoldService } from '../trade/TradeHoldService.js';
import { SteamApiError } from '../errors/SidecarError.js';

/** SteamID64 is a 17-digit decimal — looser regex catches obvious garbage early. */
const STEAM_ID64_REGEX = /^7656119[0-9]{10}$/;

/**
 * Everything this sidecar serves (T133). The surface is deliberately
 * read-only: both routes ask Steam a question and neither changes anything on
 * Steam's side. The bot pool, trade offer send/monitor and bot-status routes
 * went with the custody layer (02 §2.1) — the platform holds no items and
 * sends no offers, so there is nothing left here to write.
 */
export interface RouterDeps {
  inventoryService?: InventoryService;
  tradeHoldService?: TradeHoldService;
}

export function buildRouter(deps: RouterDeps = {}): Router {
  const { inventoryService, tradeHoldService } = deps;
  const router = Router();

  // Health check — no auth required
  router.get('/health', healthCheckFactory());

  // Prometheus metrics — no auth required (T16)
  router.get('/metrics', metricsHandler);

  // Authenticated API routes
  const apiRouter = Router();
  apiRouter.use(internalKeyAuth);

  apiRouter.get('/inventory/:steamId', inventoryGetHandler(inventoryService));
  apiRouter.delete('/inventory/:steamId/cache', inventoryInvalidateHandler(inventoryService));

  // Trade-hold / Mobile Authenticator check (08 §2.2, 07 §5.16a + §4.8).
  // Web-API-key call (no bot session) → IEconService/GetTradeHoldDurations/v1.
  apiRouter.get('/trade-hold/:steamId', tradeHoldGetHandler(tradeHoldService));

  router.use('/api', apiRouter);
  return router;
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
    const refresh = parseRefreshParam(req.query.refresh);
    if (!refresh.ok) {
      res.status(400).json({ error: refresh.error });
      return;
    }
    try {
      // 08 §2.3 — status codes are the contract the backend consumes today
      // (07 §6.1 maps them to 200 / 422 INVENTORY_PRIVATE / 503
      // STEAM_UNAVAILABLE). `visibility` is carried in the body ALONGSIDE
      // them, never instead of them: collapsing every outcome onto 200 would
      // make a private profile look like an empty inventory to any consumer
      // that has not yet been taught to read the new field.
      const result = await service.getInventory(steamId, { refresh: refresh.value });
      switch (result.visibility) {
        case 'PUBLIC':
          res.status(200).json({ visibility: result.visibility, ...result.inventory });
          return;
        case 'PRIVATE':
          res.status(422).json({
            visibility: result.visibility,
            code: result.error.code,
            error: result.error.message,
          });
          return;
        case 'UNAVAILABLE':
          req.log.warn({ steamId, err: result.error.message }, 'Steam inventory upstream failure');
          res.status(503).json({
            visibility: result.visibility,
            code: result.error.code,
            error: result.error.message,
          });
          return;
      }
    } catch (err) {
      req.log.error({ err, steamId }, 'InventoryService.getInventory threw');
      res.status(500).json({ error: (err as Error).message });
    }
  };
}

/** Accepted spellings of the 08 §2.3 `refresh` cache-bypass flag. */
const REFRESH_TRUE: ReadonlySet<string> = new Set(['true', '1']);
const REFRESH_FALSE: ReadonlySet<string> = new Set(['false', '0']);

/**
 * Parse `?refresh=`. Absent means "use the cache" (the ordinary listing read).
 *
 * An unrecognized value is rejected with 400 rather than silently treated as
 * false: the caller that sets this flag is a delivery-verification read, and
 * quietly serving it two-minute-old data is precisely the failure 08 §2.3
 * introduced the flag to prevent. Failing loud keeps the mistake visible.
 */
function parseRefreshParam(
  raw: unknown,
): { ok: true; value: boolean } | { ok: false; error: string } {
  if (raw === undefined) return { ok: true, value: false };
  if (typeof raw !== 'string') {
    return { ok: false, error: 'refresh must be a single value: true, false, 1 or 0' };
  }
  const normalized = raw.toLowerCase();
  if (REFRESH_TRUE.has(normalized)) return { ok: true, value: true };
  if (REFRESH_FALSE.has(normalized)) return { ok: true, value: false };
  return { ok: false, error: 'refresh must be one of: true, false, 1, 0' };
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
