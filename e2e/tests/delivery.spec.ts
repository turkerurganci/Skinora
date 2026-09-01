import { test, expect } from '@playwright/test';
import {
  seedHappyPath,
  backdateDeadline,
  getSettlementState,
  getLatestDeliveryCapture,
  pollDisputeForTransaction,
  pollBuyerRefundConfirmed,
  closePool,
  seed,
} from '../src/db';
import { mintAccessToken } from '../src/jwt';
import * as api from '../src/api';

/**
 * T138 — delivery verification (02 §9.2). A suite with no custody-era ancestor,
 * because the question it asks did not exist before the P2P pivot.
 *
 * Under custody the platform WAS the courier: it held the item, sent the offer
 * and got a callback saying the buyer accepted, so "was it delivered?" had an
 * answer Steam handed over. In P2P the seller trades the item straight to the
 * buyer and the platform is not a party to that trade (02 §2.1) — Steam tells it
 * nothing. Delivery is therefore INFERRED, from exactly two sources:
 *
 *   • the buyer says so (BUYER_CONFIRMED) — sufficient on its own, because the
 *     confirmation releases the buyer's own money and nobody lies to their own
 *     cost; and
 *   • the platform's own reading of two inventories (SELLER_ASSET_GONE ∧
 *     INVENTORY_DELTA) — sufficient in principle, but held shut at launch by
 *     `delivery.inventory_evidence_auto_release_enabled` (DEPLOY_RUNBOOK §H).
 *
 * The two tests here are the two answers that are NOT a cancellation:
 *
 *   1. The fast path — the buyer confirms, and that alone moves the money's
 *      permission forward. Deliberately driven with the item NEVER traded, so
 *      the inventory route could not possibly have contributed: what advances
 *      the transaction is the confirmation and nothing else. It also pins the
 *      07 §7.6b idempotency contract, where a repeat is an answer rather than a
 *      conflict.
 *
 *   2. The misdelivery signature — the seller's asset left their inventory but
 *      never appeared in the buyer's. 02 §9.2 is explicit that this "işlem
 *      sessizce iptal edilmez"; the item went SOMEWHERE, and where is a question
 *      for an admin, not for a timeout. So this is the arm that must NOT cancel
 *      and must NOT refund — it parks the transaction and opens a dispute the
 *      buyer did not ask for, because the buyer may not even know anything went
 *      wrong: from their side the transaction merely looks slow.
 *
 * The third arm — the seller simply never sent, proven by the item still sitting
 * in their inventory — is the one that DOES cancel, so it lives with the other
 * cancellations in timeout.spec.ts (03 §4.4).
 */

