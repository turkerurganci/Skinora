import { test, expect } from '@playwright/test';
import {
  seedHappyPath,
  backdateDeadline,
  pollPostCancelMonitoring,
  pollBlockchainTxConfirmed,
  pollNotificationRecipients,
  getExpectedAmount,
  closePool,
  seed,
  fakeBuyerWallet,
} from '../src/db';
import { mintAccessToken } from '../src/jwt';
import * as api from '../src/api';

/**
 * T110 payment edge cases, rewritten for P2P (T138) — drives 03 §5 at the API
 * level against the docker-compose.e2e.yml stack. Every branch is wired backend-
 * side (AmountValidationService — 02 §4.4 / 08 §3.4); the harness supplies four
 * fake levers (wrong-token, spam-token, late-detected webhooks + a distinct
 * second buyer payment).
 *
 * Covers 03 §5.1–§5.5 (+ §5.3a):
 *   1. Insufficient (§5.1)  → INCORRECT_AMOUNT_REFUND, tx stays SELLER_CONFIRMED.
 *   2. Excess (§5.2)        → accepted (PAYMENT_RECEIVED) + EXCESS_REFUND (excess only).
 *   3. Wrong token (§5.3)   → WRONG_TOKEN_REFUND, tx stays SELLER_CONFIRMED.
 *   4. Unsupported (§5.3a)  → SPAM_TOKEN_INCOMING audit row, no refund, no state change.
 *   5. Late payment (§5.4)  → LATE_PAYMENT_REFUND after a payment-timeout cancel.
 *   6. Multi-payment (§5.5) → first exact accepted, second refunded in full.
 * Every refund returns to the payment source wallet (08 §562 = fakeBuyerWallet),
 * NOT the trade-side refund wallet. Refund amounts are chosen so net > 2× gas
 * (gas estimate 2.0, threshold 4.0) → the refund proceeds rather than blocking.
 *
 * The P2P rewrite moved ONE thing, and it moved everything with it: the state a
 * payment is legal in. `AmountValidationService` accepts a transfer only in
 * SELLER_CONFIRMED — which used to be ITEM_ESCROWED, the state the platform
 * entered by taking custody of the item. It is now the state the platform enters
 * by VERIFYING the seller can still send, and reaching it takes the seller's own
 * confirm-ready call. That is not cosmetic for this suite: the branch order in
 * AmountValidationService keys the §5.5 multi-payment arm off "status is not
 * SELLER_CONFIRMED", so a setup that stopped one step short would silently route
 * every scenario down the multi-payment path instead of the one under test.
 */

test.beforeEach(async () => {
  // No edge case drives an inventory itself, but seedHappyPath does — reset
  // first so this suite starts from an empty, readable world and the seed's
  // seller inventory is the only thing on the fake.
  await api.resetFakeSteamState();
});

test.afterAll(async () => {
  await api.resetFakeSteamState();
  await closePool();
});

function tokens(): { sellerToken: string; buyerToken: string } {
  return {
    sellerToken: mintAccessToken({ userId: seed.sellerId, steamId: seed.sellerSteamId }),
    buyerToken: mintAccessToken({ userId: seed.buyerId, steamId: seed.buyerSteamId }),
  };
}

/** Fresh seed → seller creates → buyer accepts → seller confirms readiness →
 *  SELLER_CONFIRMED, the only state in which a payment is accepted at all. Each
 *  test re-seeds, so notification rows and prior blockchain rows never carry
 *  over. */
async function createAcceptConfirmReady(): Promise<{
  txId: string;
  sellerToken: string;
  buyerToken: string;
}> {
  await seedHappyPath();
  const { sellerToken, buyerToken } = tokens();

  const create = await api.createTransaction(sellerToken, {
    itemAssetId: seed.itemAssetId,
    stablecoin: 'USDT',
    price: seed.price,
    paymentTimeoutHours: 1,
    buyerIdentificationMethod: 'STEAM_ID',
    buyerSteamId: seed.buyerSteamId,
  });
  expect(create.ok, `create failed: ${JSON.stringify(create.body)}`).toBeTruthy();
  const created = api.unwrap(create.body);
  expect(created.status).toBe('CREATED');
  const txId = String(created.id);

  const accept = await api.acceptTransaction(buyerToken, txId, seed.buyerRefundAddress);
  expect(accept.ok, `accept failed: ${JSON.stringify(accept.body)}`).toBeTruthy();
  expect(api.unwrap(accept.body).status).toBe('ACCEPTED');

  const ready = await api.confirmReady(sellerToken, txId);
  expect(ready.ok, `confirm-ready failed: ${JSON.stringify(ready.body)}`).toBeTruthy();
  expect(api.unwrap(ready.body).status).toBe('SELLER_CONFIRMED');

  return { txId, sellerToken, buyerToken };
}

function statusOf(body: unknown): string {
  return String(api.unwrap(body).status);
}

