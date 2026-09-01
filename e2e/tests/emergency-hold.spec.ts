import { test, expect } from '@playwright/test';
import {
  seedHappyPath,
  ensureAdmin,
  seed,
  closePool,
  getTransactionHoldState,
  getSettlementState,
  setPayoutEligibleNow,
  pollSettlementVerified,
  backdateDeadline,
  pollCancelledNoticeRecipients,
  pollNotificationRecipients,
} from '../src/db';
import { mintAccessToken } from '../src/jwt';
import * as api from '../src/api';

/**
 * T112 emergency-hold scenarios, rewritten for P2P (T138) — drives the AD19b /
 * AD19c admin emergency-hold surface at the API level against the
 * docker-compose.e2e.yml stack. Apply-hold / release-hold (RESUME / CANCEL) and
 * the ITEM_DELIVERED cancel guard are fully wired backend-side.
 *
 * Covers 03 §8.8 (admin emergency hold):
 *   1. Apply hold (CREATED) → timeout frozen (a backdated deadline does NOT
 *      cancel) → RESUME → the transaction is live again and walks on to
 *      SELLER_CONFIRMED.
 *   2. Apply hold (SELLER_CONFIRMED) → CANCEL → CANCELLED_ADMIN, both parties
 *      notified (cancel is allowed for any non-delivered pre-hold state).
 *   3. Apply hold at ITEM_DELIVERED → CANCEL rejected (422
 *      CANNOT_CANCEL_DELIVERED_HOLD; only RESUME is permitted) → RESUME → the
 *      settlement + payout pipeline drives it to COMPLETED.
 *
 * Two rewrites worth naming.
 *
 * Scenario 2's pre-hold state moved from ITEM_ESCROWED to SELLER_CONFIRMED — the
 * same point in the story (arranged, unpaid) minus the custody. Its old
 * `itemReturned === true` assertion is gone with the field (v3.0): the platform
 * never held the skin, so an admin cancel here can only ever be about money, and
 * there is no money yet.
 *
 * Scenario 3 needed a real repair, not a rename. Under custody, ITEM_DELIVERED →
 * COMPLETED was a per-minute payout pipeline and "the hold holds the line" was
 * proven by simply watching it not fire. P2P put the 02 §4.5.1 settlement window
 * in front of that pipeline, so a held transaction would now sit at
 * ITEM_DELIVERED for eight days whether or not the hold worked — the assertion
 * would have passed for the wrong reason. The repair is to clear the clock as an
 * excuse: apply the hold, THEN bring the eligibility date forward, so the row is
 * due for payout and the hold is the only thing that can be stopping it. That
 * order also leaves no window in which the row is eligible but not yet held —
 * see the comment at the call site.
 *
 * Levers: the harness backdates the active phase deadline to prove the held row
 * is skipped by the DeadlineScannerJob (05 §4.4), and brings the settlement
 * clock forward (DEPLOY_RUNBOOK §G.4 control 10a) for scenario 3. The
 * EMERGENCY_HOLD permission is satisfied by the admin's super_admin claim. All
 * hold reason/note text is fixed test data.
 */

const HOLD_REASON = 'E2E emergency-hold scenario — sanctions review (automated).';
const RELEASE_NOTE = 'E2E hold release — automated note.';

test.beforeEach(async () => {
  // Clear any inventory / trade-hold a prior suite drove into the shared
  // (in-process) fake state; seedHappyPath re-drives the seller's inventory
  // right after, and the buyer's zero baseline depends on this reset.
  await api.resetFakeSteamState();
});

test.afterAll(async () => {
  await api.resetFakeSteamState();
  await closePool();
});

