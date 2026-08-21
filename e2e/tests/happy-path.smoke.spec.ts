import { test, expect } from '@playwright/test';
import {
  seedHappyPath,
  pollNotificationTypes,
  getSettlementState,
  setPayoutEligibleNow,
  pollSettlementVerified,
  closePool,
  seed,
} from '../src/db';
import { mintAccessToken } from '../src/jwt';
import * as api from '../src/api';

/**
 * T107 happy-path smoke, rewritten for P2P (T138) — drives the whole 02 §2.2
 * chain at the API level against the docker-compose.e2e.yml stack. The fake
 * sidecar simulates the two things the platform cannot observe by itself: the
 * buyer's on-chain payment (which the real monitor would see) and the
 * seller → buyer Steam trade, which the platform is not a party to AT ALL
 * (02 §2.1) and therefore never learns about from Steam.
 *
 * What changed from the custody-era version, and why each step exists:
 *
 *   ACCEPTED → SELLER_CONFIRMED is a NEW step with no predecessor. The platform
 *   used to pull the item into escrow here; now `confirm-ready` (07 §7.6a) only
 *   verifies that the seller CAN still send — and that verification is what
 *   licenses revealing the deposit address to the buyer (02 §2.2 step 3). It
 *   also captures the buyer's inventory baseline, which every later delivery and
 *   settlement judgement is measured against (06 §3.5: a NULL baseline is not a
 *   zero baseline).
 *
 *   PAYMENT_RECEIVED → ITEM_DELIVERED runs on the buyer's own confirmation, not
 *   on a bot's trade-offer callback. 02 §9.2 has a second route (inventory
 *   evidence: the seller's asset gone AND the buyer's class count risen), but it
 *   is held shut at launch by `delivery.inventory_evidence_auto_release_enabled`
 *   (seed default false, DEPLOY_RUNBOOK §H) — so buyer confirmation is the only
 *   route that advances on its own, exactly as DEPLOY_RUNBOOK §G.4 control 10
 *   says of the production rehearsal.
 *
 *   ITEM_DELIVERED → COMPLETED is no longer immediate. 02 §4.5.1 holds the
 *   payout for the settlement window so a reversed trade cannot be paid out;
 *   `payout_settlement_days` floors at 7 days, so the window is brought forward
 *   with the runbook's own control-10a shortcut (setPayoutEligibleNow). The
 *   shortcut moves the CLOCK only — `settlement-verification` still re-reads the
 *   buyer's inventory and only stamps SettlementVerifiedAt because the item is
 *   genuinely still there.
 */

// WP19 inbox notifications across the P2P happy path, one per producer. Two of
// the custody-era types went with their producers (06 §2.13 v3.0): ITEM_ESCROWED
// became PAYMENT_WINDOW_OPEN (nothing is escrowed at that point except, shortly,
// the money) and TRADE_OFFER_SENT_TO_BUYER became DELIVERY_EXPECTED, which also
// flipped RECIPIENT — it used to tell the buyer to accept the platform's offer
// and now tells the seller to send the item. COMPLETED still fans out to BOTH
// parties; ITEM_DELIVERED is realtime-only and must NOT appear in an inbox.
const EXPECTED_NOTIFICATIONS = [
  'TRANSACTION_INVITE',
  'BUYER_ACCEPTED',
  'PAYMENT_WINDOW_OPEN',
  'PAYMENT_RECEIVED',
  'DELIVERY_EXPECTED',
  'SELLER_PAYMENT_SENT',
  'TRANSACTION_COMPLETED',
];

test.beforeEach(async () => {
  await api.resetFakeSteamState();
});

test.afterAll(async () => {
  await api.resetFakeSteamState();
  await closePool();
});

