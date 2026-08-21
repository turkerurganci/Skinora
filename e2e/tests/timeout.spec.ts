import { test, expect } from '@playwright/test';
import {
  seedHappyPath,
  backdateDeadline,
  pollPostCancelMonitoring,
  pollCancelledNoticeRecipients,
  pollBuyerRefundConfirmed,
  pollDisputeForTransaction,
  getSettlementState,
  closePool,
  seed,
} from '../src/db';
import { mintAccessToken } from '../src/jwt';
import * as api from '../src/api';

/**
 * T109 timeout scenarios, rewritten for P2P (T138) — drives the four phase
 * timeouts of 03 §4 at the API level against the docker-compose.e2e.yml stack.
 * The timeout path itself stays unmocked: the harness back-dates the real phase
 * deadline (all e2e timeouts are 60 minutes, so a wall-clock wait is impossible)
 * and lets the production DeadlineScannerJob sweep — 5 s in the e2e stack.
 *
 * Covers 03 §4.1–§4.4, whose four phases the P2P pivot renamed and re-pointed
 * without changing their count:
 *   1. Accept timeout      (CREATED)          → CANCELLED_TIMEOUT, no refund.
 *   2. Seller-confirm      (ACCEPTED)         → CANCELLED_TIMEOUT, no money moved.
 *   3. Payment timeout     (SELLER_CONFIRMED) → CANCELLED_TIMEOUT + late-payment monitor.
 *   4. Delivery timeout    (PAYMENT_RECEIVED) → seller-fault cancel + buyer refund.
 * Every phase notifies both parties (TRANSACTION_CANCELLED). The §4.5 "deadline
 * approaching" warning is out of scope (covered by unit/integration tests).
 *
 * Phase 4 is where P2P changed the MECHANISM, not just the state name. In the
 * custody model the delivery deadline expiring WAS the decision: the bot still
 * held the item, so the platform cancelled and handed both sides back what they
 * had put in. In P2P the platform is not a party to the trade (02 §2.1), so an
 * expired deadline decides nothing on its own — 05 §4.4 requires a verification
 * round first, and its verdict picks one of three actions. This test drives the
 * one arm that is authorised to cancel: the seller's inventory was READ and the
 * item is still in it, which is the single positive proof that nothing was sent
 * (03 §4.4). The other two arms — evidence says it arrived, and evidence says it
 * went somewhere else — live in delivery.spec.ts, because neither of them
 * cancels and a timeout suite that owned them would be lying about its subject.
 */

