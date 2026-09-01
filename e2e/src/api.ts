import { e2eConfig } from './config';
import { seed } from './db';

export interface ApiResult {
  status: number;
  ok: boolean;
  body: unknown;
}

async function call(
  method: string,
  path: string,
  token?: string,
  body?: unknown,
): Promise<ApiResult> {
  const res = await fetch(`${e2eConfig.baseUrl}${path}`, {
    method,
    headers: {
      'Content-Type': 'application/json',
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
    },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  let json: unknown = null;
  try {
    json = await res.json();
  } catch {
    json = null;
  }
  return { status: res.status, ok: res.ok, body: json };
}

/** Backend wraps responses in ApiResponse<T> = { success, data, error }. */
export function unwrap(body: unknown): Record<string, unknown> {
  const b = (body ?? {}) as Record<string, unknown>;
  return (b.data ?? b) as Record<string, unknown>;
}

export interface CreateTransactionBody {
  itemAssetId: string;
  stablecoin: string;
  price: string;
  paymentTimeoutHours: number;
  buyerIdentificationMethod: string;
  buyerSteamId: string;
}

export function createTransaction(token: string, body: CreateTransactionBody): Promise<ApiResult> {
  return call('POST', '/api/v1/transactions', token, body);
}

/**
 * T119a — `steamTradeUrl` became mandatory in v3.0 (07 §7.6): in the P2P model
 * the seller ships the item straight to this address. Defaults to the seeded
 * buyer's URL so the existing scenarios keep reading as "the buyer accepts";
 * tests that accept as a different identity pass their own.
 */
export function acceptTransaction(
  token: string,
  id: string,
  refundWalletAddress: string,
  steamTradeUrl: string = seed.buyerTradeUrl,
): Promise<ApiResult> {
  return call('POST', `/api/v1/transactions/${id}/accept`, token, {
    refundWalletAddress,
    steamTradeUrl,
  });
}

export function getTransaction(token: string, id: string): Promise<ApiResult> {
  return call('GET', `/api/v1/transactions/${id}`, token);
}

/** T123 — POST /transactions/:id/confirm-ready (07 §7.6a). The SELLER asserts
 *  they are ready to send; the platform verifies the claim itself, so there is
 *  no request body. On success ACCEPTED → SELLER_CONFIRMED: the payment window
 *  is armed, the deposit address is revealed to the buyer, the buyer's inventory
 *  baseline is captured (02 §9.2) and — since T139 — the payment monitor is
 *  armed inside the same SaveChanges.
 *
 *  This is the P2P step with no custody-era counterpart. The platform used to
 *  pull the item into escrow here; now it only checks that the seller can still
 *  send it. Three gates run against the fake — the item is still in the seller's
 *  inventory and tradeable (409 ITEM_NO_LONGER_AVAILABLE / 422 INVENTORY_PRIVATE
 *  / 503 STEAM_UNAVAILABLE), and the buyer's Mobile Authenticator is live (403
 *  BUYER_MOBILE_AUTHENTICATOR_INACTIVE). */
export function confirmReady(token: string, id: string): Promise<ApiResult> {
  return call('POST', `/api/v1/transactions/${id}/confirm-ready`, token);
}

/** T126 — POST /transactions/:id/confirm-receipt (07 §7.6b). The BUYER states
 *  the item arrived; no request body. PAYMENT_RECEIVED → ITEM_DELIVERED, and in
 *  the e2e stack it is the only producer of that transition that advances on its
 *  own: 02 §9.2's other route (inventory evidence) is held shut by the
 *  `delivery.inventory_evidence_auto_release_enabled` launch gate, whose seed
 *  default is false (DEPLOY_RUNBOOK §H). Buyer confirmation is explicitly NOT
 *  subject to that gate — it runs against the buyer's own interest, so there is
 *  no incentive to claim it falsely.
 *
 *  Idempotent by 07 §7.6b: a repeat on an already-delivered transaction answers
 *  200 with the same state rather than 409. */
export function confirmReceipt(token: string, id: string): Promise<ApiResult> {
  return call('POST', `/api/v1/transactions/${id}/confirm-receipt`, token);
}

/** User-facing cancel — POST /transactions/:id/cancel (07 §7.7). The caller
 *  must be the seller or buyer; reason is required (>=10 chars). */
export function cancelTransaction(token: string, id: string, reason: string): Promise<ApiResult> {
  return call('POST', `/api/v1/transactions/${id}/cancel`, token, { reason });
}

/** Admin cancel — POST /admin/transactions/:id/cancel (07 §9.20). Requires the
 *  CANCEL_TRANSACTIONS permission, satisfied by a super_admin role claim. */
export function adminCancelTransaction(
  token: string,
  id: string,
  reason: string,
): Promise<ApiResult> {
  return call('POST', `/api/v1/admin/transactions/${id}/cancel`, token, { reason });
}

/** AD2 — GET /admin/flags (07 §9.2). Lists fraud flags for the admin review
 *  queue; the optional filters back the 03 §8.2 scope/status controls. Requires
 *  the VIEW_FLAGS permission, satisfied by a super_admin role claim. */
export function listFlags(
  token: string,
  query?: {
    scope?: string;
    type?: string;
    reviewStatus?: string;
    page?: number;
    pageSize?: number;
  },
): Promise<ApiResult> {
  const params = new URLSearchParams();
  if (query?.scope) params.set('scope', query.scope);
  if (query?.type) params.set('type', query.type);
  if (query?.reviewStatus) params.set('reviewStatus', query.reviewStatus);
  if (query?.page) params.set('page', String(query.page));
  if (query?.pageSize) params.set('pageSize', String(query.pageSize));
  const qs = params.toString();
  return call('GET', `/api/v1/admin/flags${qs ? `?${qs}` : ''}`, token);
}

/** AD4 — POST /admin/flags/:id/approve (07 §9.4). For a transaction-scoped flag
 *  this promotes the linked transaction FLAGGED → CREATED and starts the accept
 *  timeout. Requires MANAGE_FLAGS (super_admin claim). */
export function approveFlag(token: string, id: string, note?: string): Promise<ApiResult> {
  return call('POST', `/api/v1/admin/flags/${id}/approve`, token, { note: note ?? null });
}

/** AD5 — POST /admin/flags/:id/reject (07 §9.5). For a transaction-scoped flag
 *  this moves the linked transaction FLAGGED → CANCELLED_ADMIN; for an
 *  account-level flag it just marks the flag REJECTED (lifting the fund-flow
 *  block). Requires MANAGE_FLAGS (super_admin claim). */
export function rejectFlag(token: string, id: string, note?: string): Promise<ApiResult> {
  return call('POST', `/api/v1/admin/flags/${id}/reject`, token, { note: note ?? null });
}

/** AD19b — POST /admin/transactions/:id/emergency-hold (07 §9.21 / 03 §8.8).
 *  Freezes an active transaction in its current state: stamps IsOnHold=true +
 *  TimeoutFreezeReason=EMERGENCY_HOLD and captures the active-phase deadline
 *  remainder, so no automatic step advances. `reason` is required (>=10 chars).
 *  Requires the EMERGENCY_HOLD permission, satisfied by a super_admin claim. */
export function applyEmergencyHold(token: string, id: string, reason: string): Promise<ApiResult> {
  return call('POST', `/api/v1/admin/transactions/${id}/emergency-hold`, token, { reason });
}

/** AD19c — POST /admin/transactions/:id/release-hold (07 §9.22 / 03 §8.8).
 *  RESUME lifts the hold and resumes the frozen timeout at the captured remainder
 *  (status returns to the pre-hold value); CANCEL releases then admin-cancels
 *  (→ CANCELLED_ADMIN with the AD19 refund fan-out) — except when the pre-hold
 *  status was ITEM_DELIVERED, where CANCEL is rejected 422
 *  CANNOT_CANCEL_DELIVERED_HOLD and only RESUME is permitted. `note` is required
 *  (>=1 char). Same EMERGENCY_HOLD permission as AD19b. */
export function releaseEmergencyHold(
  token: string,
  id: string,
  action: 'RESUME' | 'CANCEL',
  note: string,
): Promise<ApiResult> {
  return call('POST', `/api/v1/admin/transactions/${id}/release-hold`, token, { action, note });
}

// ---------------------------------------------------------------------------
// Admin back-office surface (T113 — 03 §8 admin flows). Every endpoint below is
// gated by an admin authorization policy that a super_admin role claim
// satisfies (PermissionAuthorizationHandler bypass / AuthPolicies.AdminAccess);
// a plain `user` token is rejected 403. T113 adds test coverage only — these
// endpoints shipped wired in T63 (dashboard / tx list+detail), T39+T102
// (settings), T39+T104 (roles) and T42+T106 (audit log).
// ---------------------------------------------------------------------------

/** AD1 — GET /admin/dashboard (07 §9.1 / 03 §8.1). Returns the admin landing
 *  summary: summaryCards (active / pending-flag / daily+weekly-completed
 *  counters) and recentFlags (last 5, newest-first) — that is the whole AD1
 *  body since v3.0 dropped the steamAccounts block (the platform runs no Steam
 *  accounts, 02 §15). Gated by
 *  AuthPolicies.AdminAccess — any admin role; a `user` token is forbidden. */
export function getAdminDashboard(token: string): Promise<ApiResult> {
  return call('GET', '/api/v1/admin/dashboard', token);
}

/** AD6 — GET /admin/transactions (07 §9.6 / 03 §8.3). Paged admin transaction
 *  list with the 03 §8.3 filters (status / date / amount / Steam-id search).
 *  Requires VIEW_TRANSACTIONS (super_admin claim). */
export function listAdminTransactions(
  token: string,
  query?: { status?: string; search?: string; page?: number; pageSize?: number },
): Promise<ApiResult> {
  const params = new URLSearchParams();
  if (query?.status) params.set('status', query.status);
  if (query?.search) params.set('search', query.search);
  if (query?.page) params.set('page', String(query.page));
  if (query?.pageSize) params.set('pageSize', String(query.pageSize));
  const qs = params.toString();
  return call('GET', `/api/v1/admin/transactions${qs ? `?${qs}` : ''}`, token);
}

/** AD7 — GET /admin/transactions/:id (07 §9.7 / 03 §8.3). Full admin
 *  transaction detail (parties + reputation, item, price, statusHistory,
 *  adminActions). 404 TRANSACTION_NOT_FOUND for an unknown id. Same
 *  VIEW_TRANSACTIONS gate as AD6. */
export function getAdminTransaction(token: string, id: string): Promise<ApiResult> {
  return call('GET', `/api/v1/admin/transactions/${id}`, token);
}

/** AD8 — GET /admin/settings (07 §9.8 / 03 §8.4). Lists the admin-tunable
 *  platform parameters (key / value / category / label / valueType). Requires
 *  MANAGE_SETTINGS (super_admin claim). */
export function listSettings(token: string): Promise<ApiResult> {
  return call('GET', '/api/v1/admin/settings', token);
}

/** AD9 — PUT /admin/settings/:key (07 §9.9 / 03 §8.4). Updates one parameter and
 *  stages a SYSTEM_SETTING_CHANGED audit row in the same DB transaction. A value
 *  that fails the key's type/range rule is rejected 400 VALIDATION_ERROR. */
export function updateSetting(
  token: string,
  key: string,
  value: string | null,
): Promise<ApiResult> {
  return call('PUT', `/api/v1/admin/settings/${key}`, token, { value });
}

export interface RoleBody {
  name: string;
  description?: string | null;
  permissions?: string[];
}

/** AD11 — GET /admin/roles (07 §9.11 / 03 §8.6). Lists roles + the available
 *  permission catalog. Requires MANAGE_ROLES (super_admin claim). */
export function listRoles(token: string): Promise<ApiResult> {
  return call('GET', '/api/v1/admin/roles', token);
}

/** AD12 — POST /admin/roles (07 §9.12 / 03 §8.6). Creates a role with a
 *  permission set drawn from the catalog → 201. A duplicate name is rejected
 *  409 ROLE_NAME_EXISTS; an unknown permission key 400 INVALID_PERMISSION. */
export function createRole(token: string, body: RoleBody): Promise<ApiResult> {
  return call('POST', '/api/v1/admin/roles', token, body);
}

/** AD13 — PUT /admin/roles/:id (07 §9.13 / 03 §8.6). Replaces a role's name /
 *  description / permission set → 200. */
export function updateRole(token: string, id: string, body: RoleBody): Promise<ApiResult> {
  return call('PUT', `/api/v1/admin/roles/${id}`, token, body);
}

/** AD14 — DELETE /admin/roles/:id (07 §9.14 / 03 §8.6). Deletes an unassigned
 *  role → 200; a role with assigned users is rejected 422 ROLE_HAS_USERS. */
export function deleteRole(token: string, id: string): Promise<ApiResult> {
  return call('DELETE', `/api/v1/admin/roles/${id}`, token);
}

/** AD17 — PUT /admin/users/:id/role (07 §9.18 / 03 §8.6 step 4). Assigns the
 *  user to `roleId` (or clears the assignment when roleId is null), persisting
 *  an AdminUserRole row; the prior active assignment is tombstoned first. 404
 *  USER_NOT_FOUND / ROLE_NOT_FOUND. Requires MANAGE_ROLES (super_admin claim). */
export function assignUserRole(
  token: string,
  userId: string,
  roleId: string | null,
): Promise<ApiResult> {
  return call('PUT', `/api/v1/admin/users/${userId}/role`, token, { roleId });
}

/** AD18 — GET /admin/audit-logs (07 §9.19 / 03 §8). Paged audit trail with the
 *  category / date / search / transactionId filters. `search` matches the
 *  EntityId substring (a settings key, an entity id) plus the actor/subject
 *  Steam id or display name. Requires VIEW_AUDIT_LOG (super_admin claim). */
export function listAuditLogs(
  token: string,
  query?: {
    category?: string;
    search?: string;
    transactionId?: string;
    page?: number;
    pageSize?: number;
  },
): Promise<ApiResult> {
  const params = new URLSearchParams();
  if (query?.category) params.set('category', query.category);
  if (query?.search) params.set('search', query.search);
  if (query?.transactionId) params.set('transactionId', query.transactionId);
  if (query?.page) params.set('page', String(query.page));
  if (query?.pageSize) params.set('pageSize', String(query.pageSize));
  const qs = params.toString();
  return call('GET', `/api/v1/admin/audit-logs${qs ? `?${qs}` : ''}`, token);
}

// ---------------------------------------------------------------------------
// Platform maintenance / downtime control (T114 — 03 §11 downtime flows). The
// freeze/resume endpoints are gated by MANAGE_SETTINGS, satisfied by a
// super_admin role claim (PermissionAuthorizationHandler bypass); a plain
// `user` token is rejected 403. The public banner read is anonymous. The
// backend shipped wired in WP7 (07 §9.31 / §10.2); T114 adds test coverage only.
// ---------------------------------------------------------------------------

export interface MaintenanceFreezeBody {
  message?: string;
  plannedEnd?: string;
}

/** WP7 — POST /admin/maintenance/freeze (07 §9.31 / 03 §11). Enters a
 *  maintenance/outage window: persists the four platform.maintenance.* banner
 *  settings, bulk-freezes the timeouts of every active transaction in `type`'s
 *  scope and broadcasts the MaintenanceStatusChanged push — all
 *
 *  T138 — the scopes are the P2P ones (TimeoutFreezeReasonScopes):
 *  PLATFORM_MAINTENANCE = every active state; STEAM_OUTAGE = { ACCEPTED,
 *  PAYMENT_RECEIVED } — the two phases whose deadline depends on the platform
 *  being able to READ Steam (the seller's readiness re-check and the delivery
 *  verification window), not on the parties being able to trade;
 *  BLOCKCHAIN_DEGRADATION = { SELLER_CONFIRMED }, the only phase with a
 *  blockchain-bound deadline (PaymentDeadline); PLANNED_MAINTENANCE is
 *  banner-only. All
 *  atomically. Response data = { active, type, message, plannedEnd,
 *  affectedTransactions }. A type outside the activatable set (e.g. NONE) is
 *  rejected 400 VALIDATION_ERROR. */
export function freezeMaintenance(
  token: string,
  type: string,
  body?: MaintenanceFreezeBody,
): Promise<ApiResult> {
  return call('POST', '/api/v1/admin/maintenance/freeze', token, {
    type,
    message: body?.message ?? null,
    plannedEnd: body?.plannedEnd ?? null,
  });
}

/** WP7 — POST /admin/maintenance/resume (07 §9.31 / 03 §11). Leaves the active
 *  maintenance/outage window: resumes the timeouts frozen by the active reason
 *  (each active phase deadline rewritten as now + the captured remainder, 05
 *  §4.4), clears the four platform.maintenance.* settings and broadcasts the
 *  banner-cleared push. Idempotent when nothing is active. Response data =
 *  { active:false, type:null, ..., affectedTransactions }. */
export function resumeMaintenance(token: string): Promise<ApiResult> {
  return call('POST', '/api/v1/admin/maintenance/resume', token);
}

/** P2 — GET /platform/maintenance (07 §10.2 / 03 §11). Anonymous public
 *  read-model behind the C08 maintenance banner: { active, type, message,
 *  plannedEnd }, where type/message/plannedEnd surface as null when inactive.
 *  This is the API-observable form of the user-facing downtime notice — the
 *  MaintenanceStatusChanged realtime push that carries the same state is
 *  SignalR-only (not assertable at the API seam). */
export function getPlatformMaintenance(): Promise<ApiResult> {
  return call('GET', '/api/v1/platform/maintenance');
}

/** POST to a fake-sidecar control endpoint (/__e2e/*). The control surface is
 *  unauthenticated (the caller is the test) and shared across both fake ports. */
async function fakePost(path: string, body: unknown): Promise<ApiResult> {
  const res = await fetch(`${e2eConfig.fakeUrl}${path}`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  });
  let json: unknown = null;
  try {
    json = await res.json();
  } catch {
    json = null;
  }
  return { status: res.status, ok: res.ok, body: json };
}

/** GET from a fake-sidecar control endpoint (/__e2e/*). Same unauthenticated
 *  surface as {@link fakePost}; used to read state back rather than drive it. */
async function fakeGet(path: string): Promise<ApiResult> {
  const res = await fetch(`${e2eConfig.fakeUrl}${path}`);
  let json: unknown = null;
  try {
    json = await res.json();
  } catch {
    json = null;
  }
  return { status: res.status, ok: res.ok, body: json };
}

/** Simulate the buyer's on-chain payment via the fake sidecar control surface.
 *  Defaults to the exact expected amount at eventIndex 0 (the happy path). T110
 *  levers: `amount` drives 03 §5.1 (insufficient) / §5.2 (excess); a non-zero
 *  `eventIndex` posts a distinct second transfer for the §5.5 multi-payment
 *  (the backend treats it as a fresh confirmed payment → full refund because the
 *  transaction already left SELLER_CONFIRMED — AmountValidationService.cs:87
 *  picks the multi-payment branch on STATE, not on amount). */
export function payViaFake(
  transactionId: string,
  opts?: { amount?: string; eventIndex?: number },
): Promise<ApiResult> {
  return fakePost('/__e2e/payment/pay', { transactionId, ...opts });
}

/** 03 §5.3 — simulate a supported-but-wrong TRC-20 stablecoin landing at the
 *  deposit address (USDC when the buyer was billed USDT). Backend queues a
 *  WRONG_TOKEN_REFUND and the transaction stays SELLER_CONFIRMED. */
export function payWrongTokenViaFake(
  transactionId: string,
  opts?: { actualTokenSymbol?: string; amount?: string },
): Promise<ApiResult> {
  return fakePost('/__e2e/payment/wrong-token', { transactionId, ...opts });
}

/** 03 §5.3a — simulate an unsupported token/contract. Backend records a
 *  terminal SPAM_TOKEN_INCOMING audit row; no refund, transaction state
 *  untouched (stays SELLER_CONFIRMED). */
export function paySpamTokenViaFake(
  transactionId: string,
  opts?: { amount?: string },
): Promise<ApiResult> {
  return fakePost('/__e2e/payment/spam-token', { transactionId, ...opts });
}

/** 03 §5.4 — simulate a late buyer transfer at a cancelled transaction's
 *  deposit address (inside the post-cancel monitoring window). Backend queues a
 *  LATE_PAYMENT_REFUND. `monitorState` defaults to POST_CANCEL_24H. */
export function payLateViaFake(
  transactionId: string,
  opts?: { amount?: string; monitorState?: string },
): Promise<ApiResult> {
  return fakePost('/__e2e/payment/late-detected', { transactionId, ...opts });
}

/** One item a test seeds into a fake inventory: a `catalog` template name
 *  (`AK47_REDLINE` / `AWP_ASIIMOV`), explicit fields, or a template with
 *  overrides — `{ catalog: 'AK47_REDLINE', assetId: '777' }` is a SECOND copy
 *  of the same class, which is what a 06 §3.5 count delta is made of. */
export interface FakeInventoryItemSpec {
  catalog?: string;
  assetId?: string;
  classId?: string;
  instanceId?: string;
  name?: string;
  marketHashName?: string;
  type?: string;
  exterior?: string;
  iconUrl?: string;
  tradable?: boolean;
  marketable?: boolean;
}

/** T137 — seed one steamId's Steam inventory on the fake. `items` REPLACES the
 *  whole inventory; `visibility` drives the 08 §2.3 three-valued read (PUBLIC →
 *  200, PRIVATE → 422, UNAVAILABLE → 503, exactly as the real sidecar answers).
 *  Either field may be omitted to leave that half untouched.
 *
 *  A steamId nobody seeds reads as PUBLIC and EMPTY — so a buyer starts with a
 *  zero baseline unless the test says otherwise. */
export function setFakeInventory(
  steamId: string,
  opts: { items?: FakeInventoryItemSpec[]; visibility?: string },
): Promise<ApiResult> {
  return fakePost('/__e2e/steam/inventory', { steamId, ...opts });
}

/** Read the fake's STORED holdings for a steamId (always 200 — this reports the
 *  store, it does not simulate a Steam read). */
export function getFakeInventory(steamId: string): Promise<ApiResult> {
  return fakeGet(`/__e2e/steam/inventory/${steamId}`);
}

/** T137 — simulate the seller→buyer Steam trade the platform never sees (02
 *  §2.1). The asset leaves `fromSteamId` and lands at `toSteamId` under a NEW
 *  asset id (06 §8.4 rotation), which is returned as `newAssetId`; class and
 *  instance are preserved. Call it in the other direction to simulate the
 *  seller pulling the trade back (T129 reversal). */
export function simulateFakeTrade(
  fromSteamId: string,
  toSteamId: string,
  assetId: string,
): Promise<ApiResult> {
  return fakePost('/__e2e/steam/trade', { fromSteamId, toSteamId, assetId });
}

/** T137 — drive the 08 §2.2 MA / trade-hold probe for one steamId.
 *  `active: false` = no mobile authenticator → the accept endpoint answers 403
 *  MOBILE_AUTHENTICATOR_REQUIRED (T119a). Default (undriven) is MA-verified. */
export function setFakeTradeHold(
  steamId: string,
  opts: { active?: boolean; escrowEndDurationSeconds?: number },
): Promise<ApiResult> {
  return fakePost('/__e2e/steam/trade-hold', { steamId, ...opts });
}

/** Drop every driven inventory + trade hold on the fake. Call between scenarios
 *  so one scenario's seeded inventory never leaks into the next. */
export function resetFakeSteamState(): Promise<ApiResult> {
  return fakePost('/__e2e/steam/reset', {});
}

function statusOf(body: unknown): string | undefined {
  const v = unwrap(body).status;
  return typeof v === 'string' ? v : v === undefined ? undefined : String(v);
}

/** Poll the transaction status until it equals `target` (or a CANCELLED_* /
 *  FLAGGED terminal is hit, or timeout). Returns the last observed status. */
export async function pollStatus(
  token: string,
  id: string,
  target: string,
  opts?: { timeoutMs?: number; intervalMs?: number },
): Promise<string> {
  const deadline = Date.now() + (opts?.timeoutMs ?? 240_000);
  const interval = opts?.intervalMs ?? 3_000;
  let last: string | undefined;
  while (Date.now() < deadline) {
    const r = await getTransaction(token, id);
    last = statusOf(r.body);
    if (last === target) return last;
    if (last && (last.startsWith('CANCELLED') || last === 'FLAGGED')) {
      throw new Error(`transaction ${id} reached terminal ${last} while awaiting ${target}`);
    }
    await new Promise((res) => setTimeout(res, interval));
  }
  throw new Error(`timeout awaiting ${target} for ${id} (last status=${last})`);
}

// T138 — `pollUntilRefundableCancel` was removed here, and it has no P2P
// successor. It accepted PAYMENT_RECEIVED *or* TRADE_OFFER_SENT_TO_BUYER because
// a per-minute delivery-dispatch job could slip the transaction from the first
// to the second while the test was looking. In P2P nothing advances
// PAYMENT_RECEIVED on its own: the platform is not a party to the seller→buyer
// trade (02 §2.1), so the state only leaves when the buyer confirms receipt, an
// admin acts, or the delivery deadline expires. The refundable-cancel window is
// therefore exactly PAYMENT_RECEIVED and `pollStatus(..., 'PAYMENT_RECEIVED')`
// says so without a second state to hedge against.

/** Poll the transaction for `durationMs`, asserting the detail endpoint's
 *  projected status never leaves `expected`. The e2e proof that a freeze "holds"
 *  a timeout — no automatic step advances while frozen (03 §8.8 step 6 for an
 *  emergency hold; 03 §11 for a maintenance/outage freeze). What `expected` is
 *  depends on the freeze kind: an emergency hold sets IsOnHold, so the detail
 *  endpoint projects status=EMERGENCY_HOLD (callers pass that); a
 *  maintenance/outage freeze leaves IsOnHold false, so the projection equals the
 *  underlying phase status (callers pass CREATED / ACCEPTED / SELLER_CONFIRMED /
 *  PAYMENT_RECEIVED). Either way a frozen row keeps reporting `expected` across
 *  several DeadlineScannerJob sweeps rather than flipping to a CANCELLED_*
 *  terminal; callers pair this with a DB read of the phase status for the
 *  decisive "not cancelled" assertion. Throws on the first observation of a
 *  different status; returns when the window elapses unchanged. */
export async function assertStatusStable(
  token: string,
  id: string,
  expected: string,
  opts?: { durationMs?: number; intervalMs?: number },
): Promise<void> {
  const deadline = Date.now() + (opts?.durationMs ?? 15_000);
  const interval = opts?.intervalMs ?? 3_000;
  do {
    const r = await getTransaction(token, id);
    const s = statusOf(r.body);
    if (s !== expected) {
      throw new Error(`status left ${expected} → ${s}; expected it to stay frozen`);
    }
    await new Promise((res) => setTimeout(res, interval));
  } while (Date.now() < deadline);
}
