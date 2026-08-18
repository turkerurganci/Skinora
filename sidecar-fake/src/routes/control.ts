import { Router } from 'express';
import type { Response } from 'express';
import { config } from '../config.js';
import { logger } from '../logger.js';
import { lookupPaymentAddress, type DepositPaymentAddress } from '../db.js';
import { postWebhook } from '../webhookClient.js';
import { fakeTxHash, fakeTronAddress } from '../ids.js';
import {
  InventoryControlError,
  getInventory,
  resetSteamState,
  setInventory,
  setTradeHold,
  simulateTrade,
} from '../inventoryStore.js';

/**
 * E2E control surface (NOT part of the real sidecar contract). The Playwright
 * harness calls these to simulate what the platform cannot observe by itself:
 * the buyer's on-chain payment (which the real blockchain monitor would see)
 * and the seller→buyer Steam trade (which the platform is not a party to at
 * all, 02 §2.1). Payment endpoints resolve the deposit PaymentAddress from the
 * DB so the inbound webhooks carry backend-valid ids + the exact expected
 * amount. No X-Internal-Key required (caller is the test).
 */
export const controlRouter = Router();

const BUYER_WALLET = fakeTronAddress(999_001);
const BASE_BLOCK = 60_000_000;

// TRC-20 stablecoin contract allowlist — mirror of the backend's
// KnownStablecoinContracts (AmountValidationService.cs) and the real sidecar's
// STABLECOIN_CONTRACTS_* env. lookupPaymentAddress returns only the token
// *symbol*, so the wrong-token webhook (which needs the expected + actual
// contract addresses) resolves the mapping here. The backend keys the actual
// stablecoin off ActualContractAddress, so these must match exactly.
const STABLECOIN_CONTRACTS: Record<string, string> = {
  USDT: 'TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t',
  USDC: 'TEkxiTehnzSmSe2XqrBj4w32RUN966rdz8',
};

// A deterministic TRC-20 contract that is NOT on the allowlist — drives the
// 03 §5.3a unsupported/spam-token path (backend records a terminal CONFIRMED
// SPAM_TOKEN_INCOMING row; no refund, transaction state untouched).
const UNSUPPORTED_CONTRACT = fakeTronAddress(666_001);

function contractFor(tokenSymbol: string): string {
  return STABLECOIN_CONTRACTS[tokenSymbol] ?? STABLECOIN_CONTRACTS.USDT;
}

/** The "other" supported stablecoin — what a 03 §5.3 wrong-token transfer
 *  arrives as (USDC when the buyer was billed USDT, and vice-versa). */
function otherStablecoin(tokenSymbol: string): string {
  return tokenSymbol === 'USDT' ? 'USDC' : 'USDT';
}

/** Deterministic txHash for a buyer-payment leg. eventIndex 0 keeps the
 *  original single-payment hash (backward compatible with the happy-path /
 *  timeout / cancellation smokes); a non-zero index derives a distinct hash so
 *  a SECOND on-chain transfer to the same deposit address clears the backend's
 *  (TxHash, EventIndex) idempotency key (06 §3.8, WP10) — the 03 §5.5 setup. */
function paymentTxHash(transactionId: string, eventIndex: number): string {
  return fakeTxHash(eventIndex === 0 ? transactionId : `${transactionId}#${eventIndex}`);
}

function detectedEnvelope(
  dep: DepositPaymentAddress,
  amount: string,
  txHash: string,
  eventIndex: number,
) {
  return {
    event: 'payment.detected',
    timestamp: new Date().toISOString(),
    data: {
      paymentAddressId: dep.paymentAddressId,
      transactionId: dep.transactionId,
      txHash,
      eventIndex,
      fromAddress: BUYER_WALLET,
      toAddress: dep.address,
      contractAddress: contractFor(dep.tokenSymbol),
      tokenSymbol: dep.tokenSymbol,
      amount,
      blockTimestampMs: Date.now(),
      detectedAt: new Date().toISOString(),
    },
  };
}