test('§5.1 insufficient amount → INCORRECT_AMOUNT_REFUND, tx stays SELLER_CONFIRMED, buyer notified', async () => {
  const { txId, buyerToken } = await createAcceptConfirmReady();

  // Buyer underpays (10 of the expected ~102). Net 10 − 2 gas = 8 ≥ 4 threshold.
  const pay = await api.payViaFake(txId, { amount: '10.00' });
  expect(pay.ok, `pay failed: ${JSON.stringify(pay.body)}`).toBeTruthy();

  // 03 §5.1 step 4 — the platform does NOT accept the payment; the transaction
  // stays SELLER_CONFIRMED and the payment timeout keeps running (no advance).
  expect(statusOf((await api.getTransaction(buyerToken, txId)).body)).toBe('SELLER_CONFIRMED');

  // step 5 — the received amount is refunded to the buyer's source wallet.
  const refund = await pollBlockchainTxConfirmed(txId, 'INCORRECT_AMOUNT_REFUND');
  expect(refund, 'INCORRECT_AMOUNT_REFUND not queued').not.toBeNull();
  expect(refund?.status).toBe('CONFIRMED');
  expect(Number(refund?.amount)).toBe(10);
  expect(refund?.toAddress).toBe(fakeBuyerWallet);

  // step 6 — buyer notified.
  const recipients = await pollNotificationRecipients('INSUFFICIENT_PAYMENT', [seed.buyerId]);
  expect(recipients).toContain(seed.buyerId.toLowerCase());
});

test('§5.2 excess amount → accepted (PAYMENT_RECEIVED) + EXCESS_REFUND (excess only), buyer notified', async () => {
  const { txId, buyerToken } = await createAcceptConfirmReady();

  // The buyer's payable is ExpectedAmount = listing price + buyer commission
  // (≈102 for a 100 listing, 02 §4.6) — NOT the bare price. Read it so the
  // asserted excess does not hard-code the fee. Overpay by a comfortable margin
  // so the excess clears the 2× gas threshold (4.0) and the refund proceeds.
  const expectedAmount = await getExpectedAmount(txId);
  expect(expectedAmount).toBeGreaterThan(0);
  const overpayMargin = 20;
  const sentAmount = expectedAmount + overpayMargin;
  const pay = await api.payViaFake(txId, { amount: sentAmount.toFixed(6) });
  expect(pay.ok, `pay failed: ${JSON.stringify(pay.body)}`).toBeTruthy();

  // 03 §5.2 step 4 — the platform accepts the correct amount and the transaction
  // advances to PAYMENT_RECEIVED. Confirm is synchronous, and PAYMENT_RECEIVED
  // is a RESTING state in P2P — nothing (no bot, no job) moves it on — so this
  // is a plain equality rather than the custody-era "one of these four states".
  expect(statusOf((await api.getTransaction(buyerToken, txId)).body)).toBe('PAYMENT_RECEIVED');

  // step 5 — ONLY the excess (received − expected) is refunded to the buyer's
  // source wallet. The EXCESS_REFUND row carries the gross excess (gas is
  // deducted at broadcast, not from the stored Amount).
  const refund = await pollBlockchainTxConfirmed(txId, 'EXCESS_REFUND');
  expect(refund, 'EXCESS_REFUND not queued').not.toBeNull();
  expect(refund?.status).toBe('CONFIRMED');
  expect(Number(refund?.amount)).toBeCloseTo(overpayMargin, 2);
  expect(refund?.toAddress).toBe(fakeBuyerWallet);

  // step 7 — buyer notified.
  const recipients = await pollNotificationRecipients('OVERPAYMENT_REFUNDED', [seed.buyerId]);
  expect(recipients).toContain(seed.buyerId.toLowerCase());
});

test('§5.3 wrong token (USDC) → WRONG_TOKEN_REFUND, tx stays SELLER_CONFIRMED, buyer notified', async () => {
  const { txId, buyerToken } = await createAcceptConfirmReady();

  // A supported-but-wrong stablecoin (USDC) lands at the deposit address.
  const wrong = await api.payWrongTokenViaFake(txId, {
    actualTokenSymbol: 'USDC',
    amount: '10.00',
  });
  expect(wrong.ok, `wrong-token failed: ${JSON.stringify(wrong.body)}`).toBeTruthy();

  // 03 §5.3 step 4 — payment not accepted; the transaction stays SELLER_CONFIRMED.
  expect(statusOf((await api.getTransaction(buyerToken, txId)).body)).toBe('SELLER_CONFIRMED');

  // step 5 — the received token is refunded to the buyer's source wallet.
  const refund = await pollBlockchainTxConfirmed(txId, 'WRONG_TOKEN_REFUND');
  expect(refund, 'WRONG_TOKEN_REFUND not queued').not.toBeNull();
  expect(refund?.status).toBe('CONFIRMED');
  expect(Number(refund?.amount)).toBe(10);
  expect(refund?.toAddress).toBe(fakeBuyerWallet);

  // step 6 — buyer notified.
  const recipients = await pollNotificationRecipients('WRONG_TOKEN_REFUND', [seed.buyerId]);
  expect(recipients).toContain(seed.buyerId.toLowerCase());
});

