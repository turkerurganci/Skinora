import { test, expect } from '@playwright/test';
import { seedHappyPath, ensureAdmin, seed, closePool, getFlagForTransaction } from '../src/db';
import { mintAccessToken } from '../src/jwt';
import * as api from '../src/api';

/**
 * T113 admin-panel scenarios — drives the back-office admin surface at the API
 * level against the docker-compose.e2e.yml stack (same seam as T107–T112). Every
 * endpoint exercised here shipped fully wired backend-side (AD1 dashboard /
 * AD6–AD7 transaction list+detail in T63, AD8–AD9 settings in T39/T102, AD11–AD14
 * roles in T39/T104, AD18 audit log in T42/T106, AD2/AD4 flags in T54), so this
 * task adds test coverage only — no production source changes.
 *
 * Covers 03 §8 (admin flows):
 *   1. §8.1 — admin access control + dashboard summary.
 *   2. §8.2 — flag review queue → approve → CREATED (summary; the full
 *      approve/reject/account-flag matrix lives in T111 fraud-flags.spec.ts).
 *   3. §8.3 — admin transaction list + detail.
 *   4. §8.4 — platform parameter list + update + validation.
 *   5. §8.6 — role management lifecycle (create → update → delete).
 *   6. §8 — audit log view surfaces an admin action.
 *
 * Login note: Steam OAuth cannot be scripted, so the harness mints the access
 * token directly (src/jwt.ts) — the established e2e login surrogate. AC1's
 * forbidden check (a `user`-role token rejected 403) is the API-level proof of
 * the "admin paneline yönlendirilir" gate (only an admin reaches the panel).
 */

const REVIEW_NOTE = 'E2E T113 admin-flow review — automated reason text.';

// A listing price that deviates > price_deviation_threshold (1.0 = 100%) from the
// seeded market price (100): |300-100|/100 = 2.0. Within [min,max] = [1,10000].
const DEVIATING_PRICE = '300.00';

// A seeded, admin-tunable fraud parameter (06 §3.17 / catalog "fraud_detection")
// with a generic positive-number range rule — safe to round-trip and restore.
const SETTING_KEY = 'high_volume_amount_threshold';
const SETTING_FALLBACK = '5000';

// Per-run role name for the CRUD lifecycle. AdminRole DeleteAsync soft-deletes
// (IsDeleted=1) but UQ_AdminRoles_Name is an UNFILTERED unique index — so a
// deleted role's name is reserved forever and re-inserting it 500s. A unique
// suffix keeps the suite re-runnable against a re-used DB (CI runs once on a
// fresh DB, where any name would do).
const ROLE_TAG = `E2E T113 Role ${Date.now()}`;

test.beforeEach(async () => {
  // See the fraud-flags note: seedHappyPath re-drives the seller's inventory per
  // test, so the reset only has to clear whatever a prior suite left behind.
  await api.resetFakeSteamState();
});