function confirmedEnvelope(dep: DepositPaymentAddress, txHash: string, eventIndex: number) {
  return {
    event: 'payment.confirmed',
    timestamp: new Date().toISOString(),
    data: {
      paymentAddressId: dep.paymentAddressId,
      transactionId: dep.transactionId,
      txHash,
      eventIndex,
      blockNumber: BASE_BLOCK,
      confirmationCount: 20,
      confirmedAt: new Date().toISOString(),
    },
  };
}

async function resolveDeposit(
  transactionId: string,
): Promise<DepositPaymentAddress | { error: string }> {
  if (!transactionId) {
    return { error: 'transactionId is required' };
  }
  const dep = await lookupPaymentAddress(transactionId);
  if (!dep) {
    return { error: `no PaymentAddress for transaction ${transactionId}` };
  }
  return dep;
}

interface PaymentBody {
  transactionId?: string;
  amount?: string;
  // Distinct on-chain event index for a second transfer to the same deposit
  // address (03 §5.5 multi-payment). Defaults to 0 (the single-payment path).
  eventIndex?: number;
  // 03 §5.3 — the supported-but-wrong stablecoin a wrong-token transfer lands
  // as. Defaults to the "other" allowlisted token.
  actualTokenSymbol?: string;
  // 03 §5.4 — post-cancel monitor state the late transfer is observed in.
  monitorState?: string;
}

// Detect only — records a DETECTED BlockchainTransaction row.
controlRouter.post('/__e2e/payment/detect', async (req, res) => {
  const { transactionId = '', amount, eventIndex = 0 } = (req.body ?? {}) as PaymentBody;
  const correlationId = req.correlationId ?? transactionId;
  try {
    const dep = await resolveDeposit(transactionId);
    if ('error' in dep) {
      res.status(404).json(dep);
      return;
    }
    const payAmount = amount ?? dep.expectedAmount;
    const txHash = paymentTxHash(dep.transactionId, eventIndex);
    await postWebhook(
      '/api/v1/webhooks/blockchain/payment-detected',
      config.blockchainWebhookSecret,
      detectedEnvelope(dep, payAmount, txHash, eventIndex),
      correlationId,
    );
    res.json({ ok: true, phase: 'detected', transactionId, amount: payAmount, eventIndex });
  } catch (err) {
    logger.error({ err: String(err), transactionId }, 'payment/detect failed');
    res.status(500).json({ error: String(err) });
  }
});

// Confirm only — flips the row to CONFIRMED and runs amount validation.
controlRouter.post('/__e2e/payment/confirm', async (req, res) => {
  const { transactionId = '', eventIndex = 0 } = (req.body ?? {}) as PaymentBody;
  const correlationId = req.correlationId ?? transactionId;
  try {
    const dep = await resolveDeposit(transactionId);
    if ('error' in dep) {
      res.status(404).json(dep);
      return;
    }
    const txHash = paymentTxHash(dep.transactionId, eventIndex);
    await postWebhook(
      '/api/v1/webhooks/blockchain/payment-confirmed',
      config.blockchainWebhookSecret,
      confirmedEnvelope(dep, txHash, eventIndex),
      correlationId,
    );
    res.json({ ok: true, phase: 'confirmed', transactionId, eventIndex });
  } catch (err) {
    logger.error({ err: String(err), transactionId }, 'payment/confirm failed');
    res.status(500).json({ error: String(err) });
  }
});

// ---------------------------------------------------------------------------
// Steam inventory control (T137). The P2P counterpart of the payment levers
// above: the platform never sees the seller→buyer trade, so a test drives the
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

// Drop every driven inventory + trade hold. Tests call this between scenarios
// so one scenario's seeded inventory never leaks into the next.
controlRouter.post('/__e2e/steam/reset', (_req, res) => {
  resetSteamState();
  res.json({ ok: true });
});

