import { Router } from 'express';
import { config } from '../config.js';
import { logger } from '../logger.js';
import { lookupPaymentAddress, type DepositPaymentAddress } from '../db.js';
import { postWebhook } from '../webhookClient.js';
import { fakeTxHash, fakeTronAddress } from '../ids.js';
import { suppressAccept, clearSuppressions, listSuppressed } from '../tradeControl.js';

/**
 * E2E control surface (NOT part of the real sidecar contract). The Playwright
 * harness calls these to simulate on-chain buyer payment, which the real
 * blockchain monitor would otherwise observe. Resolves the deposit
 * PaymentAddress from the DB so the inbound webhooks carry backend-valid ids +
 * the exact expected amount. No X-Internal-Key required (caller is the test).
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

// Trade auto-accept suppression (T109 — timeout scenarios). The default fake
// behaviour self-accepts every offer; a test calls this to hold a specific
// dispatch leg at "sent" so the transaction parks in TRADE_OFFER_SENT_TO_* and
// the backend deadline scanner can time it out (03 §4.2 / §4.4). Direction is
// one of SELLER_TO_BOT / BOT_TO_BUYER / BOT_TO_SELLER_REFUND.
controlRouter.post('/__e2e/trade/suppress-accept', (req, res) => {
  const { direction } = (req.body ?? {}) as { direction?: string };
  if (!direction) {
    res.status(400).json({ error: 'direction is required' });
    return;
  }
  suppressAccept(direction);
  logger.info({ direction }, 'trade auto-accept suppressed (T109)');
  res.json({ ok: true, suppressed: listSuppressed() });
});

// Clear every trade-accept suppression — restores the default self-drive. Tests
// call this between scenarios so a held direction never leaks across tests.
controlRouter.post('/__e2e/trade/reset', (_req, res) => {
  clearSuppressions();
  res.json({ ok: true, suppressed: [] });
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