test.afterAll(async () => {
  await api.resetFakeSteamState();
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

function errorCode(body: unknown): unknown {
  return ((body as Record<string, unknown>)?.error as Record<string, unknown>)?.code;
}

/** Read a SettingItem's value off the AD8 list (string), falling back when the
 *  row is unconfigured — so the test can restore the exact prior value. */
async function readSettingValue(token: string, key: string): Promise<string> {
  const list = await api.listSettings(token);
  const found = ((api.unwrap(list.body).settings as Array<Record<string, unknown>>) ?? []).find(
    (s) => s.key === key,
  );
  return found?.value == null ? SETTING_FALLBACK : String(found.value);
}

test('admin access control + dashboard summary (AC1)', async () => {
  await seedHappyPath();
  await ensureAdmin();

  // 03 §8.1 — only an admin reaches the panel. A plain `user` token is
  // authenticated but lacks the admin role claim → 403 (AuthPolicies.AdminAccess).
  const user = mintAccessToken({ userId: seed.buyerId, steamId: seed.buyerSteamId, role: 'user' });
  const forbidden = await api.getAdminDashboard(user);
  expect(forbidden.status, JSON.stringify(forbidden.body)).toBe(403);

  // A flagged listing gives the dashboard a deterministic PENDING flag to count
  // and surface under recentFlags.
  const sellerToken = mintAccessToken({ userId: seed.sellerId, steamId: seed.sellerSteamId });
  const create = await api.createTransaction(sellerToken, createBody(DEVIATING_PRICE));
  expect(create.ok, `create failed: ${JSON.stringify(create.body)}`).toBeTruthy();
  const flaggedTxId = String(api.unwrap(create.body).id);
  expect(api.unwrap(create.body).status).toBe('FLAGGED');

  // 03 §8.1 — admin login lands on the dashboard summary (AD1).
  const dash = await api.getAdminDashboard(adminToken());
  expect(dash.ok, `dashboard failed: ${JSON.stringify(dash.body)}`).toBeTruthy();
  const body = api.unwrap(dash.body);

  const cards = body.summaryCards as Record<string, unknown>;
  expect(cards, 'summaryCards missing').toBeTruthy();
  expect(typeof cards.activeTransactions).toBe('number');
  expect(typeof cards.dailyCompleted).toBe('number');
  expect(typeof cards.weeklyCompleted).toBe('number');
  // Our PENDING flag is counted (07 §9.1 pendingFlags).
  expect(Number(cards.pendingFlags)).toBeGreaterThanOrEqual(1);

  // recentFlags (last 5, newest-first) surfaces our freshly-flagged transaction.
  const recent = (body.recentFlags as Array<Record<string, unknown>>) ?? [];
  expect(
    recent.some((f) => String(f.transactionId) === flaggedTxId),
    `flagged ${flaggedTxId} not in recentFlags`,
  ).toBeTruthy();

  // T138 — the third assertion here read `body.steamAccounts` ("Platform Steam
  // hesaplarının durumu", 03 §8.1) and expected the seeded ACTIVE bot. Both ends
  // of it are gone: v3.0 dropped the field from AdminDashboardResponse, which now
  // carries only summaryCards + recentFlags, and T117 dropped the bot table it
  // counted. The platform runs no Steam accounts to report on (02 §15), so this
  // is a deletion rather than a re-point — there is no P2P successor block, and
  // T136 removed the admin page that rendered it.
});

test('flag review queue → approve → CREATED (AC2 — summary; full matrix in T111)', async () => {
  await seedHappyPath();
  await ensureAdmin();
  const sellerToken = mintAccessToken({ userId: seed.sellerId, steamId: seed.sellerSteamId });

  const create = await api.createTransaction(sellerToken, createBody(DEVIATING_PRICE));
  expect(create.ok, `create failed: ${JSON.stringify(create.body)}`).toBeTruthy();
  const txId = String(api.unwrap(create.body).id);
  expect(api.unwrap(create.body).status).toBe('FLAGGED');

  const flag = await getFlagForTransaction(txId);
  expect(flag?.status).toBe('PENDING');

  const token = adminToken();

  // 03 §8.2 step 1 — the flag is on the admin transaction-flag review queue (AD2).
  const queue = await api.listFlags(token, {
    reviewStatus: 'PENDING',
    scope: 'TRANSACTION_PRE_CREATE',
  });
  expect(queue.ok, `list flags failed: ${JSON.stringify(queue.body)}`).toBeTruthy();
  const queued = (api.unwrap(queue.body).items as Array<Record<string, unknown>>) ?? [];
  expect(
    queued.some((f) => String(f.transactionId) === txId),
    `${txId} not on the review queue`,
  ).toBeTruthy();

  // 03 §8.2 "İşleme Devam Et" — approve promotes FLAGGED → CREATED (AD4).
  const approve = await api.approveFlag(token, flag!.id, REVIEW_NOTE);
  expect(approve.ok, `approve failed: ${JSON.stringify(approve.body)}`).toBeTruthy();
  expect(api.unwrap(approve.body).transactionStatus).toBe('CREATED');

  const after = await api.getTransaction(sellerToken, txId);
  expect(api.unwrap(after.body).status).toBe('CREATED');
});

test('admin transaction list + detail (AC3)', async () => {
  await seedHappyPath();
  await ensureAdmin();
  const sellerToken = mintAccessToken({ userId: seed.sellerId, steamId: seed.sellerSteamId });

  // A normal (non-flagged) listing → CREATED.
  const create = await api.createTransaction(sellerToken, createBody(seed.price));
  expect(create.ok, `create failed: ${JSON.stringify(create.body)}`).toBeTruthy();
  const txId = String(api.unwrap(create.body).id);
  expect(api.unwrap(create.body).status).toBe('CREATED');

  const token = adminToken();

  // AD6 — the transaction appears on the admin list, status-filtered to CREATED.
  const list = await api.listAdminTransactions(token, { status: 'CREATED', pageSize: 100 });
  expect(list.ok, `list failed: ${JSON.stringify(list.body)}`).toBeTruthy();
  const items = (api.unwrap(list.body).items as Array<Record<string, unknown>>) ?? [];
  const row = items.find((t) => String(t.id) === txId);
  expect(row, `${txId} not on the admin transaction list`).toBeTruthy();
  expect(row?.status).toBe('CREATED');
  expect(Number(row?.price)).toBe(Number(seed.price));
  expect((row?.seller as Record<string, unknown>)?.steamId).toBe(seed.sellerSteamId);

  // AD7 — full detail by id (03 §8.3 step 3 "tam bilgi").
  const detail = await api.getAdminTransaction(token, txId);
  expect(detail.ok, `detail failed: ${JSON.stringify(detail.body)}`).toBeTruthy();
  const d = api.unwrap(detail.body);
  expect(String(d.id)).toBe(txId);
  expect(d.status).toBe('CREATED');
  expect(Number(d.price)).toBe(Number(seed.price));
  expect(Array.isArray(d.statusHistory), 'statusHistory missing').toBeTruthy();
  expect(d.adminActions, 'adminActions missing').toBeTruthy();

  // AD7 — unknown id → 404 TRANSACTION_NOT_FOUND.
  const missing = await api.getAdminTransaction(token, '00000000-0000-0000-0000-0000000000aa');
  expect(missing.status, JSON.stringify(missing.body)).toBe(404);
  expect(errorCode(missing.body)).toBe('TRANSACTION_NOT_FOUND');
});

test('platform parameter list + update + validation (AC4)', async () => {
  await ensureAdmin();
  const token = adminToken();

  // AD8 — the tunable parameter list includes our target key (03 §8.4 step 2).
  const list = await api.listSettings(token);
  expect(list.ok, `settings list failed: ${JSON.stringify(list.body)}`).toBeTruthy();
  const settings = (api.unwrap(list.body).settings as Array<Record<string, unknown>>) ?? [];
  const target = settings.find((s) => s.key === SETTING_KEY);
  expect(target, `${SETTING_KEY} not in the settings catalog`).toBeTruthy();
  expect(typeof target?.valueType, 'valueType missing').toBe('string');
  const original = target?.value == null ? SETTING_FALLBACK : String(target.value);

  try {
    // AD9 — a valid new value round-trips (03 §8.4 step 3/4).
    const newValue = '7500';
    const update = await api.updateSetting(token, SETTING_KEY, newValue);
    expect(update.ok, `update failed: ${JSON.stringify(update.body)}`).toBeTruthy();
    expect(api.unwrap(update.body).key).toBe(SETTING_KEY);
    expect(Number(api.unwrap(update.body).value)).toBe(Number(newValue));

    // The change persists — a fresh AD8 read reflects it.
    const persisted = await readSettingValue(token, SETTING_KEY);
    expect(Number(persisted)).toBe(Number(newValue));

    // AD9 — a value that fails the key's numeric type rule is rejected 400.
    const invalid = await api.updateSetting(token, SETTING_KEY, 'not-a-number');
    expect(invalid.status, JSON.stringify(invalid.body)).toBe(400);
    expect(errorCode(invalid.body)).toBe('VALIDATION_ERROR');
  } finally {
    // Restore so later suites observe the e2e default.
    await api.updateSetting(token, SETTING_KEY, original);
  }
});

test('role management lifecycle: create → update → delete (AC5)', async () => {
  await ensureAdmin();
  const token = adminToken();

  // AD11 — roles + the permission catalog (03 §8.6).
  const rolesList = await api.listRoles(token);
  expect(rolesList.ok, `roles list failed: ${JSON.stringify(rolesList.body)}`).toBeTruthy();
  const available =
    (api.unwrap(rolesList.body).availablePermissions as Array<Record<string, unknown>>) ?? [];
  expect(available.length, 'no available permissions in the catalog').toBeGreaterThanOrEqual(2);
  const perm0 = String(available[0].key);
  const perm1 = String(available[1].key);

  const renamed = `${ROLE_TAG} (renamed)`;

  // AD12 — create with a single catalog permission → 201.
  const created = await api.createRole(token, {
    name: ROLE_TAG,
    description: 'E2E T113 role',
    permissions: [perm0],
  });
  expect(created.status, `create failed: ${JSON.stringify(created.body)}`).toBe(201);
  const roleId = String(api.unwrap(created.body).id);
  expect(api.unwrap(created.body).name).toBe(ROLE_TAG);
  expect(api.unwrap(created.body).permissions).toContain(perm0);

  // AD13 — update (rename + widen the permission set) → 200.
  const updated = await api.updateRole(token, roleId, {
    name: renamed,
    permissions: [perm0, perm1],
  });
  expect(updated.ok, `update failed: ${JSON.stringify(updated.body)}`).toBeTruthy();
  expect(api.unwrap(updated.body).name).toBe(renamed);
  const updatedPerms = (api.unwrap(updated.body).permissions as string[]) ?? [];
  expect(updatedPerms).toEqual(expect.arrayContaining([perm0, perm1]));

  // Persisted re-read (not the create/update echo): the AD12/AD13 responses
  // derive `permissions` from the request body (AdminRoleService Create/Update),
  // so a silently-dropped AdminRolePermission insert would still echo back. AD11
  // ListAsync is the only DB-backed permission read — re-read the role off the
  // list and assert the persisted set, proving the assignment round-tripped SQL.
  const afterUpdate = await api.listRoles(token);
  const persistedRole = (
    (api.unwrap(afterUpdate.body).roles as Array<Record<string, unknown>>) ?? []
  ).find((r) => String(r.id) === roleId);
  expect(persistedRole, 'updated role missing from the persisted list').toBeTruthy();
  expect((persistedRole?.permissions as string[]) ?? []).toEqual(
    expect.arrayContaining([perm0, perm1]),
  );

  // AD14 — delete (unassigned → 200) and confirm it is gone from the list.
  const deleted = await api.deleteRole(token, roleId);
  expect(deleted.ok, `delete failed: ${JSON.stringify(deleted.body)}`).toBeTruthy();
  const afterList = await api.listRoles(token);
  const stillThere = (
    (api.unwrap(afterList.body).roles as Array<Record<string, unknown>>) ?? []
  ).some((r) => String(r.id) === roleId);
  expect(stillThere, 'role survived delete').toBeFalsy();
});

test('assign user to role persists + ROLE_HAS_USERS guards delete (AC5 — §8.6 step 4 / AD17)', async () => {
  await seedHappyPath(); // seeds the buyer User we assign below
  await ensureAdmin();
  const token = adminToken();

  // A fresh, empty-permission role to receive the assignment (distinct name so
  // it never collides with the AC5-lifecycle role under the unfiltered UQ).
  const created = await api.createRole(token, {
    name: `${ROLE_TAG} assignable`,
    description: 'E2E T113 AD17 role',
    permissions: [],
  });
  expect(created.status, `create failed: ${JSON.stringify(created.body)}`).toBe(201);
  const roleId = String(api.unwrap(created.body).id);

  try {
    // AD17 — assign the seeded buyer to the role (03 §8.6 step 4 "kullanıcıları
    // rollere atayabilir").
    const assign = await api.assignUserRole(token, seed.buyerId, roleId);
    expect(assign.ok, `assign failed: ${JSON.stringify(assign.body)}`).toBeTruthy();
    expect(String((api.unwrap(assign.body).role as Record<string, unknown>)?.id)).toBe(roleId);

    // Persisted re-read: AD11 ListAsync counts AdminUserRole rows straight from
    // SQL → assignedUserCount proves the assignment persisted (not an echo).
    const list = await api.listRoles(token);
    const row = ((api.unwrap(list.body).roles as Array<Record<string, unknown>>) ?? []).find(
      (r) => String(r.id) === roleId,
    );
    expect(row, 'assigned role missing from the list').toBeTruthy();
    expect(Number(row?.assignedUserCount)).toBeGreaterThanOrEqual(1);

    // AD14 — a role with an active assignment is guarded: delete → 422.
    const blocked = await api.deleteRole(token, roleId);
    expect(blocked.status, JSON.stringify(blocked.body)).toBe(422);
    expect(errorCode(blocked.body)).toBe('ROLE_HAS_USERS');
  } finally {
    // Clear the assignment (roleId=null tombstones it → count 0) so the role is
    // deletable; seedHappyPath's AdminUserRoles purge keeps later re-runs clean.
    await api.assignUserRole(token, seed.buyerId, null);
    await api.deleteRole(token, roleId);
  }
});

test('audit log view surfaces an admin action (AC6)', async () => {
  await ensureAdmin();
  const token = adminToken();

  // Produce a deterministic admin-action audit row via AD9 (a settings change
  // stages a SYSTEM_SETTING_CHANGED row with EntityId = the key).
  const original = await readSettingValue(token, SETTING_KEY);
  try {
    const upd = await api.updateSetting(token, SETTING_KEY, '8000');
    expect(upd.ok, `update failed: ${JSON.stringify(upd.body)}`).toBeTruthy();

    // AD18 — the audit log surfaces the change. search=<key> matches the row's
    // EntityId; category=ADMIN_ACTION narrows to the admin queue (07 §9.19).
    const logs = await api.listAuditLogs(token, {
      search: SETTING_KEY,
      category: 'ADMIN_ACTION',
      pageSize: 50,
    });
    expect(logs.ok, `audit logs failed: ${JSON.stringify(logs.body)}`).toBeTruthy();
    const rows = (api.unwrap(logs.body).items as Array<Record<string, unknown>>) ?? [];
    const mine = rows.find((a) => a.action === 'SYSTEM_SETTING_CHANGED');
    expect(mine, 'no SYSTEM_SETTING_CHANGED row in the audit log').toBeTruthy();
    expect(mine?.category).toBe('ADMIN_ACTION');
    // Actor resolves to the seeded admin (07 §9.19 actor hydration via Users).
    expect((mine?.actor as Record<string, unknown>)?.steamId).toBe(seed.adminSteamId);
  } finally {
    await api.updateSetting(token, SETTING_KEY, original);
  }
});
