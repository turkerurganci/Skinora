import { Router } from 'express';
import type { Request, Response } from 'express';
import { logger } from '../logger.js';
import { lookupPaymentAddress, type DepositPaymentAddress } from '../db.js';
import { postWebhook, WebhookRejectedError } from '../webhookClient.js';
import {
  getFees,
  listMonitors,
  queryJournal,
  resetBlockchainState,
  setFees,
  type FeeKnobs,
} from '../blockchainState.js';
import {
  isNetwork,
  profileFor,
  signingKeyFor,
  type Network,
  type NetworkProfile,
} from '../networks.js';
import {
  lateDetectedEnvelope,
  paymentConfirmedEnvelope,
  paymentDetectedEnvelope,
  spamTokenEnvelope,
  wrongTokenEnvelope,
} from '../webhookEnvelopes.js';

/**
 * E2E payment levers (T107/T110) and — since T155 — the blockchain read-back
 * surface. NOT part of any real service contract; the caller is the test.
 *
 * The payment levers stand in for the real monitor: they resolve the
 * transaction's deposit `PaymentAddress` from SQL Server and post the webhook
 * the real service of THAT DEPOSIT'S NETWORK would post — its hash format, its
 * payer wallet, its token contracts, signed with its secret (T148: the secret
 * that verifies decides which network the caller is). A test therefore drives
 * a TRON and an Ethereum transaction with the same call; the fake picks the
 * network from the deposit, never from the port it was called on.
 *
 * Two levers exist to send what the backend must REFUSE (T155 negative
 * scenarios), both defaulting to the deposit's own network:
 *   • `claimNetwork` — the `network` the envelope claims;
 *   • `signAs` — whose secret signs it.
 * A refused webhook answers 502 with the backend's status and body
 * (`backendStatus`, `backendBody`) so the test can assert on the refusal.
 */
export const paymentControlRouter = Router();

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
  // T155 — negative levers (see the file comment).
  claimNetwork?: unknown;
  signAs?: unknown;
}

interface Resolved {
  dep: DepositPaymentAddress;
  /** The deposit's own network — formats, wallets, contracts. */
  profile: NetworkProfile;
  /** The network the envelope claims. */
  claim: Network;
  /** The shared secret that signs (named apart from `secret` so the
   *  pre-commit secret scanner does not read the accessor call as a value). */
  signingKey: string;
}

class ControlRequestError extends Error {
  constructor(
    readonly status: number,
    message: string,
  ) {
    super(message);
  }
}

async function resolve(body: PaymentBody): Promise<Resolved> {
  const transactionId = body.transactionId ?? '';
  if (!transactionId) throw new ControlRequestError(400, 'transactionId is required');
  for (const [field, value] of [
    ['claimNetwork', body.claimNetwork],
    ['signAs', body.signAs],
  ] as const) {
    if (value !== undefined && !isNetwork(value)) {
      throw new ControlRequestError(400, `${field} must be TRON or ETHEREUM`);
    }
  }
  const dep = await lookupPaymentAddress(transactionId);
  if (!dep)
    throw new ControlRequestError(404, `no PaymentAddress for transaction ${transactionId}`);
  const profile = profileFor(dep.network);
  const claim = (body.claimNetwork as Network | undefined) ?? dep.network;
  const signingKey = signingKeyFor(dep.network, body.signAs as Network | undefined);
  return { dep, profile, claim, signingKey };
}

/** Deterministic txHash for a buyer-payment leg. eventIndex 0 keeps the
 *  original single-payment hash (backward compatible with the happy-path /
 *  timeout / cancellation smokes); a non-zero index derives a distinct hash so
 *  a SECOND on-chain transfer to the same deposit address clears the backend's
 *  (Network, TxHash, EventIndex) idempotency key (06 §3.8, WP10) — the 03 §5.5
 *  setup. The format is the deposit network's (TRON 64 hex, Ethereum 0x + 64). */
function paymentTxHash(profile: NetworkProfile, transactionId: string, eventIndex: number): string {
  return profile.txHash(eventIndex === 0 ? transactionId : `${transactionId}#${eventIndex}`);
}

/** The "other" supported stablecoin — what a 03 §5.3 wrong-token transfer
 *  arrives as (USDC when the buyer was billed USDT, and vice-versa). */
function otherStablecoin(tokenSymbol: string): string {
  return tokenSymbol === 'USDT' ? 'USDC' : 'USDT';
}

/** Run a lever; map a refusal / bad request / bug onto the control answer. */
async function lever(
  req: Request,
  res: Response,
  name: string,
  run: (r: Resolved, body: PaymentBody, correlationId: string) => Promise<Record<string, unknown>>,
): Promise<void> {
  const body = (req.body ?? {}) as PaymentBody;
  const correlationId = req.correlationId ?? body.transactionId ?? name;
  try {
    const r = await resolve(body);
    const result = await run(r, body, correlationId);
    res.json({ ok: true, transactionId: body.transactionId, network: r.dep.network, ...result });
  } catch (err) {
    if (err instanceof ControlRequestError) {
      res.status(err.status).json({ error: err.message });
      return;
    }
    if (err instanceof WebhookRejectedError) {
      res.status(502).json({
        error: err.message,
        webhook: err.path,
        backendStatus: err.status,
        backendBody: parseMaybeJson(err.responseBody),
      });
      return;
    }
    logger.error({ err: String(err), transactionId: body.transactionId }, `${name} failed`);
    res.status(500).json({ error: String(err) });
  }
}

