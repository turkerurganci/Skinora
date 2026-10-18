import type { Request, Response } from 'express';
import { sameAddress } from '../address/canonicalAddress.js';
import { checksummedEvmAddress } from '../address/evmAddress.js';
import type { DepositRegistry, MonitorCadence } from '../monitor/DepositRegistry.js';
import type { StablecoinAllowlist, StablecoinSymbol } from '../monitor/tokenRules.js';
import {
  PostCancelMonitorStates,
  type PostCancelMonitorState,
} from '../webhook/WebhookPayloads.js';

/**
 * The monitor endpoints of the per-network contract (05 §3.3a "Uçlar") — the
 * TRON service's request and response shapes (T71, T75, T139), so the
 * backend's one client serves both networks.
 *
 * <para>
 * One rule TRON does not have: the expected contract must be THIS service's
 * contract for that symbol (400 `EXPECTED_CONTRACT_MISMATCH`). A deposit
 * watched for another contract would classify the buyer's real USDT payment
 * as a wrong token and refund it; a backend whose token setting disagrees
 * with the service's is refused rather than followed.
 * </para>
 */

const SYMBOLS: ReadonlySet<string> = new Set(['USDT', 'USDC']);
const CADENCES: ReadonlySet<string> = new Set(['PAYMENT', 'HOLDING']);
const POST_CANCEL_STATES: ReadonlySet<string> = new Set([
  PostCancelMonitorStates.PostCancel24h,
  PostCancelMonitorStates.PostCancel7d,
  PostCancelMonitorStates.PostCancel30d,
]);

function pick(value: unknown): string | null {
  return typeof value === 'string' && value.trim() !== '' ? value.trim() : null;
}

function bad(res: Response, error: string, message: string): void {
  res.status(400).json({ error, message });
}

interface Common {
  address: string;
  paymentAddressId: string;
  transactionId: string;
  expectedContract: string;
  expectedSymbol: StablecoinSymbol;
  /** T158 (h) — absent/null for a deposit allocated before T158. */
  watchFromBlock: bigint | null;
}

const BLOCK_NUMBER = /^(?:0|[1-9]\d{0,18})$/;

/**
 * T158 (h) — `watchFromBlock`: the block the backend persisted for the
 * deposit (`PaymentAddress.MonitorFromBlock`), a decimal string (a JSON
 * number is accepted too). Absent or null = not known (deposit allocated
 * before T158). Anything else is a bad body: following a garbled start block
 * could skip the payment.
 */
function watchFromBlockOf(value: unknown): bigint | null | 'invalid' {
  if (value === undefined || value === null) return null;
  if (typeof value === 'number' && Number.isSafeInteger(value) && value >= 0) return BigInt(value);
  if (typeof value === 'string' && BLOCK_NUMBER.test(value)) return BigInt(value);
  return 'invalid';
}

/** Shared validation of the start bodies; answers 400 itself and returns null on a bad body. */
function common(
  body: Record<string, unknown>,
  allowlist: StablecoinAllowlist,
  res: Response,
  invalidCode: string,
): Common | null {
  const address = pick(body.address);
  const paymentAddressId = pick(body.paymentAddressId);
  const transactionId = pick(body.transactionId);
  const expectedContract = pick(body.expectedContract);
  const expectedSymbol = pick(body.expectedSymbol);
  if (!address || !paymentAddressId || !transactionId || !expectedContract || !expectedSymbol) {
    bad(
      res,
      invalidCode,
      'Required fields: address, paymentAddressId, transactionId, expectedContract, expectedSymbol.',
    );
    return null;
  }
  if (checksummedEvmAddress(address) === null) {
    bad(res, invalidCode, 'address is not an Ethereum address.');
    return null;
  }
  const watchFromBlock = watchFromBlockOf(body.watchFromBlock);
  if (watchFromBlock === 'invalid') {
    bad(res, invalidCode, 'watchFromBlock, when sent, must be a non-negative block number.');
    return null;
  }
  if (!SYMBOLS.has(expectedSymbol)) {
    bad(res, 'UNSUPPORTED_SYMBOL', 'expectedSymbol must be one of: USDT, USDC');
    return null;
  }
  const configured = allowlist[expectedSymbol as StablecoinSymbol];
  if (configured === '' || !sameAddress(configured, expectedContract)) {
    bad(
      res,
      'EXPECTED_CONTRACT_MISMATCH',
      `expectedContract is not this service's ${expectedSymbol} contract${configured === '' ? ' (none configured)' : ''}.`,
    );
    return null;
  }
  return {
    address,
    paymentAddressId,
    transactionId,
    expectedContract,
    expectedSymbol: expectedSymbol as StablecoinSymbol,
    watchFromBlock,
  };
}