// Pay — detect then confirm (exact expected amount by default) in one call. The
// detect webhook is awaited (backend commits the DETECTED row) before confirm,
// so the confirm handler always finds the row by (txHash, eventIndex). This is
// the happy-path entry point: one call → PAYMENT_RECEIVED.
//
// T110 levers: pass `amount` to drive 03 §5.1 (insufficient) / §5.2 (excess);
// pass a non-zero `eventIndex` for the §5.5 multi-payment second transfer (a
// distinct (txHash, eventIndex) the backend treats as a fresh confirmed payment
// → full refund because the transaction already left ITEM_ESCROWED).
controlRouter.post('/__e2e/payment/pay', async (req, res) => {
  const { transactionId = '', amount, eventIndex = 0 } = (req.body ?? {}) as PaymentBody;
  const correlationId = req.correlationId ?? transactionId;
  try {
    const dep = await resolveDeposit(transactionId);
    if ('error' in dep) {
      res.status(404).json(dep);
      return;
    }
    const payAmount = amount ?? dep.expectedAmount;
    const txHash = paymentTxHash(dep.transactionId, eventIndex);
    await postWebhook(
      '/api/v1/webhooks/blockchain/payment-detected',
      config.blockchainWebhookSecret,
      detectedEnvelope(dep, payAmount, txHash, eventIndex),
      correlationId,
    );
    await postWebhook(
      '/api/v1/webhooks/blockchain/payment-confirmed',
      config.blockchainWebhookSecret,
      confirmedEnvelope(dep, txHash, eventIndex),
      correlationId,
    );
    res.json({
      ok: true,
      phase: 'paid',
      transactionId,
      paymentAddressId: dep.paymentAddressId,
      amount: payAmount,
      eventIndex,
    });
  } catch (err) {
    logger.error({ err: String(err), transactionId }, 'payment/pay failed');
    res.status(500).json({ error: String(err) });
  }
});

// Wrong token (03 §5.3) — a supported TRC-20 stablecoin that differs from the
// one the buyer was billed (USDC when expecting USDT). Backend records a
// WRONG_TOKEN_INCOMING row and queues a WRONG_TOKEN_REFUND; the transaction
// stays ITEM_ESCROWED and the timeout keeps running. Single webhook (no
// confirm) — the sidecar classifies the token before raising this.
controlRouter.post('/__e2e/payment/wrong-token', async (req, res) => {
  const {
    transactionId = '',
    amount,
    actualTokenSymbol,
    eventIndex = 0,
  } = (req.body ?? {}) as PaymentBody;
  const correlationId = req.correlationId ?? transactionId;
  try {
    const dep = await resolveDeposit(transactionId);
    if ('error' in dep) {
      res.status(404).json(dep);
      return;
    }
    const actual = actualTokenSymbol ?? otherStablecoin(dep.tokenSymbol);
    const payAmount = amount ?? dep.expectedAmount;
    const txHash = fakeTxHash(`${dep.transactionId}#wrong-token`);
    await postWebhook(
      '/api/v1/webhooks/blockchain/wrong-token',
      config.blockchainWebhookSecret,
      {
        event: 'payment.wrong_token',
        timestamp: new Date().toISOString(),
        data: {
          paymentAddressId: dep.paymentAddressId,
          transactionId: dep.transactionId,
          txHash,
          eventIndex,
          fromAddress: BUYER_WALLET,
          toAddress: dep.address,
          expectedContractAddress: contractFor(dep.tokenSymbol),
          actualContractAddress: contractFor(actual),
          actualTokenSymbol: actual,
          amount: payAmount,
          blockTimestampMs: Date.now(),
          detectedAt: new Date().toISOString(),
        },
      },
      correlationId,
    );
    res.json({
      ok: true,
      phase: 'wrong-token',
      transactionId,
      actualTokenSymbol: actual,
      amount: payAmount,
    });
  } catch (err) {
    logger.error({ err: String(err), transactionId }, 'payment/wrong-token failed');
    res.status(500).json({ error: String(err) });
  }
});

