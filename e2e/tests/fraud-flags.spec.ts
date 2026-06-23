import { test, expect } from '@playwright/test';
import {
  seedHappyPath,
  ensureAdmin,
  seed,
  closePool,
  getFlagForTransaction,
  insertAccountFlag,
  getSystemSetting,
  setSystemSetting,
} from '../src/db';
import { mintAccessToken } from '../src/jwt';
import * as api from '../src/api';

/**
 * T111 fraud / flag scenarios — drives the pre-create fraud engine and the admin
 * flag-review surface at the API level against the docker-compose.e2e.yml stack
 * (same seam as T107–T110). All three flag mechanisms are fully wired backend-
 * side (FraudPreCheckService + AdminFlagsController + AccountFlagChecker), so
 * this task adds test coverage only — no production source changes.
 *
 * Covers 03 §7 (fraud/flag flows) and §8.2 (admin flag review):
 *   1. Price deviation → FLAGGED + PRICE_DEVIATION flag → admin approve → CREATED.
 *   2. Price deviation → FLAGGED → admin reject → CANCELLED_ADMIN.
 *   3. High volume → FLAGGED + HIGH_VOLUME flag (on the admin review queue).
 *   4. Account flag → new transaction blocked (ACCOUNT_FLAGGED); admin reject of
 *      the account flag lifts the fund-flow block.
 *
 * Levers (no new fake-sidecar surface needed — a FLAGGED transaction makes no
 * sidecar calls): the seeded ItemPriceCache pins the market price at the listing
 * price (100), so a 300 listing deviates 200% > the 100% price_deviation_threshold;
 * high_volume_amount_threshold is dropped via SystemSetting so one prior
 * transaction trips the rolling-window rule; the account flag is inserted directly.
 */

const REVIEW_NOTE = 'E2E fraud-flag review — automated reason text.';

// A listing price that deviates > price_deviation_threshold (1.0 = 100%) from the
// seeded market price (100): |300-100|/100 = 2.0. Within [min,max] = [1,10000].
const DEVIATING_PRICE = '300.00';

test.afterAll(async () => {
  await closePool();
});

function createBody(price: string): api.CreateTransactionBody {
  return {
    itemAssetId: seed.itemAssetId,
    stablecoin: 'USDT',
    price,
    // 1h = 60 min, within the e2e PAYMENT_TIMEOUT_MIN/MAX (15/60).
    paymentTimeoutHours: 1,
    buyerIdentificationMethod: 'STEAM_ID',
    buyerSteamId: seed.buyerSteamId,
    sellerWalletAddress: seed.sellerPayoutAddress,
  };
}

function adminToken(): string {
  return mintAccessToken({
    userId: seed.adminId,
    steamId: seed.adminSteamId,
    role: 'super_admin',
  });
}

/** Seed → seller lists at a deviating price → PRICE_DEVIATION pre-create flag.
 *  Returns the flagged transaction id (status asserted FLAGGED before return). */
async function createPriceDeviationFlag(sellerToken: string): Promise<string> {
  const create = await api.createTransaction(sellerToken, createBody(DEVIATING_PRICE));
  expect(create.ok, `create failed: ${JSON.stringify(create.body)}`).toBeTruthy();
  const created = api.unwrap(create.body);
  // Pre-create flag: status FLAGGED, flagReason names the rule (07 §7.2).
  expect(created.status, JSON.stringify(created)).toBe('FLAGGED');
  expect(created.flagReason).toBe('PRICE_DEVIATION');
  return String(created.id);
}

test('price deviation → FLAGGED + PRICE_DEVIATION flag → admin approve → CREATED', async () => {
  await seedHappyPath();
  const sellerToken = mintAccessToken({ userId: seed.sellerId, steamId: seed.sellerSteamId });

  const txId = await createPriceDeviationFlag(sellerToken);

  // 06 §3.12 — a FLAGGED transaction always carries a matching PENDING flag row.
  const flag = await getFlagForTransaction(txId);
  expect(flag, 'no FraudFlag row staged for the flagged transaction').not.toBeNull();
  expect(flag?.scope).toBe('TRANSACTION_PRE_CREATE');
  expect(flag?.type).toBe('PRICE_DEVIATION');
  expect(flag?.status).toBe('PENDING');

  // 03 §8.2 step 1 — the flag lands on the admin transaction-flag queue (AD2).
  await ensureAdmin();
  const token = adminToken();
  const queue = await api.listFlags(token, {
    reviewStatus: 'PENDING',
    scope: 'TRANSACTION_PRE_CREATE',
  });
  expect(queue.ok, `list flags failed: ${JSON.stringify(queue.body)}`).toBeTruthy();
  const queued = (api.unwrap(queue.body).items as Array<Record<string, unknown>>) ?? [];
  const mine = queued.find((f) => String(f.transactionId) === txId);
  expect(mine, `flag for ${txId} not on the review queue`).toBeTruthy();
  expect(mine?.type).toBe('PRICE_DEVIATION');

  // 03 §8.2 "İşleme Devam Et" — approve promotes FLAGGED → CREATED (07 §9.4).
  const approve = await api.approveFlag(token, flag!.id, REVIEW_NOTE);
  expect(approve.ok, `approve failed: ${JSON.stringify(approve.body)}`).toBeTruthy();
  const result = api.unwrap(approve.body);
  expect(result.reviewStatus).toBe('APPROVED');
  expect(result.transactionStatus).toBe('CREATED');

  // The transition commits synchronously: the seller now sees CREATED and the
  // flag is APPROVED.
  const after = await api.getTransaction(sellerToken, txId);
  expect(api.unwrap(after.body).status).toBe('CREATED');
  expect((await getFlagForTransaction(txId))?.status).toBe('APPROVED');
});