/** `POST /api/monitor/start` → 200 `{ acknowledged, started, cadence, address }`. Idempotent. */
export function startMonitorHandler(registry: DepositRegistry, allowlist: StablecoinAllowlist) {
  return (req: Request, res: Response): void => {
    const body = (req.body ?? {}) as Record<string, unknown>;
    const opts = common(body, allowlist, res, 'INVALID_MONITOR_REQUEST');
    if (!opts) return;
    let cadence: MonitorCadence = 'PAYMENT';
    if (body.cadence !== undefined) {
      if (typeof body.cadence !== 'string' || !CADENCES.has(body.cadence)) {
        bad(res, 'UNSUPPORTED_CADENCE', 'cadence must be one of: PAYMENT, HOLDING');
        return;
      }
      cadence = body.cadence as MonitorCadence;
    }
    const result = registry.startActive({ ...opts, cadence });
    res.status(200).json({
      acknowledged: true,
      started: result.started,
      cadence: result.cadence,
      address: opts.address,
    });
  };
}

/** `POST /api/monitor/stop` → 200 `{ acknowledged, stopped, address }`. */
export function stopMonitorHandler(registry: DepositRegistry) {
  return (req: Request, res: Response): void => {
    const address = pick((req.body as Record<string, unknown> | undefined)?.address);
    if (!address) {
      bad(res, 'INVALID_MONITOR_REQUEST', 'Required field: address.');
      return;
    }
    res
      .status(200)
      .json({ acknowledged: true, stopped: registry.stopActive(address).stopped, address });
  };
}

function isoDate(value: string | null): Date | null {
  if (value === null) return null;
  const d = new Date(value);
  return Number.isNaN(d.getTime()) ? null : d;
}

/** `POST /api/monitor/post-cancel-start` → 200 `{ acknowledged, started, state, address }`. */
export function postCancelStartHandler(registry: DepositRegistry, allowlist: StablecoinAllowlist) {
  return (req: Request, res: Response): void => {
    const body = (req.body ?? {}) as Record<string, unknown>;
    if (!pick(body.cancelledAt)) {
      bad(
        res,
        'INVALID_POST_CANCEL_REQUEST',
        'Required fields: address, paymentAddressId, transactionId, expectedContract, expectedSymbol, cancelledAt.',
      );
      return;
    }
    const opts = common(body, allowlist, res, 'INVALID_POST_CANCEL_REQUEST');
    if (!opts) return;
    const cancelledAt = isoDate(pick(body.cancelledAt));
    if (!cancelledAt) {
      bad(res, 'INVALID_CANCELLED_AT', 'cancelledAt must be a valid ISO-8601 timestamp.');
      return;
    }
    const initialStateRaw = pick(body.initialState);
    if (initialStateRaw !== null && !POST_CANCEL_STATES.has(initialStateRaw)) {
      bad(
        res,
        'INVALID_INITIAL_STATE',
        `initialState must be one of: ${[...POST_CANCEL_STATES].join(', ')}`,
      );
      return;
    }
    const expiresRaw = pick(body.initialStateExpiresAt);
    const initialStateExpiresAt = isoDate(expiresRaw);
    if (expiresRaw !== null && !initialStateExpiresAt) {
      bad(
        res,
        'INVALID_STATE_EXPIRES_AT',
        'initialStateExpiresAt must be a valid ISO-8601 timestamp.',
      );
      return;
    }
    const result = registry.startPostCancel({
      ...opts,
      cancelledAt,
      initialState: (initialStateRaw as PostCancelMonitorState | null) ?? undefined,
      initialStateExpiresAt: initialStateExpiresAt ?? undefined,
    });
    res.status(200).json({
      acknowledged: true,
      started: result.started,
      state: result.state,
      address: opts.address,
    });
  };
}

/** `POST /api/monitor/post-cancel-stop` → 200 `{ acknowledged, stopped, address }`. */
export function postCancelStopHandler(registry: DepositRegistry) {
  return (req: Request, res: Response): void => {
    const address = pick((req.body as Record<string, unknown> | undefined)?.address);
    if (!address) {
      bad(res, 'INVALID_POST_CANCEL_REQUEST', 'Required field: address.');
      return;
    }
    res
      .status(200)
      .json({ acknowledged: true, stopped: registry.stopPostCancel(address).stopped, address });
  };
}