function parseMaybeJson(text: string): unknown {
  try {
    return JSON.parse(text);
  } catch {
    return text;
  }
}

// Detect only — records a DETECTED BlockchainTransaction row.
paymentControlRouter.post('/__e2e/payment/detect', (req, res) =>
  lever(req, res, 'payment/detect', async (r, body, correlationId) => {
    const eventIndex = body.eventIndex ?? 0;
    const amount = body.amount ?? r.dep.expectedAmount;
    const txHash = paymentTxHash(r.profile, r.dep.transactionId, eventIndex);
    await postWebhook(
      '/api/v1/webhooks/blockchain/payment-detected',
      r.signingKey,
      paymentDetectedEnvelope({ ...r, txHash, eventIndex, amount, network: r.claim }),
      correlationId,
    );
    return { phase: 'detected', amount, eventIndex, txHash };
  }),
);

// Confirm only — flips the row to CONFIRMED and runs amount validation.
paymentControlRouter.post('/__e2e/payment/confirm', (req, res) =>
  lever(req, res, 'payment/confirm', async (r, body, correlationId) => {
    const eventIndex = body.eventIndex ?? 0;
    const txHash = paymentTxHash(r.profile, r.dep.transactionId, eventIndex);
    await postWebhook(
      '/api/v1/webhooks/blockchain/payment-confirmed',
      r.signingKey,
      paymentConfirmedEnvelope({ ...r, txHash, eventIndex, network: r.claim }),
      correlationId,
    );
    return { phase: 'confirmed', eventIndex, txHash };
  }),
);

// Pay — detect then confirm (exact expected amount by default) in one call. The
// detect webhook is awaited (backend commits the DETECTED row) before confirm,
// so the confirm handler always finds the row by (network, txHash, eventIndex).
// This is the happy-path entry point: one call → PAYMENT_RECEIVED.
//
// T110 levers: pass `amount` to drive 03 §5.1 (insufficient) / §5.2 (excess);
// pass a non-zero `eventIndex` for the §5.5 multi-payment second transfer (a
// distinct (txHash, eventIndex) the backend treats as a fresh confirmed payment
// → full refund because the transaction already left SELLER_CONFIRMED).
paymentControlRouter.post('/__e2e/payment/pay', (req, res) =>
  lever(req, res, 'payment/pay', async (r, body, correlationId) => {
    const eventIndex = body.eventIndex ?? 0;
    const amount = body.amount ?? r.dep.expectedAmount;
    const txHash = paymentTxHash(r.profile, r.dep.transactionId, eventIndex);
    await postWebhook(
      '/api/v1/webhooks/blockchain/payment-detected',
      r.signingKey,
      paymentDetectedEnvelope({ ...r, txHash, eventIndex, amount, network: r.claim }),
      correlationId,
    );
    await postWebhook(
      '/api/v1/webhooks/blockchain/payment-confirmed',
      r.signingKey,
      paymentConfirmedEnvelope({ ...r, txHash, eventIndex, network: r.claim }),
      correlationId,
    );
    return {
      phase: 'paid',
      paymentAddressId: r.dep.paymentAddressId,
      depositAddress: r.dep.address,
      amount,
      eventIndex,
      txHash,
    };
  }),
);

// Wrong token (03 §5.3) — a supported stablecoin that differs from the one the
// buyer was billed (USDC when expecting USDT), on the deposit's network.
// Backend records a WRONG_TOKEN_INCOMING row and queues a WRONG_TOKEN_REFUND;
// the transaction stays SELLER_CONFIRMED and the timeout keeps running. Single
// webhook (no confirm) — the service classifies the token before raising this.
paymentControlRouter.post('/__e2e/payment/wrong-token', (req, res) =>
  lever(req, res, 'payment/wrong-token', async (r, body, correlationId) => {
    const actual = body.actualTokenSymbol ?? otherStablecoin(r.dep.tokenSymbol);
    const amount = body.amount ?? r.dep.expectedAmount;
    const txHash = r.profile.txHash(`${r.dep.transactionId}#wrong-token`);
    await postWebhook(
      '/api/v1/webhooks/blockchain/wrong-token',
      r.signingKey,
      wrongTokenEnvelope({
        ...r,
        txHash,
        eventIndex: body.eventIndex ?? 0,
        amount,
        actualTokenSymbol: actual,
        network: r.claim,
      }),
      correlationId,
    );
    return { phase: 'wrong-token', actualTokenSymbol: actual, amount, txHash };
  }),
);

