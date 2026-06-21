import { Router } from 'express';
import { config } from '../config.js';
import { logger } from '../logger.js';
import { lookupPaymentAddress, type DepositPaymentAddress } from '../db.js';
import { postWebhook } from '../webhookClient.js';
import { fakeTxHash, fakeTronAddress } from '../ids.js';

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

function detectedEnvelope(dep: DepositPaymentAddress, amount: string) {
  return {
    event: 'payment.detected',
    timestamp: new Date().toISOString(),
    data: {
      paymentAddressId: dep.paymentAddressId,
      transactionId: dep.transactionId,
      txHash: fakeTxHash(dep.transactionId),
      eventIndex: 0,
      fromAddress: BUYER_WALLET,
      toAddress: dep.address,
      contractAddress: '',
      tokenSymbol: dep.tokenSymbol,
      amount,
      blockTimestampMs: Date.now(),
      detectedAt: new Date().toISOString(),
    },
  };
}

function confirmedEnvelope(dep: DepositPaymentAddress) {
  return {
    event: 'payment.confirmed',
    timestamp: new Date().toISOString(),
    data: {
      paymentAddressId: dep.paymentAddressId,
      transactionId: dep.transactionId,
      txHash: fakeTxHash(dep.transactionId),
      eventIndex: 0,
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
}

// Detect only — records a DETECTED BlockchainTransaction row.
controlRouter.post('/__e2e/payment/detect', async (req, res) => {
  const { transactionId = '', amount } = (req.body ?? {}) as PaymentBody;
  const correlationId = req.correlationId ?? transactionId;
  try {
    const dep = await resolveDeposit(transactionId);
    if ('error' in dep) {
      res.status(404).json(dep);
      return;
    }
    const payAmount = amount ?? dep.expectedAmount;
    await postWebhook(
      '/api/v1/webhooks/blockchain/payment-detected',
      config.blockchainWebhookSecret,
      detectedEnvelope(dep, payAmount),
      correlationId,
    );
    res.json({ ok: true, phase: 'detected', transactionId, amount: payAmount });
  } catch (err) {
    logger.error({ err: String(err), transactionId }, 'payment/detect failed');
    res.status(500).json({ error: String(err) });
  }
});

// Confirm only — flips the row to CONFIRMED and runs amount validation.
controlRouter.post('/__e2e/payment/confirm', async (req, res) => {
  const { transactionId = '' } = (req.body ?? {}) as PaymentBody;
  const correlationId = req.correlationId ?? transactionId;
  try {
    const dep = await resolveDeposit(transactionId);
    if ('error' in dep) {
      res.status(404).json(dep);
      return;
    }
    await postWebhook(
      '/api/v1/webhooks/blockchain/payment-confirmed',
      config.blockchainWebhookSecret,
      confirmedEnvelope(dep),
      correlationId,
    );
    res.json({ ok: true, phase: 'confirmed', transactionId });
  } catch (err) {
    logger.error({ err: String(err), transactionId }, 'payment/confirm failed');
    res.status(500).json({ error: String(err) });
  }
});

// Pay — detect then confirm (exact expected amount) in one call. The detect
// webhook is awaited (backend commits the DETECTED row) before confirm, so the
// confirm handler always finds the row by (txHash, eventIndex). This is the
// happy-path entry point: one call → PAYMENT_RECEIVED.
controlRouter.post('/__e2e/payment/pay', async (req, res) => {
  const { transactionId = '', amount } = (req.body ?? {}) as PaymentBody;
  const correlationId = req.correlationId ?? transactionId;
  try {
    const dep = await resolveDeposit(transactionId);
    if ('error' in dep) {
      res.status(404).json(dep);
      return;
    }
    const payAmount = amount ?? dep.expectedAmount;
    await postWebhook(
      '/api/v1/webhooks/blockchain/payment-detected',
      config.blockchainWebhookSecret,
      detectedEnvelope(dep, payAmount),
      correlationId,
    );
    await postWebhook(
      '/api/v1/webhooks/blockchain/payment-confirmed',
      config.blockchainWebhookSecret,
      confirmedEnvelope(dep),
      correlationId,
    );
    res.json({
      ok: true,
      phase: 'paid',
      transactionId,
      paymentAddressId: dep.paymentAddressId,
      amount: payAmount,
    });
  } catch (err) {
    logger.error({ err: String(err), transactionId }, 'payment/pay failed');
    res.status(500).json({ error: String(err) });
  }
});
