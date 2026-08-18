import { Router } from 'express';
import { getTradeHold, inventoryResponse } from '../inventoryStore.js';

/**
 * Steam sidecar surface (:5100) — the outbound calls the backend makes.
 *
 * Everything answered here reads from `inventoryStore`, which the `/__e2e/steam/*`
 * control surface drives (T137). Before that the inventory was one constant
 * list served to every steamId, which made the seller and the buyer
 * indistinguishable and left P2P delivery unsimulatable.
 *
 * `POST /api/trade-offers/send` was RETIRED here (T137): the platform no longer
 * sends trade offers (02 §2.1), the backend has neither a client that calls it
 * nor a `/api/v1/webhooks/steam/trade-events` endpoint to receive its self-drive
 * (both went with T117/T118), so the route could only ever have posted webhooks
 * into a 404. The seller→buyer trade it used to fake is now
 * `POST /__e2e/steam/trade`, which moves the asset between inventories instead
 * of announcing a custody event.
 */
export const steamRouter = Router();

steamRouter.get('/api/inventory/:steamId', (req, res) => {
  // `?refresh=true` needs no handling: the fake serves no cache, so every read
  // is already fresh. The 08 §2.3 visibility travels in the body AND in the
  // status code, exactly as the real sidecar sends it.
  const { status, body } = inventoryResponse(req.params.steamId);
  res.status(status).json(body);
});

steamRouter.delete('/api/inventory/:steamId/cache', (_req, res) => {
  res.status(204).end();
});

steamRouter.get('/api/trade-hold/:steamId', (req, res) => {
  // `active` is the MOBILE AUTHENTICATOR flag, not the hold flag: the backend
  // maps active=true → SteamTradeHoldProbeResult.Active ("MA on, hold is 0
  // seconds"). Defaults to MA-verified/no-hold so every accept keeps working
  // untouched; a test drives `active: false` to exercise the 403
  // MOBILE_AUTHENTICATOR_REQUIRED branch T119a wired into the accept endpoint.
  res.json(getTradeHold(req.params.steamId));
});
