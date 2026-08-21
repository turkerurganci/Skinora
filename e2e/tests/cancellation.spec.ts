import { test, expect } from '@playwright/test';
import {
  seedHappyPath,
  ensureAdmin,
  pollCancelledNoticeRecipients,
  pollBuyerRefundConfirmed,
  closePool,
  seed,
} from '../src/db';
import { mintAccessToken } from '../src/jwt';
import * as api from '../src/api';

/**
 * T108 cancellation scenarios, rewritten for P2P (T138) — drives the cancel
 * flows at the API level against the docker-compose.e2e.yml stack (same seam as
 * the T107 happy-path smoke).
 *
 * Covers 03 §2.5 (seller cancel), §3.3 (buyer cancel) and §8.7 (admin cancel):
 *   1. Seller cancel before payment   → CANCELLED_SELLER, buyer notified.
 *   2. Buyer cancel before payment    → CANCELLED_BUYER,  seller notified.
 *   3. Admin cancel before payment    → CANCELLED_ADMIN,  both notified.
 *   4. Post-payment                   → user cancel rejected (PAYMENT_ALREADY_SENT, 422),
 *                                        admin cancel refunds the buyer + notifies both.
 *
 * Two things changed with the P2P pivot, and both are visible here:
 *
 *   The "before payment" state is now SELLER_CONFIRMED, not ITEM_ESCROWED. It is
 *   the same moment in the story — everything is arranged and the buyer's money
 *   has not moved — but the platform holds nothing at it. Reaching it takes the
 *   seller's confirm-ready (07 §7.6a), which is also what opened the payment
 *   window these scenarios then cancel out of.
 *
 *   `itemReturned` is GONE from both cancel responses (v3.0). The old assertion
 *   `itemReturned === true` was the custody model's core claim — the platform
 *   held the skin and gave it back. In P2P the item never left the seller before
 *   PAYMENT_RECEIVED, so there is nothing to return and nothing to report;
 *   asserting the field's absence would be asserting a DTO shape, so what these
 *   tests assert instead is the substantive half: no refund is queued, because
 *   no money moved either.
 */

const CANCEL_REASON = 'E2E cancellation scenario — automated reason text.';

test.beforeEach(async () => {
  await api.resetFakeSteamState();
});

test.afterAll(async () => {
  await api.resetFakeSteamState();
  await closePool();
});

/** Fresh seed → seller creates → buyer accepts → seller confirms readiness →
 *  SELLER_CONFIRMED. The deepest state a transaction can sit in with the buyer's
 *  money still untouched, and therefore the one every "before payment" cancel
 *  scenario acts on. Each test re-seeds, so cooldown stamps and prior rows never
 *  carry over. */
async function createAndConfirmReady(): Promise<{
  txId: string;
  sellerToken: string;
  buyerToken: string;
}> {
  await seedHappyPath();
  const sellerToken = mintAccessToken({ userId: seed.sellerId, steamId: seed.sellerSteamId });
  const buyerToken = mintAccessToken({ userId: seed.buyerId, steamId: seed.buyerSteamId });

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
  const txId = String(created.id);

  const accept = await api.acceptTransaction(buyerToken, txId, seed.buyerRefundAddress);
  expect(accept.ok, `accept failed: ${JSON.stringify(accept.body)}`).toBeTruthy();
  expect(api.unwrap(accept.body).status).toBe('ACCEPTED');

  const ready = await api.confirmReady(sellerToken, txId);
  expect(ready.ok, `confirm-ready failed: ${JSON.stringify(ready.body)}`).toBeTruthy();
  expect(api.unwrap(ready.body).status).toBe('SELLER_CONFIRMED');

  return { txId, sellerToken, buyerToken };
}

/** No BUYER_REFUND row was queued. Short timeout on purpose: this asserts an
 *  absence, and the queue+dispatch cadence is per-minute, so the point is that
 *  nothing is even staged — not that nothing arrived within three minutes. */
async function expectNoRefundQueued(txId: string): Promise<void> {
  const refund = await pollBuyerRefundConfirmed(txId, { timeoutMs: 8_000 });
  expect(
    refund,
    `a BUYER_REFUND was queued for an unpaid transaction: ${JSON.stringify(refund)}`,
  ).toBeNull();
}

test('seller cancel before payment → CANCELLED_SELLER, nothing refunded, buyer notified', async () => {
  const { txId, sellerToken } = await createAndConfirmReady();

  const cancel = await api.cancelTransaction(sellerToken, txId, CANCEL_REASON);
  expect(cancel.ok, `seller cancel failed: ${JSON.stringify(cancel.body)}`).toBeTruthy();
  const body = api.unwrap(cancel.body);
  expect(body.status).toBe('CANCELLED_SELLER');
  expect(body.paymentRefunded).toBe(false);
  await expectNoRefundQueued(txId);

  // 03 §2.5 step 9 — only the counter-party (buyer) is notified.
  const recipients = await pollCancelledNoticeRecipients([seed.buyerId]);
  expect(recipients).toContain(seed.buyerId.toLowerCase());
  expect(recipients).not.toContain(seed.sellerId.toLowerCase());
});

