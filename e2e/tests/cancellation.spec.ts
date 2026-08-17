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
 * T108 cancellation scenarios — drives the cancel flows at the API level against
 * the docker-compose.e2e.yml stack (same seam as the T107 happy-path smoke). The
 * cancel path is fully wired backend-side (unlike the WP19 notification gap), so
 * this task adds test coverage only — no production source changes.
 *
 * Covers 03 §2.5 (seller cancel), §3.3 (buyer cancel) and §8.7 (admin cancel):
 *   1. Seller cancel before payment   → CANCELLED_SELLER, item returned, buyer notified.
 *   2. Buyer cancel before payment    → CANCELLED_BUYER,  item returned, seller notified.
 *   3. Admin cancel before payment    → CANCELLED_ADMIN,  item returned, both notified.
 *   4. Post-payment                   → user cancel rejected (PAYMENT_ALREADY_SENT, 422),
 *                                        admin cancel refunds the buyer + notifies both.
 *
 * T137a: the custody-era item-return assertions (RETURN_TO_SELLER trade offer +
 * bot escrow slot) were removed — T117 dropped both tables and P2P has no return
 * leg to observe. The flows themselves still drive through ITEM_ESCROWED, which
 * no longer exists; rewriting them is T138's scope.
 */

const CANCEL_REASON = 'E2E cancellation scenario — automated reason text.';

test.afterAll(async () => {
  await closePool();
});

/** Fresh seed → seller creates → buyer accepts → escrow dispatch (Hangfire) +
 *  fake trade self-drive → ITEM_ESCROWED. Returns the ids/tokens the cancel
 *  scenarios act on. Each test re-seeds, so cooldown stamps and prior rows never
 *  carry over. */
async function createAndEscrow(): Promise<{
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

  await api.pollStatus(buyerToken, txId, 'ITEM_ESCROWED', { timeoutMs: 180_000 });
  return { txId, sellerToken, buyerToken };
}

test('seller cancel before payment → CANCELLED_SELLER, item returned, buyer notified', async () => {
  const { txId, sellerToken } = await createAndEscrow();

  const cancel = await api.cancelTransaction(sellerToken, txId, CANCEL_REASON);
  expect(cancel.ok, `seller cancel failed: ${JSON.stringify(cancel.body)}`).toBeTruthy();
  const body = api.unwrap(cancel.body);
  expect(body.status).toBe('CANCELLED_SELLER');
  expect(body.itemReturned).toBe(true);
  expect(body.paymentRefunded).toBe(false);

  // 03 §2.5 step 9 — only the counter-party (buyer) is notified.
  const recipients = await pollCancelledNoticeRecipients([seed.buyerId]);
  expect(recipients).toContain(seed.buyerId.toLowerCase());
  expect(recipients).not.toContain(seed.sellerId.toLowerCase());
});

test('buyer cancel before payment → CANCELLED_BUYER, item returned, seller notified', async () => {
  const { txId, buyerToken } = await createAndEscrow();

  const cancel = await api.cancelTransaction(buyerToken, txId, CANCEL_REASON);
  expect(cancel.ok, `buyer cancel failed: ${JSON.stringify(cancel.body)}`).toBeTruthy();
  const body = api.unwrap(cancel.body);
  expect(body.status).toBe('CANCELLED_BUYER');
  expect(body.itemReturned).toBe(true);
  expect(body.paymentRefunded).toBe(false);

  // 03 §3.3 step 8 — only the counter-party (seller) is notified.
  const recipients = await pollCancelledNoticeRecipients([seed.sellerId]);
  expect(recipients).toContain(seed.sellerId.toLowerCase());
  expect(recipients).not.toContain(seed.buyerId.toLowerCase());
});

test('admin cancel before payment → CANCELLED_ADMIN, item returned, both parties notified', async () => {
  const { txId } = await createAndEscrow();
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
  expect(body.itemReturned).toBe(true);
  expect(body.paymentRefunded).toBe(false);

  // 03 §8.7 — neither party initiated the cancel, so BOTH are notified.
  const recipients = await pollCancelledNoticeRecipients([seed.sellerId, seed.buyerId]);
  expect(recipients).toContain(seed.sellerId.toLowerCase());
  expect(recipients).toContain(seed.buyerId.toLowerCase());
});

test('post-payment: user cancel blocked (422); admin cancel refunds buyer + notifies both', async () => {
  const { txId, sellerToken, buyerToken } = await createAndEscrow();

  // Buyer pays (fake control endpoint: detect → confirm exact amount).
  const pay = await api.payViaFake(txId);
  expect(pay.ok, `pay failed: ${JSON.stringify(pay.body)}`).toBeTruthy();

  // Catch a post-payment, pre-delivery state (PAYMENT_RECEIVED or, briefly,
  // TRADE_OFFER_SENT_TO_BUYER). Both reject a user cancel and still let an admin
  // cancel refund the buyer; this races the per-minute delivery-dispatch job
  // safely by accepting either.
  await api.pollUntilRefundableCancel(buyerToken, txId, { timeoutMs: 90_000 });

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
