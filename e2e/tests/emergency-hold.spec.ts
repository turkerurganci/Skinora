import { test, expect } from '@playwright/test';
import {
  seedHappyPath,
  ensureAdmin,
  seed,
  closePool,
  getTransactionHoldState,
  backdateDeadline,
  pollCancelledNoticeRecipients,
  pollNotificationRecipients,
} from '../src/db';
import { mintAccessToken } from '../src/jwt';
import * as api from '../src/api';

/**
 * T112 emergency-hold scenarios — drives the AD19b/AD19c admin emergency-hold
 * surface at the API level against the docker-compose.e2e.yml stack (same seam as
 * T107–T111). Apply-hold / release-hold (RESUME / CANCEL) and the ITEM_DELIVERED
 * cancel guard are fully wired backend-side (AdminTransactionService +
 * TimeoutFreezeService + the SellerPayout pipeline's !IsOnHold gate), so this task
 * adds test coverage only — no production source changes.
 *
 * Covers 03 §8.8 (admin emergency hold):
 *   1. Apply hold → timeout frozen (a backdated deadline does NOT cancel) →
 *      RESUME → the transaction is live again and continues to ITEM_ESCROWED.
 *   2. Apply hold (ITEM_ESCROWED) → CANCEL → CANCELLED_ADMIN, item returned to
 *      the seller, both parties notified (cancel is allowed for any non-delivered
 *      pre-hold state).
 *   3. Apply hold at ITEM_DELIVERED → CANCEL rejected (422
 *      CANNOT_CANCEL_DELIVERED_HOLD; only RESUME is permitted) → RESUME → the
 *      payout pipeline (released from the hold gate) drives it to COMPLETED.
 *
 * T137a: the custody-era assertions (bot escrow slot, RETURN_TO_SELLER offer)
 * were removed — T117 dropped both tables and P2P has no platform inventory.
 * The flows still drive through ITEM_ESCROWED, which no longer exists; the
 * rewrite is T138's scope.
 *
 * Levers: the harness backdates the active phase deadline to prove the held row
 * is skipped by the DeadlineScannerJob (05 §4.4); the ITEM_DELIVERED park needs
 * no fake lever because the ITEM_DELIVERED→COMPLETED payout pipeline is itself
 * gated by !IsOnHold (SellerPayoutQueueJob + PayoutCompletedConsumer), so the
 * hold holds the line. The EMERGENCY_HOLD permission is satisfied by the admin's
 * super_admin claim. All emergency holds reason/note text is fixed test data.
 */

const HOLD_REASON = 'E2E emergency-hold scenario — sanctions review (automated).';
const RELEASE_NOTE = 'E2E hold release — automated note.';

test.beforeEach(async () => {
  // Clear any direction suppression a prior suite left on the shared (in-process)
  // fake state so the escrow + delivery legs auto-drive normally here.
  await api.resetTradeControl();
});

test.afterAll(async () => {
  await api.resetTradeControl();
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
    sellerWalletAddress: seed.sellerPayoutAddress,
  });
  expect(create.ok, `create failed: ${JSON.stringify(create.body)}`).toBeTruthy();
  const created = api.unwrap(create.body);
  expect(created.status).toBe('CREATED');
  return String(created.id);
}

test('apply hold → timeout frozen → resume → transaction continues to ITEM_ESCROWED', async () => {
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
  // RESUME does not touch item/payment — those fields stay null on the wire.
  expect(resumeBody.itemReturned ?? null).toBeNull();
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
  // now + remainder, so it is no longer past-due). The buyer accepts and the flow
  // proceeds normally through escrow dispatch to ITEM_ESCROWED.
  const accept = await api.acceptTransaction(buyerToken, txId, seed.buyerRefundAddress);
  expect(accept.ok, `accept after resume failed: ${JSON.stringify(accept.body)}`).toBeTruthy();
  expect(api.unwrap(accept.body).status).toBe('ACCEPTED');

  const advanced = await api.pollStatus(buyerToken, txId, 'ITEM_ESCROWED', { timeoutMs: 180_000 });
  expect(advanced).toBe('ITEM_ESCROWED');
});