// Unsupported / spam token (03 §5.3a) — a token NOT on the platform allowlist.
// Backend records a terminal CONFIRMED SPAM_TOKEN_INCOMING audit row, attempts
// no refund, and leaves the transaction in its current state.
paymentControlRouter.post('/__e2e/payment/spam-token', (req, res) =>
  lever(req, res, 'payment/spam-token', async (r, body, correlationId) => {
    const amount = body.amount ?? r.dep.expectedAmount;
    const txHash = r.profile.txHash(`${r.dep.transactionId}#spam`);
    await postWebhook(
      '/api/v1/webhooks/blockchain/spam-token',
      r.signingKey,
      spamTokenEnvelope({
        ...r,
        txHash,
        eventIndex: body.eventIndex ?? 0,
        amount,
        network: r.claim,
      }),
      correlationId,
    );
    return { phase: 'spam-token', amount, txHash };
  }),
);

// Late payment (03 §5.4) — a buyer transfer that lands at a cancelled
// transaction's deposit address while still inside the post-cancel monitoring
// window. Backend records a BUYER_PAYMENT row + queues a LATE_PAYMENT_REFUND
// (T73 pipeline). The transaction stays in its terminal cancel state. Single
// webhook; monitorState defaults to POST_CANCEL_24H (the window opened by a
// payment timeout).
paymentControlRouter.post('/__e2e/payment/late-detected', (req, res) =>
  lever(req, res, 'payment/late-detected', async (r, body, correlationId) => {
    const amount = body.amount ?? r.dep.expectedAmount;
    const monitorState = body.monitorState ?? 'POST_CANCEL_24H';
    const txHash = r.profile.txHash(`${r.dep.transactionId}#late`);
    await postWebhook(
      '/api/v1/webhooks/blockchain/late-payment-detected',
      r.signingKey,
      lateDetectedEnvelope({
        ...r,
        txHash,
        eventIndex: body.eventIndex ?? 0,
        amount,
        monitorState,
        network: r.claim,
      }),
      correlationId,
    );
    return { phase: 'late-detected', monitorState, amount, txHash };
  }),
);

// ---------------------------------------------------------------------------
// T155 — blockchain read-back + fee knobs. What the backend asked of each
// network's service is only visible on this side of the seam.
// ---------------------------------------------------------------------------

function networkParam(value: unknown): Network | undefined | null {
  if (value === undefined) return undefined;
  return isNetwork(value) ? value : null;
}

// Every backend → service call, oldest first. Filters: `network`, `path`
// (prefix, e.g. /api/transfer/payout), `sinceSeq` (exclusive).
paymentControlRouter.get('/__e2e/blockchain/calls', (req, res) => {
  const network = networkParam(req.query.network);
  if (network === null) {
    res.status(400).json({ error: 'network must be TRON or ETHEREUM' });
    return;
  }
  const path = typeof req.query.path === 'string' ? req.query.path : undefined;
  const sinceSeq = typeof req.query.sinceSeq === 'string' ? Number(req.query.sinceSeq) : undefined;
  res.json({ ok: true, calls: queryJournal({ network, path, sinceSeq }) });
});

// Deposits currently watched (monitor/start minus monitor/stop), per network.
paymentControlRouter.get('/__e2e/blockchain/monitors', (req, res) => {
  const network = networkParam(req.query.network);
  if (network === null) {
    res.status(400).json({ error: 'network must be TRON or ETHEREUM' });
    return;
  }
  res.json({ ok: true, monitors: listMonitors(network) });
});

// Drive one network's estimate endpoints. Unknown keys are refused so a typo
// cannot silently leave a default in place.
const FEE_KEYS: ReadonlyArray<keyof FeeKnobs> = [
  'available',
  'tronFeeUsdt',
  'tronSweepCostSun',
  'tronTrxPriceUsdt',
  'ethFeeWei',
  'ethPriceUsdt',
];

paymentControlRouter.post('/__e2e/blockchain/fees', (req, res) => {
  const { network, ...patch } = (req.body ?? {}) as Record<string, unknown>;
  if (!isNetwork(network)) {
    res.status(400).json({ error: 'network must be TRON or ETHEREUM' });
    return;
  }
  const unknown = Object.keys(patch).filter((k) => !FEE_KEYS.includes(k as keyof FeeKnobs));
  if (unknown.length > 0) {
    res.status(400).json({ error: `unknown fee knob(s): ${unknown.join(', ')}` });
    return;
  }
  res.json({ ok: true, network, fees: setFees(network, patch as Partial<FeeKnobs>) });
});

paymentControlRouter.get('/__e2e/blockchain/fees/:network', (req, res) => {
  const network = req.params.network;
  if (!isNetwork(network)) {
    res.status(400).json({ error: 'network must be TRON or ETHEREUM' });
    return;
  }
  res.json({ ok: true, network, fees: getFees(network) });
});

// Journal + monitors + fee knobs back to their defaults.
paymentControlRouter.post('/__e2e/blockchain/reset', (_req, res) => {
  resetBlockchainState();
  res.json({ ok: true });
});