test('price deviation → FLAGGED → admin reject → CANCELLED_ADMIN', async () => {
  await seedHappyPath();
  const sellerToken = mintAccessToken({ userId: seed.sellerId, steamId: seed.sellerSteamId });

  const txId = await createPriceDeviationFlag(sellerToken);
  const flag = await getFlagForTransaction(txId);
  expect(flag?.status).toBe('PENDING');

  await ensureAdmin();
  const token = adminToken();

  // 03 §8.2 "İptal Et" — reject cancels the transaction (FLAGGED → CANCELLED_ADMIN, 07 §9.5).
  const reject = await api.rejectFlag(token, flag!.id, REVIEW_NOTE);
  expect(reject.ok, `reject failed: ${JSON.stringify(reject.body)}`).toBeTruthy();
  const result = api.unwrap(reject.body);
  expect(result.reviewStatus).toBe('REJECTED');
  expect(result.transactionStatus).toBe('CANCELLED_ADMIN');

  const after = await api.getTransaction(sellerToken, txId);
  expect(api.unwrap(after.body).status).toBe('CANCELLED_ADMIN');
  expect((await getFlagForTransaction(txId))?.status).toBe('REJECTED');
});

test('high volume → FLAGGED + HIGH_VOLUME flag on the admin queue', async () => {
  await seedHappyPath();
  const sellerToken = mintAccessToken({ userId: seed.sellerId, steamId: seed.sellerSteamId });

  // Drop the rolling-window amount threshold so a single prior transaction
  // (TotalAmount ≈ 102 for a 100 listing) trips the rule on the next create. The
  // e2e default is 5000; restore it afterwards so later suites are unaffected.
  const original = await getSystemSetting('high_volume_amount_threshold');
  await setSystemSetting('high_volume_amount_threshold', '50');
  try {
    // tx1 — empty rolling window → CREATED (not yet over threshold).
    const first = await api.createTransaction(sellerToken, createBody(seed.price));
    expect(first.ok, `first create failed: ${JSON.stringify(first.body)}`).toBeTruthy();
    expect(api.unwrap(first.body).status).toBe('CREATED');

    // tx2 — the window now holds tx1 (~102 > 50) → HIGH_VOLUME pre-create flag.
    // Listed at the market price so PRICE_DEVIATION (higher priority) stays clear.
    const second = await api.createTransaction(sellerToken, createBody(seed.price));
    expect(second.ok, `second create failed: ${JSON.stringify(second.body)}`).toBeTruthy();
    const flagged = api.unwrap(second.body);
    expect(flagged.status, JSON.stringify(flagged)).toBe('FLAGGED');
    expect(flagged.flagReason).toBe('HIGH_VOLUME');
    const txId = String(flagged.id);

    const flag = await getFlagForTransaction(txId);
    expect(flag?.scope).toBe('TRANSACTION_PRE_CREATE');
    expect(flag?.type).toBe('HIGH_VOLUME');
    expect(flag?.status).toBe('PENDING');

    // The high-volume flag is reviewable on the same admin queue (03 §8.2).
    await ensureAdmin();
    const queue = await api.listFlags(adminToken(), {
      reviewStatus: 'PENDING',
      type: 'HIGH_VOLUME',
    });
    expect(queue.ok, `list flags failed: ${JSON.stringify(queue.body)}`).toBeTruthy();
    const queued = (api.unwrap(queue.body).items as Array<Record<string, unknown>>) ?? [];
    expect(
      queued.some((f) => String(f.transactionId) === txId),
      `${txId} not on the HIGH_VOLUME queue`,
    ).toBeTruthy();
  } finally {
    await setSystemSetting('high_volume_amount_threshold', original ?? '5000.0');
  }
});

test('account flag → new transaction blocked (ACCOUNT_FLAGGED); reject lifts the block', async () => {
  await seedHappyPath();
  await ensureAdmin();
  const sellerToken = mintAccessToken({ userId: seed.sellerId, steamId: seed.sellerSteamId });

  // 03 §7.3/§7.4 — an active ACCOUNT_LEVEL flag blocks the user from starting any
  // new transaction (the fund-flow restriction; AccountFlagChecker → eligibility).
  const flagId = await insertAccountFlag(seed.sellerId);

  const blocked = await api.createTransaction(sellerToken, createBody(seed.price));
  expect(blocked.status, `expected 422, body: ${JSON.stringify(blocked.body)}`).toBe(422);
  const errorCode = ((blocked.body as Record<string, unknown>)?.error as Record<string, unknown>)
    ?.code;
  expect(errorCode).toBe('ACCOUNT_FLAGGED');

  // Admin rejects the account flag (03 §7.3 step 5 "flag kaldırma") → block lifts.
  const reject = await api.rejectFlag(adminToken(), flagId, REVIEW_NOTE);
  expect(reject.ok, `reject failed: ${JSON.stringify(reject.body)}`).toBeTruthy();
  expect(api.unwrap(reject.body).reviewStatus).toBe('REJECTED');

  // With no active account flag, the same seller can now create a transaction.
  const allowed = await api.createTransaction(sellerToken, createBody(seed.price));
  expect(allowed.ok, `create after unblock failed: ${JSON.stringify(allowed.body)}`).toBeTruthy();
  expect(api.unwrap(allowed.body).status).toBe('CREATED');
});