// Unsupported / spam token (03 §5.3a) — a token NOT on the platform allowlist.
// Backend records a terminal CONFIRMED SPAM_TOKEN_INCOMING audit row, attempts
// no refund, and leaves the transaction in its current state (ITEM_ESCROWED).
controlRouter.post('/__e2e/payment/spam-token', async (req, res) => {
  const { transactionId = '', amount, eventIndex = 0 } = (req.body ?? {}) as PaymentBody;
  const correlationId = req.correlationId ?? transactionId;
  try {
    const dep = await resolveDeposit(transactionId);
    if ('error' in dep) {
      res.status(404).json(dep);
      return;
    }
    const payAmount = amount ?? dep.expectedAmount;
    const txHash = fakeTxHash(`${dep.transactionId}#spam`);
    await postWebhook(
      '/api/v1/webhooks/blockchain/spam-token',
      config.blockchainWebhookSecret,
      {
        event: 'payment.spam_token',
        timestamp: new Date().toISOString(),
        data: {
          paymentAddressId: dep.paymentAddressId,
          transactionId: dep.transactionId,
          txHash,
          eventIndex,
          fromAddress: BUYER_WALLET,
          toAddress: dep.address,
          expectedContractAddress: contractFor(dep.tokenSymbol),
          actualContractAddress: UNSUPPORTED_CONTRACT,
          amount: payAmount,
          blockTimestampMs: Date.now(),
          detectedAt: new Date().toISOString(),
        },
      },
      correlationId,
    );
    res.json({ ok: true, phase: 'spam-token', transactionId, amount: payAmount });
  } catch (err) {
    logger.error({ err: String(err), transactionId }, 'payment/spam-token failed');
    res.status(500).json({ error: String(err) });
  }
});

// Late payment (03 §5.4) — a buyer transfer that lands at a cancelled
// transaction's deposit address while still inside the post-cancel monitoring
// window. Backend records a BUYER_PAYMENT row + queues a LATE_PAYMENT_REFUND
// (T73 pipeline). The transaction stays in its terminal cancel state. Single
// webhook; monitorState defaults to POST_CANCEL_24H (the window opened by a
// payment timeout).
controlRouter.post('/__e2e/payment/late-detected', async (req, res) => {
  const {
    transactionId = '',
    amount,
    monitorState,
    eventIndex = 0,
  } = (req.body ?? {}) as PaymentBody;
  const correlationId = req.correlationId ?? transactionId;
  try {
    const dep = await resolveDeposit(transactionId);
    if ('error' in dep) {
      res.status(404).json(dep);
      return;
    }
    const payAmount = amount ?? dep.expectedAmount;
    const state = monitorState ?? 'POST_CANCEL_24H';
    const txHash = fakeTxHash(`${dep.transactionId}#late`);
    await postWebhook(
      '/api/v1/webhooks/blockchain/late-payment-detected',
      config.blockchainWebhookSecret,
      {
        event: 'payment.late_detected',
        timestamp: new Date().toISOString(),
        data: {
          paymentAddressId: dep.paymentAddressId,
          transactionId: dep.transactionId,
          txHash,
          eventIndex,
          fromAddress: BUYER_WALLET,
          toAddress: dep.address,
          contractAddress: contractFor(dep.tokenSymbol),
          tokenSymbol: dep.tokenSymbol,
          amount: payAmount,
          blockTimestampMs: Date.now(),
          detectedAt: new Date().toISOString(),
          monitorState: state,
        },
      },
      correlationId,
    );
    res.json({
      ok: true,
      phase: 'late-detected',
      transactionId,
      monitorState: state,
      amount: payAmount,
    });
  } catch (err) {
    logger.error({ err: String(err), transactionId }, 'payment/late-detected failed');
    res.status(500).json({ error: String(err) });
  }
});