test('buyer cancel before payment → CANCELLED_BUYER, nothing refunded, seller notified', async () => {
  const { txId, buyerToken } = await createAndConfirmReady();

  const cancel = await api.cancelTransaction(buyerToken, txId, CANCEL_REASON);
  expect(cancel.ok, `buyer cancel failed: ${JSON.stringify(cancel.body)}`).toBeTruthy();
  const body = api.unwrap(cancel.body);
  expect(body.status).toBe('CANCELLED_BUYER');
  expect(body.paymentRefunded).toBe(false);
  await expectNoRefundQueued(txId);

  // 03 §3.3 step 8 — only the counter-party (seller) is notified.
  const recipients = await pollCancelledNoticeRecipients([seed.sellerId]);
  expect(recipients).toContain(seed.sellerId.toLowerCase());
  expect(recipients).not.toContain(seed.buyerId.toLowerCase());
});

test('admin cancel before payment → CANCELLED_ADMIN, nothing refunded, both parties notified', async () => {
  const { txId } = await createAndConfirmReady();
  await ensureAdmin();
  const adminToken = mintAccessToken({
    userId: seed.adminId,
    steamId: seed.adminSteamId,
    role: 'super_admin',
  });

  const cancel = await api.adminCancelTransaction(adminToken, txId, CANCEL_REASON);
  expect(cancel.ok, `admin cancel failed: ${JSON.stringify(cancel.body)}`).toBeTruthy();
  const body = api.unwrap(cancel.body);
  expect(body.status).toBe('CANCELLED_ADMIN');
  expect(body.paymentRefunded).toBe(false);
  await expectNoRefundQueued(txId);

  // 03 §8.7 — neither party initiated the cancel, so BOTH are notified.
  const recipients = await pollCancelledNoticeRecipients([seed.sellerId, seed.buyerId]);
  expect(recipients).toContain(seed.sellerId.toLowerCase());
  expect(recipients).toContain(seed.buyerId.toLowerCase());
});

test('post-payment: user cancel blocked (422); admin cancel refunds buyer + notifies both', async () => {
  const { txId, sellerToken, buyerToken } = await createAndConfirmReady();

  // Buyer pays (fake control endpoint: detect → confirm exact amount).
  const pay = await api.payViaFake(txId);
  expect(pay.ok, `pay failed: ${JSON.stringify(pay.body)}`).toBeTruthy();

  // PAYMENT_RECEIVED is a resting state in P2P: nothing advances it but the
  // buyer's confirmation, an admin, or the delivery deadline. The custody-era
  // helper that hedged against a per-minute delivery-dispatch job racing this
  // poll (pollUntilRefundableCancel) went with the job it was hedging against.
  await api.pollStatus(buyerToken, txId, 'PAYMENT_RECEIVED', { timeoutMs: 90_000 });

  // 03 §2.5 / §3.3 — once payment is in, NEITHER party may cancel. The cancel
  // service short-circuits every post-payment state to PAYMENT_ALREADY_SENT,
  // which the controller maps uniquely to 422 (other failures use 400/403/404/409).
  const sellerBlocked = await api.cancelTransaction(sellerToken, txId, CANCEL_REASON);
  expect(sellerBlocked.status, `seller cancel body: ${JSON.stringify(sellerBlocked.body)}`).toBe(
    422,
  );
  const buyerBlocked = await api.cancelTransaction(buyerToken, txId, CANCEL_REASON);
  expect(buyerBlocked.status, `buyer cancel body: ${JSON.stringify(buyerBlocked.body)}`).toBe(422);

  // 03 §8.7 — admin CAN cancel after payment (until ITEM_DELIVERED). This is the
  // unique admin capability: the buyer's payment is refunded.
  await ensureAdmin();
  const adminToken = mintAccessToken({
    userId: seed.adminId,
    steamId: seed.adminSteamId,
    role: 'super_admin',
  });
  const cancel = await api.adminCancelTransaction(adminToken, txId, CANCEL_REASON);
  expect(cancel.ok, `admin cancel failed: ${JSON.stringify(cancel.body)}`).toBeTruthy();
  const body = api.unwrap(cancel.body);
  expect(body.status).toBe('CANCELLED_ADMIN');
  expect(body.paymentRefunded).toBe(true);

  // Payment refund: a BUYER_REFUND blockchain transfer is queued + confirmed
  // (02 §4.6 net = TotalAmount − gas fee), addressed to the buyer's refund wallet.
  const refund = await pollBuyerRefundConfirmed(txId);
  expect(refund, 'BUYER_REFUND row never queued').not.toBeNull();
  expect(refund?.status).toBe('CONFIRMED');
  expect(refund?.toAddress).toBe(seed.buyerRefundAddress);

  // Admin cancel notifies BOTH parties (03 §8.7).
  const recipients = await pollCancelledNoticeRecipients([seed.sellerId, seed.buyerId]);
  expect(recipients).toContain(seed.sellerId.toLowerCase());
  expect(recipients).toContain(seed.buyerId.toLowerCase());
});