test('happy path: CREATED → COMPLETED through the P2P chain, with WP19 notifications', async () => {
  await seedHappyPath();
  const sellerToken = mintAccessToken({ userId: seed.sellerId, steamId: seed.sellerSteamId });
  const buyerToken = mintAccessToken({ userId: seed.buyerId, steamId: seed.buyerSteamId });

  // 1. Seller creates the transaction (status CREATED, not FLAGGED). Stage 5 of
  //    create reads the seller's Steam inventory, which seedHappyPath drove.
  const create = await api.createTransaction(sellerToken, {
    itemAssetId: seed.itemAssetId,
    stablecoin: 'USDT',
    price: seed.price,
    // 1h = 60 min, within the e2e PAYMENT_TIMEOUT_MIN/MAX (15/60). The smoke
    // pays immediately, so the exact deadline is irrelevant.
    paymentTimeoutHours: 1,
    buyerIdentificationMethod: 'STEAM_ID',
    buyerSteamId: seed.buyerSteamId,
    sellerWalletAddress: seed.sellerPayoutAddress,
  });
  expect(create.ok, `create failed: ${JSON.stringify(create.body)}`).toBeTruthy();
  const created = api.unwrap(create.body);
  expect(created.status).toBe('CREATED');
  const txId = String(created.id);

  // 2. Buyer accepts → ACCEPTED. The trade URL fixed here is the address the
  //    seller will actually send to, and the one the MA probe answers for.
  const accept = await api.acceptTransaction(buyerToken, txId, seed.buyerRefundAddress);
  expect(accept.ok, `accept failed: ${JSON.stringify(accept.body)}`).toBeTruthy();
  expect(api.unwrap(accept.body).status).toBe('ACCEPTED');

  // 3. Seller confirms readiness → SELLER_CONFIRMED (07 §7.6a). Synchronous:
  //    three Steam reads and the transition commit in one request.
  const ready = await api.confirmReady(sellerToken, txId);
  expect(ready.ok, `confirm-ready failed: ${JSON.stringify(ready.body)}`).toBeTruthy();
  const readyBody = api.unwrap(ready.body);
  expect(readyBody.status).toBe('SELLER_CONFIRMED');
  // The payment window is armed by this call, not by the buyer's accept.
  expect(readyBody.paymentDeadline, 'paymentDeadline not armed').toBeTruthy();
  // 03 §2.3 step 3 md.3 — the buyer's inventory was readable, so the delivery
  // baseline exists. This is the precondition of BOTH later inventory
  // judgements; the settlement check answers NoDeliveryReference without it.
  expect(readyBody.buyerInventoryVisible, 'buyer inventory baseline not captured').toBe(true);

  const baselined = await getSettlementState(txId);
  expect(baselined?.buyerBaselineCapturedAt, 'BuyerBaselineCapturedAt not stamped').not.toBeNull();
  // The buyer starts with ZERO copies of this class — that zero is the reference
  // the delivery delta and the settlement re-read are both measured against.
  expect(baselined?.buyerBaselineClassCount).toBe(0);

  // 4. Buyer pays (fake control endpoint: detect → confirm) → PAYMENT_RECEIVED.
  //    AmountValidationService accepts a payment ONLY in SELLER_CONFIRMED, so
  //    step 3 is a hard precondition of this one, not a formality.
  const pay = await api.payViaFake(txId);
  expect(pay.ok, `pay failed: ${JSON.stringify(pay.body)}`).toBeTruthy();
  await api.pollStatus(buyerToken, txId, 'PAYMENT_RECEIVED', { timeoutMs: 90_000 });

  // 5. The seller sends the item DIRECTLY to the buyer (02 §2.2 step 6). No
  //    platform endpoint is involved — this is a Steam trade between two people,
  //    simulated on the fake. The asset lands under a NEW id (06 §8.4 rotation),
  //    which is why the buyer side is judged by CLASS COUNT, never by asset id.
  const trade = await api.simulateFakeTrade(
    seed.sellerSteamId,
    seed.buyerSteamId,
    seed.itemAssetId,
  );
  expect(trade.ok, `trade simulation failed: ${JSON.stringify(trade.body)}`).toBeTruthy();

  // 6. Buyer confirms receipt → ITEM_DELIVERED (07 §7.6b). Sufficient on its own
  //    and not subject to the launch gate: the confirmation releases the buyer's
  //    own money, so there is no incentive to claim it falsely (02 §9.2).
  const receipt = await api.confirmReceipt(buyerToken, txId);
  expect(receipt.ok, `confirm-receipt failed: ${JSON.stringify(receipt.body)}`).toBeTruthy();
  const receiptBody = api.unwrap(receipt.body);
  expect(receiptBody.status).toBe('ITEM_DELIVERED');
  expect(receiptBody.evidence as string[]).toContain('BUYER_CONFIRMED');

  // 02 §4.5.1 — entering ITEM_DELIVERED opens the settlement window rather than
  // paying anyone. Both stamps are guard inputs for the transition itself
  // (HasDeliveryEvidence + PayoutEligibleAt), so their absence would have been a
  // refused transition, not a late write.
  const delivered = await getSettlementState(txId);
  expect(delivered?.status).toBe('ITEM_DELIVERED');
  expect(delivered?.deliveryVerifiedAt, 'DeliveryVerifiedAt not stamped').not.toBeNull();
  expect(delivered?.payoutEligibleAt, 'PayoutEligibleAt not stamped').not.toBeNull();
  expect(
    delivered?.settlementVerifiedAt,
    'settlement verified before the window elapsed',
  ).toBeNull();

  // 7. Bring the settlement window forward (DEPLOY_RUNBOOK §G.4 control 10a).
  //    This makes the row a CANDIDATE and nothing more: settlement-verification
  //    re-reads the buyer's inventory for real, and stamps SettlementVerifiedAt
  //    only because the traded item is genuinely still there.
  await setPayoutEligibleNow(txId);
  const verified = await pollSettlementVerified(txId);
  expect(
    verified?.settlementVerifiedAt,
    `settlement never verified: ${JSON.stringify(verified)}`,
  ).toBeTruthy();

  // 8. Seller payout pipeline (queue → dispatch → confirm → complete, each on a
  //    per-minute cron, all gated on that stamp) → COMPLETED.
  await api.pollStatus(buyerToken, txId, 'COMPLETED', { timeoutMs: 300_000 });

  // 9. WP19 notifications: every producer fired, COMPLETED fanned out to both
  //    parties, ITEM_DELIVERED suppressed (AC2 — all notifications correct).
  const notifTypes = await pollNotificationTypes(EXPECTED_NOTIFICATIONS, { timeoutMs: 30_000 });
  for (const type of EXPECTED_NOTIFICATIONS) {
    expect(notifTypes, `missing ${type}: ${JSON.stringify(notifTypes)}`).toContain(type);
  }
  // COMPLETED is written for seller + buyer (WP19: 2 rows).
  expect(
    notifTypes.filter((t) => t === 'TRANSACTION_COMPLETED').length,
    `COMPLETED fan-out: ${JSON.stringify(notifTypes)}`,
  ).toBe(2);
  // Delivery emits a realtime badge only — no inbox notification.
  expect(
    notifTypes,
    `ITEM_DELIVERED must be suppressed: ${JSON.stringify(notifTypes)}`,
  ).not.toContain('ITEM_DELIVERED');
});
