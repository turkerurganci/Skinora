import { e2eConfig } from './config';

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
  sellerWalletAddress: string;
}

export function createTransaction(token: string, body: CreateTransactionBody): Promise<ApiResult> {
  return call('POST', '/api/v1/transactions', token, body);
}

export function acceptTransaction(
  token: string,
  id: string,
  refundWalletAddress: string,
): Promise<ApiResult> {
  return call('POST', `/api/v1/transactions/${id}/accept`, token, { refundWalletAddress });
}

export function getTransaction(token: string, id: string): Promise<ApiResult> {
  return call('GET', `/api/v1/transactions/${id}`, token);
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

/** Simulate the buyer's on-chain payment via the fake sidecar control surface. */
export async function payViaFake(transactionId: string): Promise<ApiResult> {
  const res = await fetch(`${e2eConfig.fakeUrl}/__e2e/payment/pay`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ transactionId }),
  });
  let json: unknown = null;
  try {
    json = await res.json();
  } catch {
    json = null;
  }
  return { status: res.status, ok: res.ok, body: json };
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

/** Poll until the transaction reaches a post-payment state in which the buyer's
 *  payment is in custody AND an admin cancel still triggers a refund —
 *  PAYMENT_RECEIVED or TRADE_OFFER_SENT_TO_BUYER, i.e. before ITEM_DELIVERED
 *  (02 §7 / 03 §8.7). In these states a *user* cancel must be rejected with
 *  PAYMENT_ALREADY_SENT. Accepting either state makes the test race-free against
 *  the per-minute delivery-dispatch job: catching PAYMENT_RECEIVED early leaves
 *  a wide window, and a brief slip to TRADE_OFFER_SENT_TO_BUYER is still
 *  refundable. Throws if the flow advances to ITEM_DELIVERED or a terminal
 *  state first (the drive overran the cancellable window). */
export async function pollUntilRefundableCancel(
  token: string,
  id: string,
  opts?: { timeoutMs?: number; intervalMs?: number },
): Promise<string> {
  const deadline = Date.now() + (opts?.timeoutMs ?? 90_000);
  const interval = opts?.intervalMs ?? 1_000;
  let last: string | undefined;
  while (Date.now() < deadline) {
    const r = await getTransaction(token, id);
    last = statusOf(r.body);
    if (last === 'PAYMENT_RECEIVED' || last === 'TRADE_OFFER_SENT_TO_BUYER') return last;
    if (
      last === 'ITEM_DELIVERED' ||
      last === 'COMPLETED' ||
      last === 'FLAGGED' ||
      (last !== undefined && last.startsWith('CANCELLED'))
    ) {
      throw new Error(`transaction ${id} advanced to ${last} before a refundable-cancel state`);
    }
    await new Promise((res) => setTimeout(res, interval));
  }
  throw new Error(`timeout awaiting a refundable-cancel state for ${id} (last status=${last})`);
}