function tokens(): { sellerToken: string; buyerToken: string; adminToken: string } {
  return {
    sellerToken: mintAccessToken({ userId: seed.sellerId, steamId: seed.sellerSteamId }),
    buyerToken: mintAccessToken({ userId: seed.buyerId, steamId: seed.buyerSteamId }),
    adminToken: mintAccessToken({
      userId: seed.adminId,
      steamId: seed.adminSteamId,
      role: 'super_admin',
    }),
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
  });
  expect(create.ok, `create failed: ${JSON.stringify(create.body)}`).toBeTruthy();
  const created = api.unwrap(create.body);
  expect(created.status).toBe('CREATED');
  return String(created.id);
}

test('apply hold → timeout frozen → resume → transaction continues to SELLER_CONFIRMED', async () => {
  await seedHappyPath();
  await ensureAdmin();
  const { sellerToken, buyerToken, adminToken } = tokens();
  const txId = await createTransaction(sellerToken);

  // 03 §8.8 steps 5–7 — admin freezes the CREATED transaction in place.
  const hold = await api.applyEmergencyHold(adminToken, txId, HOLD_REASON);
  expect(hold.ok, `apply hold failed: ${JSON.stringify(hold.body)}`).toBeTruthy();
  const holdBody = api.unwrap(hold.body);
  // The hold is an overlay: status stays the pre-hold value, surfaced as the
  // projected "EMERGENCY_HOLD" string + the captured previousStatus (07 §9.21).
  expect(holdBody.status).toBe('EMERGENCY_HOLD');
  expect(holdBody.previousStatus).toBe('CREATED');
  expect(holdBody.frozenAt, 'frozenAt missing on the hold response').toBeTruthy();

  // 05 §4.5 / 06 §3.5 — IsOnHold + the freeze trio are stamped, remainder > 0.
  const held = await getTransactionHoldState(txId);
  expect(held?.status).toBe('CREATED');
  expect(held?.isOnHold).toBe(true);
  expect(held?.timeoutFreezeReason).toBe('EMERGENCY_HOLD');
  expect(held?.timeoutFrozenAt, 'TimeoutFrozenAt not stamped').not.toBeNull();
  expect(held?.timeoutRemainingSeconds ?? 0, 'no frozen remainder captured').toBeGreaterThan(0);

  // 03 §8.8 step 6 — both parties are notified the transaction was frozen.
  const heldNotice = await pollNotificationRecipients(
    'EMERGENCY_HOLD_APPLIED',
    [seed.sellerId, seed.buyerId],
    { timeoutMs: 60_000 },
  );
  expect(heldNotice).toContain(seed.sellerId.toLowerCase());
  expect(heldNotice).toContain(seed.buyerId.toLowerCase());

  // "Timeout durur" — push the accept deadline into the past. A NON-held CREATED
  // transaction would be cancelled by the next DeadlineScannerJob sweep (5 s in
  // the e2e stack); the held row is skipped. While on hold the detail endpoint
  // projects status=EMERGENCY_HOLD, so the API view stays EMERGENCY_HOLD across
  // several sweeps — and, decisively, the underlying phase status is still
  // CREATED (the scanner never flipped it to CANCELLED_TIMEOUT).
  await backdateDeadline(txId, 'AcceptDeadline');
  await api.assertStatusStable(buyerToken, txId, 'EMERGENCY_HOLD', { durationMs: 18_000 });
  const stillFrozen = await getTransactionHoldState(txId);
  expect(stillFrozen?.status).toBe('CREATED');
  expect(stillFrozen?.isOnHold).toBe(true);

  // 03 §8.8 step 8 "Devam ettir" — RESUME lifts the hold; status returns to the
  // pre-hold value and the timeout resumes at the captured remainder.
  const resume = await api.releaseEmergencyHold(adminToken, txId, 'RESUME', RELEASE_NOTE);
  expect(resume.ok, `resume failed: ${JSON.stringify(resume.body)}`).toBeTruthy();
  const resumeBody = api.unwrap(resume.body);
  expect(resumeBody.status).toBe('CREATED');
  expect(resumeBody.action).toBe('RESUME');
  // RESUME moves no money — the field stays null on the wire (07 §9.22).
  expect(resumeBody.paymentRefunded ?? null).toBeNull();

  const resumed = await getTransactionHoldState(txId);
  expect(resumed?.isOnHold).toBe(false);
  expect(resumed?.timeoutFreezeReason).toBeNull();
  expect(resumed?.timeoutFrozenAt, 'freeze stamp not cleared on resume').toBeNull();

  // 03 §8.8 step 6 — release notifies both parties.
  const resumeNotice = await pollNotificationRecipients(
    'EMERGENCY_HOLD_RELEASED',
    [seed.sellerId, seed.buyerId],
    { timeoutMs: 60_000 },
  );
  expect(resumeNotice).toContain(seed.sellerId.toLowerCase());
  expect(resumeNotice).toContain(seed.buyerId.toLowerCase());

  // "Devam" — the resumed transaction is live (RESUME rewrote AcceptDeadline to
  // now + remainder, so it is no longer past-due). Both party steps still work:
  // the buyer accepts and the seller confirms readiness, which is the P2P proof
  // that the freeze left no residue on either side's gate.
  const accept = await api.acceptTransaction(buyerToken, txId, seed.buyerRefundAddress);
  expect(accept.ok, `accept after resume failed: ${JSON.stringify(accept.body)}`).toBeTruthy();
  expect(api.unwrap(accept.body).status).toBe('ACCEPTED');

  const ready = await api.confirmReady(sellerToken, txId);
  expect(ready.ok, `confirm-ready after resume failed: ${JSON.stringify(ready.body)}`).toBeTruthy();
  expect(api.unwrap(ready.body).status).toBe('SELLER_CONFIRMED');
});

