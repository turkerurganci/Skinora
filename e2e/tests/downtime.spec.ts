import { test, expect } from '@playwright/test';
import {
  seedHappyPath,
  ensureAdmin,
  getTransactionHoldState,
  backdateDeadline,
  closePool,
  seed,
} from '../src/db';
import { mintAccessToken } from '../src/jwt';
import * as api from '../src/api';

/**
 * T114 downtime & maintenance scenarios, rewritten for P2P (T138) — drives the
 * WP7 admin maintenance/outage control surface at the API level against the
 * docker-compose.e2e.yml stack.
 *
 * Covers 03 §11:
 *   §11.1 Platform maintenance — PLATFORM_MAINTENANCE freezes every active
 *         timeout, raises the C08 banner, and resume continues the flow.
 *   §11.2 Global Steam outage — STEAM_OUTAGE freezes the Steam-bound timeouts
 *         and surfaces the user notice via the banner.
 *   §11.3 Blockchain degradation — BLOCKCHAIN_DEGRADATION freezes the
 *         payment-step timeout, and resume restores delayed payment detection.
 *
 * §11.2 is the scenario the P2P pivot changed most, and not just by renaming a
 * state. Under custody, a Steam outage froze the two trade-offer phases for the
 * obvious reason: the platform's own bot could not send or receive offers. In
 * P2P the parties trade directly, so a Steam outage does not stop THEM — if
 * Steam is up for the two of them, the trade goes through. What breaks is the
 * platform's ability to VERIFY it. So the scope moved to the two phases whose
 * deadline depends on a platform-side Steam READ (TimeoutFreezeReasonScopes):
 * ACCEPTED, where confirm-ready must re-read the seller's inventory, and
 * PAYMENT_RECEIVED, where the delivery round must read both. Freezing the second
 * is what stops a seller who delivered fine from being recorded as having failed
 * (02 §23, 03 §11.2). This test drives the first.
 *
 * Simulation lever (AC "E2E (simüle)"): the admin freeze endpoint is the manual
 * trigger 03 §11 lists alongside auto-detection. "Frozen" is proven decisively by
 * back-dating the active phase deadline into the past and letting the real
 * DeadlineScannerJob (5 s in e2e) sweep several times — a frozen row is filtered
 * out (!IsOnHold && TimeoutFrozenAt IS NULL) so it never times out. Maintenance
 * freeze does NOT set IsOnHold, so the detail status keeps reporting the
 * underlying phase status (no EMERGENCY_HOLD overlay); the freeze trio is read
 * straight from the DB. The user-facing "bildirim" is the public maintenance
 * banner / MaintenanceStatusChanged broadcast (no per-user inbox row), so it is
 * asserted via GET /platform/maintenance; the SignalR push stays unasserted
 * (broadcast-only — T112/T113 pattern).
 */

/** ≥3 DeadlineScannerJob sweeps at the e2e 5s interval — long enough that a
 *  freeze failure would surface as a CANCELLED_TIMEOUT flip within the window. */
const SCANNER_SKIP_WINDOW_MS = 18_000;

test.beforeEach(async () => {
  // T137 fix-round G1: this reset must run BEFORE seedHappyPath, never after.
  // seedHappyPath drives the seller's fake inventory, and every scenario below
  // needs it — a reset inside the test body (where two of these scenarios used
  // to call it) wipes the seed and every `create` afterwards is rejected
  // ITEM_NOT_IN_INVENTORY.
  await api.resetFakeSteamState();
});

test.afterAll(async () => {
  await api.resetFakeSteamState();
  await closePool();
});

/** super_admin token for the maintenance endpoints (MANAGE_SETTINGS via the
 *  PermissionAuthorizationHandler bypass). The admin must exist as a User row —
 *  the freeze/resume audit row carries ActorId under a NO ACTION FK. */
function adminToken(): string {
  return mintAccessToken({
    userId: seed.adminId,
    steamId: seed.adminSteamId,
    role: 'super_admin',
  });
}

/** Seller creates a fresh CREATED transaction against the seeded happy-path
 *  parties; returns its id. */