test.beforeEach(async () => {
  // seedHappyPath() runs after this and re-drives the seller's inventory; the
  // reset is what guarantees the BUYER starts from a zero baseline, which every
  // count-based judgement below is measured against.
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

/** Fresh seed → create → accept → confirm-ready → pay → PAYMENT_RECEIVED, i.e.
 *  the moment the seller owes the buyer an item and the buyer's money is in
 *  escrow. Every delivery-verification question is asked from here. */
async function driveToPaymentReceived(): Promise<{
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
  const txId = String(api.unwrap(create.body).id);

  const accept = await api.acceptTransaction(buyerToken, txId, seed.buyerRefundAddress);
  expect(accept.ok, `accept failed: ${JSON.stringify(accept.body)}`).toBeTruthy();

  const ready = await api.confirmReady(sellerToken, txId);
  expect(ready.ok, `confirm-ready failed: ${JSON.stringify(ready.body)}`).toBeTruthy();
  // The baseline captured here is the reference for both tests below; without it
  // the buyer half of every judgement is simply unknowable (06 §3.5).
  expect(api.unwrap(ready.body).buyerInventoryVisible).toBe(true);

  const pay = await api.payViaFake(txId);
  expect(pay.ok, `pay failed: ${JSON.stringify(pay.body)}`).toBeTruthy();
  await api.pollStatus(buyerToken, txId, 'PAYMENT_RECEIVED', { timeoutMs: 90_000 });

  return { txId, sellerToken, buyerToken };
}

test('buyer confirmation alone delivers (02 §9.2 fast path), and the repeat is idempotent', async () => {
  const { txId, buyerToken } = await driveToPaymentReceived();

  // NOTE the absence: no api.simulateFakeTrade. The item is still in the
  // seller's inventory and the buyer's class count is still zero, so the
  // inventory route has NOTHING to offer — neither SELLER_ASSET_GONE nor
  // INVENTORY_DELTA can be observed. Whatever advances this transaction is the
  // buyer's word, which is precisely the claim under test. (A real buyer would
  // have the item; the scenario strips the corroboration on purpose so the
  // assertion cannot be satisfied by accident.)
  const receipt = await api.confirmReceipt(buyerToken, txId);
  expect(receipt.ok, `confirm-receipt failed: ${JSON.stringify(receipt.body)}`).toBeTruthy();
  const body = api.unwrap(receipt.body);
  expect(body.status).toBe('ITEM_DELIVERED');
  expect(
    body.deliveryVerifiedAt,
    'deliveryVerifiedAt missing on the receipt response',
  ).toBeTruthy();

  // The evidence set is EXACTLY the buyer's confirmation. If the launch gate had
  // leaked, or an inventory read had contributed, this list would be longer.
  const evidence = body.evidence as string[];
  expect(evidence).toEqual(['BUYER_CONFIRMED']);

  const delivered = await getSettlementState(txId);
  expect(delivered?.status).toBe('ITEM_DELIVERED');
  // 02 §4.5.1 — delivery opens the settlement window; it does not pay anyone.
  expect(delivered?.payoutEligibleAt, 'PayoutEligibleAt not stamped').not.toBeNull();
  expect(delivered?.settlementVerifiedAt).toBeNull();
  // No inventory round ran, so there is no launch-gate capture row to review —
  // the confirmation route bypasses the gate AND its evidence trail (§H.3 reads
  // captures written by INVENTORY rounds).
  expect(await getLatestDeliveryCapture(txId)).toBeNull();

  // 07 §7.6b — a repeat is the same answer a second time, not a 409. This
  // matters because the button is on a page the buyer can reload.
  const repeat = await api.confirmReceipt(buyerToken, txId);
  expect(repeat.status, `repeat body: ${JSON.stringify(repeat.body)}`).toBe(200);
  expect(api.unwrap(repeat.body).status).toBe('ITEM_DELIVERED');
  // Idempotent means unchanged, not merely accepted: the delivery stamp from the
  // FIRST call must survive.
  const after = await getSettlementState(txId);
  expect(after?.deliveryVerifiedAt?.toISOString()).toBe(
    delivered?.deliveryVerifiedAt?.toISOString(),
  );
});

test('seller sent the item somewhere else: no cancel, no refund, dispute auto-escalated', async () => {
  const { txId, buyerToken } = await driveToPaymentReceived();

  // The seller trades the item to a THIRD party. From the platform's two
  // read-only vantage points this is indistinguishable from "sent to the wrong
  // trade URL" — and that ambiguity is the whole point: the asset is gone from
  // the seller, so a cancel would rob them, yet nothing arrived at the buyer, so
  // a payout would rob the buyer.
  const trade = await api.simulateFakeTrade(
    seed.sellerSteamId,
    seed.outsiderSteamId,
    seed.itemAssetId,
  );
  expect(trade.ok, `trade simulation failed: ${JSON.stringify(trade.body)}`).toBeTruthy();

  // The delivery deadline expires. Under custody this alone cancelled the
  // transaction; here it only schedules a verification round (05 §4.4).
  await backdateDeadline(txId, 'DeliveryDeadline');

  // 02 §9.2 / §10.1 — the round raises a dispute nobody opened. OpenedByUserId is
  // the SYSTEM service account (06 §8.9), which is what makes this row different
  // from every other one in the table: 02 §10.2 gives the opening right to the
  // buyer, and this is the exception the same document creates two sections
  // earlier.
  const dispute = await pollDisputeForTransaction(txId, { timeoutMs: 120_000 });
  expect(dispute, 'no dispute opened for the misdelivery signature').not.toBeNull();
  expect(dispute?.type).toBe('DELIVERY');
  expect(dispute?.status).toBe('ESCALATED');
  expect(dispute?.openedByUserId).toBe('00000000-0000-0000-0000-000000000001');
  expect(dispute?.systemCheckResult, 'no system finding recorded on the dispute').toBeTruthy();

  // The verdict is on record for the reviewer (DEPLOY_RUNBOOK §H.3). Asserting
  // it separately from the dispute keeps the two claims independent: the dispute
  // proves an escalation happened, the capture proves WHY.
  const capture = await getLatestDeliveryCapture(txId);
  expect(capture, 'no DeliveryEvidenceCaptures row written').not.toBeNull();
  expect(capture?.verdict).toBe('MisdeliverySignature');

  // The decisive negative: the transaction did NOT move. It is held at
  // PAYMENT_RECEIVED with the dispute flag raised — not cancelled, not delivered.
  const state = await getSettlementState(txId);
  expect(state?.status).toBe('PAYMENT_RECEIVED');
  expect(state?.hasActiveDispute).toBe(true);
  expect(state?.deliveryVerifiedAt, 'delivery must not be recorded').toBeNull();

  // And no money moved in either direction while a human decides.
  const refund = await pollBuyerRefundConfirmed(txId, { timeoutMs: 8_000 });
  expect(
    refund,
    `an escalated misdelivery must not auto-refund: ${JSON.stringify(refund)}`,
  ).toBeNull();

  // The status the buyer's own view reports is unchanged too — the escalation is
  // an admin-side fact, so nothing about it silently ends the transaction.
  await api.assertStatusStable(buyerToken, txId, 'PAYMENT_RECEIVED', { durationMs: 12_000 });
});