test('apply hold (SELLER_CONFIRMED) → cancel → CANCELLED_ADMIN, nothing refunded, both notified', async () => {
  await seedHappyPath();
  await ensureAdmin();
  const { sellerToken, buyerToken, adminToken } = tokens();
  const txId = await createTransaction(sellerToken);

  // Drive to SELLER_CONFIRMED (payment window open, buyer has not paid).
  const accept = await api.acceptTransaction(buyerToken, txId, seed.buyerRefundAddress);
  expect(accept.ok, `accept failed: ${JSON.stringify(accept.body)}`).toBeTruthy();
  const ready = await api.confirmReady(sellerToken, txId);
  expect(ready.ok, `confirm-ready failed: ${JSON.stringify(ready.body)}`).toBeTruthy();
  expect(api.unwrap(ready.body).status).toBe('SELLER_CONFIRMED');

  // Apply the hold mid-flow.
  const hold = await api.applyEmergencyHold(adminToken, txId, HOLD_REASON);
  expect(hold.ok, `apply hold failed: ${JSON.stringify(hold.body)}`).toBeTruthy();
  expect(api.unwrap(hold.body).previousStatus).toBe('SELLER_CONFIRMED');
  const held = await getTransactionHoldState(txId);
  expect(held?.isOnHold).toBe(true);
  expect(held?.timeoutFreezeReason).toBe('EMERGENCY_HOLD');

  const heldNotice = await pollNotificationRecipients(
    'EMERGENCY_HOLD_APPLIED',
    [seed.sellerId, seed.buyerId],
    { timeoutMs: 60_000 },
  );
  expect(heldNotice).toContain(seed.sellerId.toLowerCase());
  expect(heldNotice).toContain(seed.buyerId.toLowerCase());

  // 03 §8.8 step 8 "İptal et" — CANCEL is allowed for a non-delivered pre-hold
  // state. The hold is released, then the transaction is admin-cancelled with
  // the standard AD19 fan-out. In P2P that fan-out has one leg, not two: there
  // is no item to hand back, and the buyer never paid, so nothing moves at all.
  const cancel = await api.releaseEmergencyHold(adminToken, txId, 'CANCEL', RELEASE_NOTE);
  expect(cancel.ok, `cancel-after-hold failed: ${JSON.stringify(cancel.body)}`).toBeTruthy();
  const cancelBody = api.unwrap(cancel.body);
  expect(cancelBody.status).toBe('CANCELLED_ADMIN');
  expect(cancelBody.action).toBe('CANCEL');
  expect(cancelBody.paymentRefunded).toBe(false);

  const after = await getTransactionHoldState(txId);
  expect(after?.status).toBe('CANCELLED_ADMIN');
  expect(after?.isOnHold).toBe(false);

  // 03 §8.8 / §8.7 — neither party initiated the cancel, so BOTH are notified.
  const recipients = await pollCancelledNoticeRecipients([seed.sellerId, seed.buyerId], {
    timeoutMs: 60_000,
  });
  expect(recipients).toContain(seed.sellerId.toLowerCase());
  expect(recipients).toContain(seed.buyerId.toLowerCase());
});

