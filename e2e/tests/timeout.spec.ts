import { test, expect } from '@playwright/test';
import {
  seedHappyPath,
  backdateDeadline,
  pollPostCancelMonitoring,
  pollCancelledNoticeRecipients,
  pollBuyerRefundConfirmed,
  closePool,
  seed,
} from '../src/db';
import { mintAccessToken } from '../src/jwt';
import * as api from '../src/api';

/**
 * T109 timeout scenarios — drives the four phase timeouts of 03 §4 at the API
 * level against the docker-compose.e2e.yml stack (same seam as the T107/T108
 * smokes). The whole timeout path is wired backend-side (DeadlineScannerJob —
 * 05 §4.4), so this task adds test coverage plus two e2e-only levers:
 *   • the harness backdates the real phase deadline (all e2e timeouts are 60 min,
 *     so a wall-clock wait is impossible) and lets the production scanner fire;
 *   • the fake's trade auto-accept is suppressed per direction so a transaction
 *     parks in TRADE_OFFER_SENT_TO_SELLER / _BUYER long enough to time out.
 * No production source changes.
 *
 * Covers 03 §4.1–§4.4:
 *   1. Accept timeout (CREATED)                → CANCELLED_TIMEOUT, no refund.
 *   2. Seller trade-offer timeout (TO_SELLER)  → CANCELLED_TIMEOUT, no escrow.
 *   3. Payment timeout (ITEM_ESCROWED)         → item returned + late-payment monitor.
 *   4. Delivery timeout (TO_BUYER)             → item returned + buyer refund.
 * Every phase notifies both parties (TRANSACTION_CANCELLED). The §4.5 "deadline
 * approaching" warning is out of scope (covered by unit/integration tests).
 *
 * T137a: the custody-era assertions (bot escrow slot, RETURN_TO_SELLER offer)
 * were removed — T117 dropped both tables and P2P has no platform inventory.
 * Phases 2–4 above are custody phases that no longer exist; rewriting them for
 * the P2P timeline is T138's scope.
 */

test.beforeEach(async () => {
  // Clear any inventory / trade-hold a previous test drove into the shared
  // (in-process) fake state, so this test starts from an empty, readable world.
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

test('seller trade-offer timeout: TRADE_OFFER_SENT_TO_SELLER → CANCELLED_TIMEOUT, no escrow, both notified', async () => {
  await seedHappyPath();
  // T137 — the lever this scenario used (hold the escrow leg at "sent" so the
  // seller never accepts the bot's offer) went with the custody trade surface:
  // the platform sends no trade offers any more (02 §2.1), so TRADE_OFFER_SENT_
  // TO_SELLER is not a state the flow can reach. The P2P replacement is the
  // seller confirm-ready deadline (03 §2.3), and writing it is T138's scope.
  const { sellerToken, buyerToken } = tokens();
  const txId = await createTransaction(sellerToken);

  const accept = await api.acceptTransaction(buyerToken, txId, seed.buyerRefundAddress);
  expect(accept.ok, `accept failed: ${JSON.stringify(accept.body)}`).toBeTruthy();
  expect(api.unwrap(accept.body).status).toBe('ACCEPTED');

  // Escrow dispatch sends SELLER_TO_BOT; suppression parks it here (no accept).
  await api.pollStatus(buyerToken, txId, 'TRADE_OFFER_SENT_TO_SELLER', { timeoutMs: 120_000 });

  // 03 §4.2 — backdate the seller-offer deadline; scanner cancels.
  await backdateDeadline(txId, 'SellerConfirmDeadline');
  const status = await api.pollStatus(buyerToken, txId, 'CANCELLED_TIMEOUT', { timeoutMs: 90_000 });
  expect(status).toBe('CANCELLED_TIMEOUT');

  const recipients = await pollCancelledNoticeRecipients([seed.sellerId, seed.buyerId]);
  expect(recipients).toContain(seed.sellerId.toLowerCase());
  expect(recipients).toContain(seed.buyerId.toLowerCase());
});

test('payment timeout: ITEM_ESCROWED → CANCELLED_TIMEOUT, item returned to seller, late-payment monitor started, both notified', async () => {
  await seedHappyPath();
  const { sellerToken, buyerToken } = tokens();
  const txId = await createTransaction(sellerToken);

  const accept = await api.acceptTransaction(buyerToken, txId, seed.buyerRefundAddress);
  expect(accept.ok, `accept failed: ${JSON.stringify(accept.body)}`).toBeTruthy();

  // Escrow leg auto-drives (not suppressed) → ITEM_ESCROWED. The buyer never pays.
  await api.pollStatus(buyerToken, txId, 'ITEM_ESCROWED', { timeoutMs: 180_000 });

  // 03 §4.3 — backdate the payment deadline; the scanner (belt-and-suspenders for
  // the per-tx Hangfire job) cancels the ITEM_ESCROWED transaction.
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

test('delivery timeout: TRADE_OFFER_SENT_TO_BUYER → CANCELLED_TIMEOUT, item to seller + payment to buyer, both notified', async () => {
  await seedHappyPath();
  // T137 — same retirement as above: there is no bot delivery leg to hold. The
  // P2P replacement is "the seller never trades the item to the buyer", driven
  // by simply NOT calling api.simulateFakeTrade before the delivery deadline
  // (03 §6.4); wiring that into this scenario is T138's scope.
  const { sellerToken, buyerToken } = tokens();
  const txId = await createTransaction(sellerToken);

  const accept = await api.acceptTransaction(buyerToken, txId, seed.buyerRefundAddress);
  expect(accept.ok, `accept failed: ${JSON.stringify(accept.body)}`).toBeTruthy();

  await api.pollStatus(buyerToken, txId, 'ITEM_ESCROWED', { timeoutMs: 180_000 });

  // Buyer pays → PAYMENT_RECEIVED; delivery dispatch sends BOT_TO_BUYER, which
  // suppression parks at TRADE_OFFER_SENT_TO_BUYER (buyer never accepts).
  const pay = await api.payViaFake(txId);
  expect(pay.ok, `pay failed: ${JSON.stringify(pay.body)}`).toBeTruthy();
  await api.pollStatus(buyerToken, txId, 'TRADE_OFFER_SENT_TO_BUYER', { timeoutMs: 120_000 });

  // 03 §4.4 — backdate the buyer-delivery deadline; scanner cancels.
  await backdateDeadline(txId, 'DeliveryDeadline');
  const status = await api.pollStatus(buyerToken, txId, 'CANCELLED_TIMEOUT', { timeoutMs: 90_000 });
  expect(status).toBe('CANCELLED_TIMEOUT');

  // 03 §4.4 step 4 — payment returned to the buyer (02 §4.6 net = TotalAmount −
  // gas fee), addressed to the buyer's refund wallet.
  const refund = await pollBuyerRefundConfirmed(txId);
  expect(refund, 'BUYER_REFUND row never queued').not.toBeNull();
  expect(refund?.status).toBe('CONFIRMED');
  expect(refund?.toAddress).toBe(seed.buyerRefundAddress);

  // 03 §4.4 steps 5–6 — both parties notified.
  const recipients = await pollCancelledNoticeRecipients([seed.sellerId, seed.buyerId]);
  expect(recipients).toContain(seed.sellerId.toLowerCase());
  expect(recipients).toContain(seed.buyerId.toLowerCase());
});
