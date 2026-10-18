import { Router } from 'express';
import type { Response } from 'express';
import { logger } from '../logger.js';
import {
  InventoryControlError,
  getInventory,
  resetSteamState,
  setInventory,
  setLimitedAccount,
  setTradeHold,
  simulateTrade,
} from '../inventoryStore.js';
import { paymentControlRouter } from './paymentControl.js';

/**
 * E2E control surface (NOT part of the real sidecar contract). The Playwright
 * harness calls these to simulate what the platform cannot observe by itself:
 * the buyer's on-chain payment (which the real blockchain monitor would see —
 * `paymentControl.ts`, network-aware since T155) and the seller→buyer Steam
 * trade (which the platform is not a party to at all, 02 §2.1 — below). No
 * X-Internal-Key required (caller is the test).
 */
export const controlRouter = Router();

controlRouter.use(paymentControlRouter);

// ---------------------------------------------------------------------------
// Steam inventory control (T137). The P2P counterpart of the payment levers
// (paymentControl.ts): the platform never sees the seller→buyer trade, so a test drives the
// inventories the backend reads instead of announcing a custody event. The
// retired custody levers (`/__e2e/trade/suppress-accept` + `/__e2e/trade/reset`)
// held a bot dispatch at "sent"; that path no longer exists anywhere in the
// stack, and their old path names are deliberately NOT reused here so a stale
// reader cannot mistake one surface for the other.
// ---------------------------------------------------------------------------

interface InventorySetBody {
  steamId?: string;
  items?: unknown;
  visibility?: unknown;
}

interface TradeBody {
  fromSteamId?: string;
  toSteamId?: string;
  assetId?: string;
}

interface TradeHoldBody {
  steamId?: string;
  active?: boolean;
  escrowEndDurationSeconds?: number;
}

interface LimitedAccountBody {
  steamId?: string;
  limited?: boolean;
}

/** Map a control-surface validation failure onto 400; anything else is a bug. */
function handleControlError(err: unknown, res: Response): void {
  if (err instanceof InventoryControlError) {
    res.status(400).json({ error: err.message });
    return;
  }
  logger.error({ err: String(err) }, 'steam inventory control failed');
  res.status(500).json({ error: String(err) });
}

// Seed one steamId's holdings and/or readability. `items` replaces the whole
// inventory (each entry either a `catalog` name, explicit fields, or both);
// `visibility` drives the 08 §2.3 three-valued read (PUBLIC / PRIVATE /
// UNAVAILABLE). Either field may be omitted to leave that half untouched.
controlRouter.post('/__e2e/steam/inventory', (req, res) => {
  const { steamId = '', items, visibility } = (req.body ?? {}) as InventorySetBody;
  try {
    const entry = setInventory(steamId, { items, visibility });
    logger.info(
      { steamId, count: entry.items.length, visibility: entry.visibility },
      'inventory seeded',
    );
    res.json({ ok: true, steamId, ...entry });
  } catch (err) {
    handleControlError(err, res);
  }
});

// Read back what the store holds for a steamId — assertion + debugging aid.
// Unlike GET /api/inventory/:steamId this always answers 200, because it
// reports the STORED state rather than simulating a Steam read.
controlRouter.get('/__e2e/steam/inventory/:steamId', (req, res) => {
  const steamId = req.params.steamId;
  res.json({ ok: true, steamId, ...getInventory(steamId) });
});

// The seller→buyer trade (02 §2.1). Moves one asset between inventories and
// rotates its asset id the way Steam does (06 §8.4), so the seller-side lookup
// of the original id stops finding it while the buyer-side class count rises by
// one — the two halves of the 02 §9.2 delivery evidence. Call it in the other
// direction to simulate the seller pulling the trade back (T129 reversal).
controlRouter.post('/__e2e/steam/trade', (req, res) => {
  const { fromSteamId = '', toSteamId = '', assetId = '' } = (req.body ?? {}) as TradeBody;
  const result = simulateTrade(fromSteamId, toSteamId, assetId);
  if (!result.ok) {
    res.status(400).json({ error: result.error });
    return;
  }
  logger.info(
    { fromSteamId, toSteamId, assetId, newAssetId: result.newAssetId },
    'simulated seller→buyer trade',
  );
  res.json({ ok: true, fromSteamId, toSteamId, assetId, newAssetId: result.newAssetId });
});

// Drive the 08 §2.2 MA / trade-hold probe for one steamId. `active: false`
// means "no mobile authenticator", which the accept endpoint answers with 403
// MOBILE_AUTHENTICATOR_REQUIRED (T119a).
controlRouter.post('/__e2e/steam/trade-hold', (req, res) => {
  const { steamId = '', active, escrowEndDurationSeconds } = (req.body ?? {}) as TradeHoldBody;
  try {
    const state = setTradeHold(steamId, { active, escrowEndDurationSeconds });
    res.json({ ok: true, steamId, ...state });
  } catch (err) {
    handleControlError(err, res);
  }
});

// Drive the 08 §2.2a limited-account probe for one steamId. `limited: true`
// means "Steam blocks this account from trading at all", which the gates answer
// with 403 STEAM_ACCOUNT_LIMITED — the condition that stopped the 2026-09-02
// rehearsal AFTER the buyer's payment had already been confirmed on-chain.
controlRouter.post('/__e2e/steam/limited-account', (req, res) => {
  const { steamId = '', limited } = (req.body ?? {}) as LimitedAccountBody;
  try {
    const state = setLimitedAccount(steamId, { limited });
    res.json({ ok: true, steamId, ...state });
  } catch (err) {
    handleControlError(err, res);
  }
});

// Drop every driven inventory + trade hold + limited flag. Tests call this
// between scenarios so one scenario's seeded state never leaks into the next.
controlRouter.post('/__e2e/steam/reset', (_req, res) => {
  resetSteamState();
  res.json({ ok: true });
});