test('§5.3a unsupported token → SPAM_TOKEN_INCOMING audit row, no refund, tx unchanged', async () => {
  const { txId, buyerToken } = await createAcceptConfirmReady();

  // An unsupported token/contract lands at the deposit address.
  const spam = await api.paySpamTokenViaFake(txId, { amount: '10.00' });
  expect(spam.ok, `spam-token failed: ${JSON.stringify(spam.body)}`).toBeTruthy();

  // 03 §5.3a steps 2–3 — recorded as a terminal CONFIRMED audit row; the
  // transaction state is NOT affected (unsupported assets are not payments).
  const audit = await pollBlockchainTxConfirmed(txId, 'SPAM_TOKEN_INCOMING');
  expect(audit, 'SPAM_TOKEN_INCOMING row not recorded').not.toBeNull();
  expect(audit?.status).toBe('CONFIRMED');

  // steps 3 & 5 — transaction stays SELLER_CONFIRMED.
  expect(statusOf((await api.getTransaction(buyerToken, txId)).body)).toBe('SELLER_CONFIRMED');

  // step 6 — automatic refund is NOT guaranteed for an unsupported asset; the
  // backend queues no refund row (admin review path — known limitation: the
  // §5.3a buyer notification + admin-review wiring are out of scope here).
  const noRefund = await pollBlockchainTxConfirmed(txId, 'WRONG_TOKEN_REFUND', {
    timeoutMs: 8_000,
  });
  expect(noRefund, 'unsupported token must not auto-refund').toBeNull();
});

test('§5.4 late payment after timeout → LATE_PAYMENT_REFUND, tx stays CANCELLED_TIMEOUT, buyer notified', async () => {
  // Setup mirrors the T109 payment-timeout scenario so the deposit address
  // enters POST_CANCEL_24H monitoring.
  const { txId, buyerToken } = await createAcceptConfirmReady();

  await backdateDeadline(txId, 'PaymentDeadline');
  const cancelled = await api.pollStatus(buyerToken, txId, 'CANCELLED_TIMEOUT', {
    timeoutMs: 90_000,
  });
  expect(cancelled).toBe('CANCELLED_TIMEOUT');

  const monitoring = await pollPostCancelMonitoring(txId);
  expect(monitoring?.status, `monitoring=${JSON.stringify(monitoring)}`).toBe('POST_CANCEL_24H');

  // 03 §5.4 step 3 — a late transfer lands at the still-monitored address.
  const late = await api.payLateViaFake(txId, { amount: '10.00' });
  expect(late.ok, `late-detected failed: ${JSON.stringify(late.body)}`).toBeTruthy();

  // step 4 — auto-refunded to the buyer's source wallet (gas deducted at broadcast).
  const refund = await pollBlockchainTxConfirmed(txId, 'LATE_PAYMENT_REFUND');
  expect(refund, 'LATE_PAYMENT_REFUND not queued').not.toBeNull();
  expect(refund?.status).toBe('CONFIRMED');
  expect(Number(refund?.amount)).toBe(10);
  expect(refund?.toAddress).toBe(fakeBuyerWallet);

  // The transaction stays in its terminal cancel state.
  expect(statusOf((await api.getTransaction(buyerToken, txId)).body)).toBe('CANCELLED_TIMEOUT');

  // step 5 — buyer notified (T110 wired the previously-missing consumer).
  const recipients = await pollNotificationRecipients('LATE_PAYMENT_REFUNDED', [seed.buyerId]);
  expect(recipients).toContain(seed.buyerId.toLowerCase());
});

test('§5.5 multi-payment → first exact accepted (proceeds), second refunded in full, buyer notified', async () => {
  const { txId, buyerToken } = await createAcceptConfirmReady();

  // 03 §5.5 scenario A — the first transfer is the exact expected amount: it is
  // accepted and the transaction advances to PAYMENT_RECEIVED.
  const first = await api.payViaFake(txId);
  expect(first.ok, `first pay failed: ${JSON.stringify(first.body)}`).toBeTruthy();
  expect(statusOf((await api.getTransaction(buyerToken, txId)).body)).toBe('PAYMENT_RECEIVED');

  // A second distinct on-chain transfer (eventIndex 1) arrives after the tx left
  // SELLER_CONFIRMED → treated as excess and refunded IN FULL (50, not a delta).
  // This is the branch AmountValidationService selects on state rather than on
  // amount, which is why the setup above must land exactly on SELLER_CONFIRMED.
  const second = await api.payViaFake(txId, { amount: '50.00', eventIndex: 1 });
  expect(second.ok, `second pay failed: ${JSON.stringify(second.body)}`).toBeTruthy();

  const refund = await pollBlockchainTxConfirmed(txId, 'EXCESS_REFUND');
  expect(refund, 'EXCESS_REFUND (multi-payment) not queued').not.toBeNull();
  expect(refund?.status).toBe('CONFIRMED');
  expect(Number(refund?.amount)).toBe(50);
  expect(refund?.toAddress).toBe(fakeBuyerWallet);

  // step 5 — buyer notified (multi-payment reuses the OVERPAYMENT_REFUNDED type).
  const recipients = await pollNotificationRecipients('OVERPAYMENT_REFUNDED', [seed.buyerId]);
  expect(recipients).toContain(seed.buyerId.toLowerCase());
});