test('apply hold (ITEM_ESCROWED) → cancel → CANCELLED_ADMIN, item returned, both notified', async () => {
  await seedHappyPath();
  await ensureAdmin();
  const { sellerToken, buyerToken, adminToken } = tokens();
  const txId = await createTransaction(sellerToken);

  // Drive to ITEM_ESCROWED (item on the platform, payment not yet in).
  const accept = await api.acceptTransaction(buyerToken, txId, seed.buyerRefundAddress);
  expect(accept.ok, `accept failed: ${JSON.stringify(accept.body)}`).toBeTruthy();
  await api.pollStatus(buyerToken, txId, 'ITEM_ESCROWED', { timeoutMs: 180_000 });

  // Apply the hold mid-escrow.
  const hold = await api.applyEmergencyHold(adminToken, txId, HOLD_REASON);
  expect(hold.ok, `apply hold failed: ${JSON.stringify(hold.body)}`).toBeTruthy();
  expect(api.unwrap(hold.body).previousStatus).toBe('ITEM_ESCROWED');
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
  // state. The hold is released then the transaction is admin-cancelled, applying
  // the standard AD19 refund fan-out: item back to the seller, no payment to
  // refund (the buyer never paid).
  const cancel = await api.releaseEmergencyHold(adminToken, txId, 'CANCEL', RELEASE_NOTE);
  expect(cancel.ok, `cancel-after-hold failed: ${JSON.stringify(cancel.body)}`).toBeTruthy();
  const cancelBody = api.unwrap(cancel.body);
  expect(cancelBody.status).toBe('CANCELLED_ADMIN');
  expect(cancelBody.action).toBe('CANCEL');
  expect(cancelBody.itemReturned).toBe(true);
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

  // Drive the full happy path to ITEM_DELIVERED (item handed to the buyer). The
  // ITEM_DELIVERED→COMPLETED seller-payout pipeline runs on a per-minute cadence
  // and is itself gated by !IsOnHold, so a hold applied right after delivery
  // parks the transaction at ITEM_DELIVERED with no race.
  const accept = await api.acceptTransaction(buyerToken, txId, seed.buyerRefundAddress);
  expect(accept.ok, `accept failed: ${JSON.stringify(accept.body)}`).toBeTruthy();
  await api.pollStatus(buyerToken, txId, 'ITEM_ESCROWED', { timeoutMs: 180_000 });
  const pay = await api.payViaFake(txId);
  expect(pay.ok, `pay failed: ${JSON.stringify(pay.body)}`).toBeTruthy();
  await api.pollStatus(buyerToken, txId, 'ITEM_DELIVERED', { timeoutMs: 180_000 });

  // Apply the hold at ITEM_DELIVERED.
  const hold = await api.applyEmergencyHold(adminToken, txId, HOLD_REASON);
  expect(hold.ok, `apply hold failed: ${JSON.stringify(hold.body)}`).toBeTruthy();
  expect(api.unwrap(hold.body).previousStatus).toBe('ITEM_DELIVERED');
  const held = await getTransactionHoldState(txId);
  expect(held?.status).toBe('ITEM_DELIVERED');
  expect(held?.isOnHold).toBe(true);
  expect(held?.timeoutFreezeReason).toBe('EMERGENCY_HOLD');

  const heldNotice = await pollNotificationRecipients(
    'EMERGENCY_HOLD_APPLIED',
    [seed.sellerId, seed.buyerId],
    { timeoutMs: 60_000 },
  );
  expect(heldNotice).toContain(seed.sellerId.toLowerCase());
  expect(heldNotice).toContain(seed.buyerId.toLowerCase());

  // The hold holds the line: the payout pipeline is gated by !IsOnHold, so while
  // held the detail endpoint reports EMERGENCY_HOLD and the underlying phase
  // status stays ITEM_DELIVERED (it does not advance to COMPLETED).
  await api.assertStatusStable(buyerToken, txId, 'EMERGENCY_HOLD', { durationMs: 12_000 });
  const parked = await getTransactionHoldState(txId);
  expect(parked?.status).toBe('ITEM_DELIVERED');
  expect(parked?.isOnHold).toBe(true);

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
  // transaction to ITEM_DELIVERED and re-arms the payout pipeline.
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

  // "Devam" — with the hold gone, the seller-payout pipeline (queue → dispatch →
  // confirm → complete, each per-minute) drives the transaction to COMPLETED.
  const completed = await api.pollStatus(sellerToken, txId, 'COMPLETED', { timeoutMs: 300_000 });
  expect(completed).toBe('COMPLETED');
});