test('apply hold at ITEM_DELIVERED → cancel rejected (422), resume only → COMPLETED', async () => {
  await seedHappyPath();
  await ensureAdmin();
  const { sellerToken, buyerToken, adminToken } = tokens();
  const txId = await createTransaction(sellerToken);

  // Drive the full P2P happy path to ITEM_DELIVERED.
  const accept = await api.acceptTransaction(buyerToken, txId, seed.buyerRefundAddress);
  expect(accept.ok, `accept failed: ${JSON.stringify(accept.body)}`).toBeTruthy();
  const ready = await api.confirmReady(sellerToken, txId);
  expect(ready.ok, `confirm-ready failed: ${JSON.stringify(ready.body)}`).toBeTruthy();
  const pay = await api.payViaFake(txId);
  expect(pay.ok, `pay failed: ${JSON.stringify(pay.body)}`).toBeTruthy();
  await api.pollStatus(buyerToken, txId, 'PAYMENT_RECEIVED', { timeoutMs: 90_000 });
  // The seller's trade has to actually happen: the settlement re-read at the end
  // of this test asks whether the buyer still holds the item, and it asks Steam.
  const trade = await api.simulateFakeTrade(
    seed.sellerSteamId,
    seed.buyerSteamId,
    seed.itemAssetId,
  );
  expect(trade.ok, `trade simulation failed: ${JSON.stringify(trade.body)}`).toBeTruthy();
  const receipt = await api.confirmReceipt(buyerToken, txId);
  expect(receipt.ok, `confirm-receipt failed: ${JSON.stringify(receipt.body)}`).toBeTruthy();
  expect(api.unwrap(receipt.body).status).toBe('ITEM_DELIVERED');

  const hold = await api.applyEmergencyHold(adminToken, txId, HOLD_REASON);
  expect(hold.ok, `apply hold failed: ${JSON.stringify(hold.body)}`).toBeTruthy();
  expect(api.unwrap(hold.body).previousStatus).toBe('ITEM_DELIVERED');
  const held = await getTransactionHoldState(txId);
  expect(held?.status).toBe('ITEM_DELIVERED');
  expect(held?.isOnHold).toBe(true);
  expect(held?.timeoutFreezeReason).toBe('EMERGENCY_HOLD');

  // Only NOW make the row payout-ELIGIBLE. Ordering matters and the other order
  // is subtly wrong twice over. Setting the clock first would leave a window —
  // small, but the settlement cron is a real background job — in which the row
  // is eligible and NOT yet held, so a sweep could stamp SettlementVerifiedAt
  // and turn the assertion below into a flake. And leaving the clock alone
  // entirely would be worse than a flake: the row would sit at ITEM_DELIVERED
  // for the eight-day window whether or not the hold worked, so "the hold holds
  // the line" would pass while testing nothing. Held first, then eligible: the
  // hold is the only thing left that can be stopping it.
  await setPayoutEligibleNow(txId);

  const heldNotice = await pollNotificationRecipients(
    'EMERGENCY_HOLD_APPLIED',
    [seed.sellerId, seed.buyerId],
    { timeoutMs: 60_000 },
  );
  expect(heldNotice).toContain(seed.sellerId.toLowerCase());
  expect(heldNotice).toContain(seed.buyerId.toLowerCase());

  // The hold holds the line at BOTH gates: SettlementVerificationJob and
  // SellerPayoutQueueJob each filter on !IsOnHold. The window is due, so a
  // working hold is the only reason nothing advances.
  //
  // Calibration, so the next reader does not overclaim it: SettlementVerification
  // runs on a FIVE-MINUTE cron (SettlementVerificationJob.Cron — a const, not a
  // knob, and unlike Timeouts__DeadlineScannerIntervalSeconds it is not lowered
  // for e2e). A ~20s park window therefore rarely contains a sweep, so the null
  // SettlementVerifiedAt below is CORROBORATION, not the decisive proof — the
  // decisive assertions are that the projection stays EMERGENCY_HOLD and that
  // IsOnHold survives. The job-level !IsOnHold filter itself is pinned where it
  // can be pinned deterministically: SettlementVerificationJobTests
  // .IneligibleTransactions_AreNotEvenRead("hold"). Widening this window to
  // clear the cron would add five idle minutes to the leg for a duplicate.
  await api.assertStatusStable(buyerToken, txId, 'EMERGENCY_HOLD', { durationMs: 18_000 });
  const parked = await getSettlementState(txId);
  expect(parked?.status).toBe('ITEM_DELIVERED');
  expect(parked?.settlementVerifiedAt, 'settlement ran on a held transaction').toBeNull();
  expect((await getTransactionHoldState(txId))?.isOnHold).toBe(true);

  // 03 §8.8 note — CANCEL is forbidden once the item has been delivered: the item
  // is already with the buyer, so standard cancel/refund cannot apply. The guard
  // rejects with 422 CANNOT_CANCEL_DELIVERED_HOLD and the hold survives.
  const badCancel = await api.releaseEmergencyHold(adminToken, txId, 'CANCEL', RELEASE_NOTE);
  expect(badCancel.status, `expected 422, body: ${JSON.stringify(badCancel.body)}`).toBe(422);
  const errorCode = ((badCancel.body as Record<string, unknown>)?.error as Record<string, unknown>)
    ?.code;
  expect(errorCode).toBe('CANNOT_CANCEL_DELIVERED_HOLD');
  expect((await getTransactionHoldState(txId))?.isOnHold, 'hold lifted by a rejected cancel').toBe(
    true,
  );

  // 03 §8.8 note — only RESUME is permitted. Releasing the hold returns the
  // transaction to ITEM_DELIVERED and re-arms the settlement + payout pipeline.
  const resume = await api.releaseEmergencyHold(adminToken, txId, 'RESUME', RELEASE_NOTE);
  expect(resume.ok, `resume failed: ${JSON.stringify(resume.body)}`).toBeTruthy();
  const resumeBody = api.unwrap(resume.body);
  expect(resumeBody.status).toBe('ITEM_DELIVERED');
  expect(resumeBody.action).toBe('RESUME');
  expect((await getTransactionHoldState(txId))?.isOnHold).toBe(false);

  const resumeNotice = await pollNotificationRecipients(
    'EMERGENCY_HOLD_RELEASED',
    [seed.sellerId, seed.buyerId],
    { timeoutMs: 60_000 },
  );
  expect(resumeNotice).toContain(seed.sellerId.toLowerCase());
  expect(resumeNotice).toContain(seed.buyerId.toLowerCase());

  // "Devam" — with the hold gone, settlement-verification re-reads the buyer's
  // inventory (the item is there, because the trade really happened above) and
  // the payout pipeline follows it to COMPLETED.
  const verified = await pollSettlementVerified(txId);
  expect(
    verified?.settlementVerifiedAt,
    `settlement never verified after resume: ${JSON.stringify(verified)}`,
  ).toBeTruthy();
  const completed = await api.pollStatus(sellerToken, txId, 'COMPLETED', { timeoutMs: 300_000 });
  expect(completed).toBe('COMPLETED');
});