test.beforeEach(async () => {
  // Clear any inventory / trade-hold a previous test drove into the shared
  // (in-process) fake state, so this test starts from an empty, readable world.
  // seedHappyPath() runs AFTER this and re-drives the seller's inventory.
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

/** Seller creates a CREATED transaction against the seeded buyer. */
async function createTransaction(sellerToken: string): Promise<string> {
  const create = await api.createTransaction(sellerToken, {
    itemAssetId: seed.itemAssetId,
    stablecoin: 'USDT',
    price: seed.price,
    paymentTimeoutHours: 1,
    buyerIdentificationMethod: 'STEAM_ID',
    buyerSteamId: seed.buyerSteamId,
    sellerWalletAddress: seed.sellerPayoutAddress,
  });
  expect(create.ok, `create failed: ${JSON.stringify(create.body)}`).toBeTruthy();
  const created = api.unwrap(create.body);
  expect(created.status).toBe('CREATED');
  return String(created.id);
}

test('accept timeout: CREATED → CANCELLED_TIMEOUT, no refund, both parties notified', async () => {
  await seedHappyPath();
  const { sellerToken, buyerToken } = tokens();
  const txId = await createTransaction(sellerToken);

  // 03 §4.1 — the buyer never accepts. Backdate the accept deadline; the scanner
  // cancels the still-CREATED transaction on its next sweep.
  await backdateDeadline(txId, 'AcceptDeadline');
  const status = await api.pollStatus(buyerToken, txId, 'CANCELLED_TIMEOUT', { timeoutMs: 90_000 });
  expect(status).toBe('CANCELLED_TIMEOUT');

  // 03 §4.1 steps 4–5 — seller notified; buyer notified because registered.
  const recipients = await pollCancelledNoticeRecipients([seed.sellerId, seed.buyerId]);
  expect(recipients).toContain(seed.sellerId.toLowerCase());
  expect(recipients).toContain(seed.buyerId.toLowerCase());
});

test('seller-confirm timeout: ACCEPTED → CANCELLED_TIMEOUT, no money moved, both notified', async () => {
  await seedHappyPath();
  const { sellerToken, buyerToken } = tokens();
  const txId = await createTransaction(sellerToken);

  const accept = await api.acceptTransaction(buyerToken, txId, seed.buyerRefundAddress);
  expect(accept.ok, `accept failed: ${JSON.stringify(accept.body)}`).toBeTruthy();
  expect(api.unwrap(accept.body).status).toBe('ACCEPTED');

  // 03 §4.2 — the P2P replacement for the retired seller trade-offer phase. The
  // transaction parks in ACCEPTED simply because the seller never presses
  // confirm-ready: there is no offer to hold open and no fake lever needed, so
  // "the seller did not act" is modelled by NOT acting. SellerConfirmDeadline is
  // armed by the accept itself (TransactionAcceptanceService), which is what
  // makes the plain wait observable at all.
  await backdateDeadline(txId, 'SellerConfirmDeadline');
  const status = await api.pollStatus(buyerToken, txId, 'CANCELLED_TIMEOUT', { timeoutMs: 90_000 });
  expect(status).toBe('CANCELLED_TIMEOUT');

  // Nothing to give back on either side: the buyer's payment window never opened
  // (the deposit address is revealed at SELLER_CONFIRMED) and the item never left
  // the seller.
  const refund = await pollBuyerRefundConfirmed(txId, { timeoutMs: 8_000 });
  expect(
    refund,
    `unexpected refund for an unpaid transaction: ${JSON.stringify(refund)}`,
  ).toBeNull();

  const recipients = await pollCancelledNoticeRecipients([seed.sellerId, seed.buyerId]);
  expect(recipients).toContain(seed.sellerId.toLowerCase());
  expect(recipients).toContain(seed.buyerId.toLowerCase());
});

test('payment timeout: SELLER_CONFIRMED → CANCELLED_TIMEOUT, late-payment monitor started, both notified', async () => {
  await seedHappyPath();
  const { sellerToken, buyerToken } = tokens();
  const txId = await createTransaction(sellerToken);

  const accept = await api.acceptTransaction(buyerToken, txId, seed.buyerRefundAddress);
  expect(accept.ok, `accept failed: ${JSON.stringify(accept.body)}`).toBeTruthy();

  // The payment window opens here, and only here — the buyer cannot pay before
  // the seller has confirmed the item is still sendable (02 §2.2 step 3).
  const ready = await api.confirmReady(sellerToken, txId);
  expect(ready.ok, `confirm-ready failed: ${JSON.stringify(ready.body)}`).toBeTruthy();
  expect(api.unwrap(ready.body).status).toBe('SELLER_CONFIRMED');

  // 03 §4.3 — the buyer never pays. Backdate the payment deadline; the scanner
  // (belt-and-suspenders for the per-tx Hangfire job) cancels.
  await backdateDeadline(txId, 'PaymentDeadline');
  const status = await api.pollStatus(buyerToken, txId, 'CANCELLED_TIMEOUT', { timeoutMs: 90_000 });
  expect(status).toBe('CANCELLED_TIMEOUT');

  // 03 §4.3 step 4 / 08 §3.4 — the platform keeps watching the deposit address
  // for a late payment: the PaymentAddress flips to a POST_CANCEL_* window.
  const monitoring = await pollPostCancelMonitoring(txId);
  expect(monitoring?.status, `monitoring=${JSON.stringify(monitoring)}`).toBe('POST_CANCEL_24H');
  expect(monitoring?.expiresAt, 'POST_CANCEL window has no expiry').not.toBeNull();

  // 03 §4.3 steps 5–6 — both parties notified.
  const recipients = await pollCancelledNoticeRecipients([seed.sellerId, seed.buyerId]);
  expect(recipients).toContain(seed.sellerId.toLowerCase());
  expect(recipients).toContain(seed.buyerId.toLowerCase());
});

test('delivery timeout with the item still at the seller: seller-fault cancel + buyer refund, both notified', async () => {
  await seedHappyPath();
  const { sellerToken, buyerToken } = tokens();
  const txId = await createTransaction(sellerToken);

  const accept = await api.acceptTransaction(buyerToken, txId, seed.buyerRefundAddress);
  expect(accept.ok, `accept failed: ${JSON.stringify(accept.body)}`).toBeTruthy();
  const ready = await api.confirmReady(sellerToken, txId);
  expect(ready.ok, `confirm-ready failed: ${JSON.stringify(ready.body)}`).toBeTruthy();

  const pay = await api.payViaFake(txId);
  expect(pay.ok, `pay failed: ${JSON.stringify(pay.body)}`).toBeTruthy();
  await api.pollStatus(buyerToken, txId, 'PAYMENT_RECEIVED', { timeoutMs: 90_000 });

  // 03 §4.4 — the seller never sends. Modelled by NOT calling
  // api.simulateFakeTrade: the item stays in the seller's inventory exactly as
  // seedHappyPath left it, which is both the scenario AND the evidence. The
  // buyer's baseline is zero and stays zero.
  await backdateDeadline(txId, 'DeliveryDeadline');
  const status = await api.pollStatus(buyerToken, txId, 'CANCELLED_TIMEOUT', {
    timeoutMs: 120_000,
  });
  expect(status).toBe('CANCELLED_TIMEOUT');

  const rounded = await getSettlementState(txId);
  expect(rounded?.deliveryRoundAt, 'no delivery verification round ever ran').not.toBeNull();
  expect(rounded?.deliveryVerifiedAt, 'delivery must not be recorded on a cancel').toBeNull();

  // 05 §4.4 — the cancel came from a verification ROUND, not from the clock.
  // DeliveryRoundAt is stamped by the round before any arm runs (T127 finding
  // B2: the scanner's fairness window asks when a row was last LOOKED at, not
  // when it was last concluded about), so its presence is the proof that an
  // inventory read happened at all. There is deliberately NO
  // DeliveryEvidenceCaptures row to check here: captures are written only where
  // a reviewer has something to weigh — a gated delivery or a misdelivery
  // signature — and "nothing moved, the seller still has it" is neither.

  // 02 §9.2 — a cancel and an escalation are mutually exclusive answers. This
  // arm cancels precisely BECAUSE the item was proven to be still with the
  // seller, so no dispute may have been opened.
  const dispute = await pollDisputeForTransaction(txId, { timeoutMs: 8_000 });
  expect(
    dispute,
    `a dispute was opened for a proven non-delivery: ${JSON.stringify(dispute)}`,
  ).toBeNull();

  // 03 §4.4 step 4 — payment returned to the buyer (02 §4.6 net = TotalAmount −
  // gas fee), addressed to the buyer's trade-side refund wallet.
  const refund = await pollBuyerRefundConfirmed(txId);
  expect(refund, 'BUYER_REFUND row never queued').not.toBeNull();
  expect(refund?.status).toBe('CONFIRMED');
  expect(refund?.toAddress).toBe(seed.buyerRefundAddress);

  // 03 §4.4 steps 5–6 — both parties notified.
  const recipients = await pollCancelledNoticeRecipients([seed.sellerId, seed.buyerId]);
  expect(recipients).toContain(seed.sellerId.toLowerCase());
  expect(recipients).toContain(seed.buyerId.toLowerCase());
});