async function createCreated(sellerToken: string): Promise<string> {
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

test('platform maintenance: freeze halts the accept timeout + raises the banner; resume continues the flow', async () => {
  await seedHappyPath();
  await ensureAdmin();
  const admin = adminToken();
  const sellerToken = mintAccessToken({ userId: seed.sellerId, steamId: seed.sellerSteamId });
  const buyerToken = mintAccessToken({ userId: seed.buyerId, steamId: seed.buyerSteamId });

  const txId = await createCreated(sellerToken);

  try {
    // Authz gate (03 §8 / 02 §21) — a plain user cannot enter maintenance.
    const forbidden = await api.freezeMaintenance(buyerToken, 'PLATFORM_MAINTENANCE');
    expect(forbidden.status, `expected 403, body: ${JSON.stringify(forbidden.body)}`).toBe(403);

    // 03 §11.1 step 3-4 — admin enters platform maintenance: all active timeouts
    // freeze and the banner goes live.
    const freeze = await api.freezeMaintenance(admin, 'PLATFORM_MAINTENANCE', {
      message: 'Platform maintenance in progress.',
      plannedEnd: '2026-07-01T18:00:00Z',
    });
    expect(freeze.ok, `freeze failed: ${JSON.stringify(freeze.body)}`).toBeTruthy();
    const freezeBody = api.unwrap(freeze.body);
    expect(freezeBody.active).toBe(true);
    expect(freezeBody.type).toBe('PLATFORM_MAINTENANCE');
    expect(Number(freezeBody.affectedTransactions)).toBeGreaterThanOrEqual(1);

    // 03 §11.1 step 4 — the C08 maintenance banner is live for every user.
    const banner = api.unwrap((await api.getPlatformMaintenance()).body);
    expect(banner.active).toBe(true);
    expect(banner.type).toBe('PLATFORM_MAINTENANCE');
    expect(banner.message).toBe('Platform maintenance in progress.');

    // The transaction's accept timeout is frozen (06 §3.5 trio). Maintenance
    // freeze is not an emergency hold, so IsOnHold stays false.
    const held = await getTransactionHoldState(txId);
    expect(held?.status).toBe('CREATED');
    expect(held?.isOnHold).toBe(false);
    expect(held?.timeoutFreezeReason).toBe('MAINTENANCE');
    expect(held?.timeoutFrozenAt).not.toBeNull();
    expect(held?.timeoutRemainingSeconds ?? 0).toBeGreaterThan(0);

    // Decisive "timeout dondurma": push the accept deadline into the past, then
    // let the scanner sweep several times — the frozen row is skipped, so it
    // never flips to CANCELLED_TIMEOUT.
    await backdateDeadline(txId, 'AcceptDeadline');
    await api.assertStatusStable(buyerToken, txId, 'CREATED', {
      durationMs: SCANNER_SKIP_WINDOW_MS,
    });
    expect((await getTransactionHoldState(txId))?.status).toBe('CREATED');

    // 03 §11.1 step 5-6 — maintenance ends: resume the frozen timeouts + clear banner.
    const resume = await api.resumeMaintenance(admin);
    expect(resume.ok, `resume failed: ${JSON.stringify(resume.body)}`).toBeTruthy();
    const resumeBody = api.unwrap(resume.body);
    expect(resumeBody.active).toBe(false);
    expect(resumeBody.type).toBeNull();
    expect(Number(resumeBody.affectedTransactions)).toBeGreaterThanOrEqual(1);

    expect(api.unwrap((await api.getPlatformMaintenance()).body).active).toBe(false);
    const resumed = await getTransactionHoldState(txId);
    expect(resumed?.timeoutFrozenAt).toBeNull();
    expect(resumed?.timeoutFreezeReason).toBeNull();

    // 03 §11.1 step 6 — the flow continues from where it left off: the buyer can
    // still accept (the accept deadline was rewritten now + remainder) and the
    // seller's readiness step works too.
    const accept = await api.acceptTransaction(buyerToken, txId, seed.buyerRefundAddress);
    expect(accept.ok, `accept failed: ${JSON.stringify(accept.body)}`).toBeTruthy();
    expect(api.unwrap(accept.body).status).toBe('ACCEPTED');
    const ready = await api.confirmReady(sellerToken, txId);
    expect(ready.ok, `confirm-ready failed: ${JSON.stringify(ready.body)}`).toBeTruthy();
    expect(api.unwrap(ready.body).status).toBe('SELLER_CONFIRMED');
  } finally {
    await api.resumeMaintenance(admin).catch(() => undefined);
  }
});

test('steam outage: freeze halts the seller-confirm timeout + shows the outage notice; resume re-arms it', async () => {
  await seedHappyPath();
  await ensureAdmin();
  const admin = adminToken();
  const sellerToken = mintAccessToken({ userId: seed.sellerId, steamId: seed.sellerSteamId });
  const buyerToken = mintAccessToken({ userId: seed.buyerId, steamId: seed.buyerSteamId });

  try {
    const txId = await createCreated(sellerToken);

    // Park in ACCEPTED — one of the two states STEAM_OUTAGE covers, and the one
    // reachable without spending a payment. The seller has not confirmed
    // readiness yet, which is exactly the step a Steam outage would block: it
    // needs a fresh read of the seller's inventory (07 §7.6a Stage 4).
    const accept = await api.acceptTransaction(buyerToken, txId, seed.buyerRefundAddress);
    expect(accept.ok, `accept failed: ${JSON.stringify(accept.body)}`).toBeTruthy();
    expect(api.unwrap(accept.body).status).toBe('ACCEPTED');

    // No deadline fixture is needed any more. The custody-era version had to
    // stamp SellerConfirmDeadline by hand because the state it parked in was
    // reached through the fake's trade webhook, which never went through the
    // production stamp; ACCEPTED is reached through the real accept endpoint,
    // and TransactionAcceptanceService arms the deadline itself. The freeze
    // therefore captures a genuine remainder rather than a fabricated one.

    // 03 §11.2 step 1-3 — global Steam outage: the Steam-bound timeouts freeze and
    // users see the "Steam temporarily unavailable, your transactions are safe"
    // notice (the public banner / MaintenanceStatusChanged push).
    const message = 'Steam services temporarily unavailable — your transactions are safe.';
    const freeze = await api.freezeMaintenance(admin, 'STEAM_OUTAGE', { message });
    expect(freeze.ok, `freeze failed: ${JSON.stringify(freeze.body)}`).toBeTruthy();
    const freezeBody = api.unwrap(freeze.body);
    expect(freezeBody.type).toBe('STEAM_OUTAGE');
    expect(Number(freezeBody.affectedTransactions)).toBeGreaterThanOrEqual(1);

    // 03 §11.2 step 3 — the user-facing outage notice rides the banner.
    const banner = api.unwrap((await api.getPlatformMaintenance()).body);
    expect(banner.active).toBe(true);
    expect(banner.type).toBe('STEAM_OUTAGE');
    expect(banner.message).toBe(message);

    // The parked transaction's seller-confirm timeout is frozen with STEAM_OUTAGE.
    const held = await getTransactionHoldState(txId);
    expect(held?.status).toBe('ACCEPTED');
    expect(held?.timeoutFreezeReason).toBe('STEAM_OUTAGE');
    expect(held?.timeoutFrozenAt).not.toBeNull();
    expect(held?.timeoutRemainingSeconds ?? 0).toBeGreaterThan(0);

    // Decisive "timeout dondurma": back-date the seller-confirm deadline; the
    // frozen row is skipped by the scanner and never times out.
    await backdateDeadline(txId, 'SellerConfirmDeadline');
    await api.assertStatusStable(buyerToken, txId, 'ACCEPTED', {
      durationMs: SCANNER_SKIP_WINDOW_MS,
    });
    expect((await getTransactionHoldState(txId))?.status).toBe('ACCEPTED');

    // 03 §11.2 step 4-6 — Steam recovers: resume re-arms the timeout + clears the banner.
    const resume = await api.resumeMaintenance(admin);
    expect(resume.ok, `resume failed: ${JSON.stringify(resume.body)}`).toBeTruthy();
    const resumeBody = api.unwrap(resume.body);
    expect(resumeBody.active).toBe(false);
    expect(Number(resumeBody.affectedTransactions)).toBeGreaterThanOrEqual(1);

    expect(api.unwrap((await api.getPlatformMaintenance()).body).active).toBe(false);
    const resumed = await getTransactionHoldState(txId);
    expect(resumed?.timeoutFreezeReason).toBeNull();
    expect(resumed?.timeoutFrozenAt).toBeNull();
    // Still an active transaction (the timeout was re-armed, not cancelled) —
    // and the step the outage was blocking now goes through.
    expect(resumed?.status).toBe('ACCEPTED');
    const ready = await api.confirmReady(sellerToken, txId);
    expect(
      ready.ok,
      `confirm-ready after recovery failed: ${JSON.stringify(ready.body)}`,
    ).toBeTruthy();
    expect(api.unwrap(ready.body).status).toBe('SELLER_CONFIRMED');
  } finally {
    await api.resumeMaintenance(admin).catch(() => undefined);
  }
});

test('blockchain degradation: freeze halts the payment timeout; resume restores payment detection', async () => {
  await seedHappyPath();
  await ensureAdmin();
  const admin = adminToken();
  const sellerToken = mintAccessToken({ userId: seed.sellerId, steamId: seed.sellerSteamId });
  const buyerToken = mintAccessToken({ userId: seed.buyerId, steamId: seed.buyerSteamId });

  try {
    const txId = await createCreated(sellerToken);
    const accept = await api.acceptTransaction(buyerToken, txId, seed.buyerRefundAddress);
    expect(accept.ok, `accept failed: ${JSON.stringify(accept.body)}`).toBeTruthy();

    // SELLER_CONFIRMED is the whole scope of BLOCKCHAIN_DEGRADATION: it is the
    // only phase whose deadline is blockchain-bound (PaymentDeadline). Under
    // custody that phase was ITEM_ESCROWED — the same clock, a different reason
    // for the platform to be holding it.
    const ready = await api.confirmReady(sellerToken, txId);
    expect(ready.ok, `confirm-ready failed: ${JSON.stringify(ready.body)}`).toBeTruthy();
    expect(api.unwrap(ready.body).status).toBe('SELLER_CONFIRMED');

    // 03 §11.3 step 1-2 — blockchain infra degraded: the payment-step timeout
    // freezes; nothing else.
    const message = 'Payment verification temporarily delayed — your transactions are safe.';
    const freeze = await api.freezeMaintenance(admin, 'BLOCKCHAIN_DEGRADATION', { message });
    expect(freeze.ok, `freeze failed: ${JSON.stringify(freeze.body)}`).toBeTruthy();
    const freezeBody = api.unwrap(freeze.body);
    expect(freezeBody.type).toBe('BLOCKCHAIN_DEGRADATION');
    expect(Number(freezeBody.affectedTransactions)).toBeGreaterThanOrEqual(1);

    const banner = api.unwrap((await api.getPlatformMaintenance()).body);
    expect(banner.active).toBe(true);
    expect(banner.type).toBe('BLOCKCHAIN_DEGRADATION');

    const held = await getTransactionHoldState(txId);
    expect(held?.status).toBe('SELLER_CONFIRMED');
    expect(held?.timeoutFreezeReason).toBe('BLOCKCHAIN_DEGRADATION');
    expect(held?.timeoutFrozenAt).not.toBeNull();
    expect(held?.timeoutRemainingSeconds ?? 0).toBeGreaterThan(0);

    // Decisive "ödeme timeout dondurma": the per-tx payment-timeout job was
    // cancelled on freeze and the scanner skips frozen rows, so back-dating
    // PaymentDeadline never fires a timeout.
    await backdateDeadline(txId, 'PaymentDeadline');
    await api.assertStatusStable(buyerToken, txId, 'SELLER_CONFIRMED', {
      durationMs: SCANNER_SKIP_WINDOW_MS,
    });
    expect((await getTransactionHoldState(txId))?.status).toBe('SELLER_CONFIRMED');

    // 03 §11.3 step 4-6 — infra recovers: resume re-arms the payment timeout and
    // delayed payment detection proceeds.
    const resume = await api.resumeMaintenance(admin);
    expect(resume.ok, `resume failed: ${JSON.stringify(resume.body)}`).toBeTruthy();
    const resumeBody = api.unwrap(resume.body);
    expect(resumeBody.active).toBe(false);
    expect(Number(resumeBody.affectedTransactions)).toBeGreaterThanOrEqual(1);

    expect(api.unwrap((await api.getPlatformMaintenance()).body).active).toBe(false);
    const resumed = await getTransactionHoldState(txId);
    expect(resumed?.timeoutFreezeReason).toBeNull();
    expect(resumed?.timeoutFrozenAt).toBeNull();

    // 03 §11.3 step 5 — payment detection works again after recovery.
    const pay = await api.payViaFake(txId);
    expect(pay.ok, `pay failed: ${JSON.stringify(pay.body)}`).toBeTruthy();
    await api.pollStatus(buyerToken, txId, 'PAYMENT_RECEIVED', { timeoutMs: 120_000 });
  } finally {
    await api.resumeMaintenance(admin).catch(() => undefined);
  }
});
