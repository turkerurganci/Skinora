import type { Request, Response } from 'express';
import { logger } from '../logger.js';
import type { MonitorRegistry, MonitorStartOptions } from '../monitor/MonitorRegistry.js';
import type { StablecoinSymbol } from '../monitor/PaymentMonitorRules.js';
import type {
  PostCancelMonitorRegistry,
  PostCancelMonitorStartOptions,
} from '../monitor/PostCancelMonitor.js';
import { PostCancelMonitorStates } from '../webhook/WebhookPayloads.js';
import type { PostCancelMonitorState } from '../webhook/WebhookPayloads.js';

interface StartRequestBody {
  address?: unknown;
  paymentAddressId?: unknown;
  transactionId?: unknown;
  expectedContract?: unknown;
  expectedSymbol?: unknown;
}

interface StopRequestBody {
  address?: unknown;
}

const ALLOWED_SYMBOLS: ReadonlySet<StablecoinSymbol> = new Set(['USDT', 'USDC']);

/**
 * POST /api/monitor/start — backend asks the sidecar to begin watching a
 * deposit address. Idempotent: re-issuing the same address is a no-op and
 * still responds 200 (08 §3.4 polling lifecycle).
 */
export function startMonitorHandler(registry: MonitorRegistry) {
  return (req: Request, res: Response): void => {
    const body = (req.body ?? {}) as StartRequestBody;

    const address = pickString(body.address);
    const paymentAddressId = pickString(body.paymentAddressId);
    const transactionId = pickString(body.transactionId);
    const expectedContract = pickString(body.expectedContract);
    const expectedSymbolRaw = pickString(body.expectedSymbol);

    if (
      !address ||
      !paymentAddressId ||
      !transactionId ||
      !expectedContract ||
      !expectedSymbolRaw
    ) {
      res.status(400).json({
        error: 'INVALID_MONITOR_REQUEST',
        message:
          'Required fields: address, paymentAddressId, transactionId, expectedContract, expectedSymbol.',
      });
      return;
    }

    if (!ALLOWED_SYMBOLS.has(expectedSymbolRaw as StablecoinSymbol)) {
      res.status(400).json({
        error: 'UNSUPPORTED_SYMBOL',
        message: `expectedSymbol must be one of: ${[...ALLOWED_SYMBOLS].join(', ')}`,
      });
      return;
    }

    const options: MonitorStartOptions = {
      address,
      paymentAddressId,
      transactionId,
      expectedContract,
      expectedSymbol: expectedSymbolRaw as StablecoinSymbol,
    };

    try {
      const result = registry.start(options);
      logger.info(
        {
          address,
          transactionId,
          paymentAddressId,
          expectedSymbol: options.expectedSymbol,
          started: result.started,
        },
        'Monitor start request handled',
      );
      res.status(200).json({ acknowledged: true, started: result.started, address });
    } catch (err) {
      logger.error({ err: (err as Error).message, address, transactionId }, 'Monitor start failed');
      res.status(500).json({ error: 'MONITOR_START_FAILED', message: (err as Error).message });
    }
  };
}

/**
 * POST /api/monitor/stop — backend asks the sidecar to stop watching a
 * deposit address. Returns 200 regardless of whether the address was
 * actually being monitored (idempotent from the backend's perspective).
 */
export function stopMonitorHandler(registry: MonitorRegistry) {
  return (req: Request, res: Response): void => {
    const body = (req.body ?? {}) as StopRequestBody;
    const address = pickString(body.address);

    if (!address) {
      res.status(400).json({
        error: 'INVALID_MONITOR_REQUEST',
        message: 'Required field: address.',
      });
      return;
    }

    const result = registry.stop(address);
    logger.info({ address, stopped: result.stopped }, 'Monitor stop request handled');
    res.status(200).json({ acknowledged: true, stopped: result.stopped, address });
  };
}

function pickString(value: unknown): string | null {
  return typeof value === 'string' && value.trim().length > 0 ? value : null;
}

interface PostCancelStartRequestBody {
  address?: unknown;
  paymentAddressId?: unknown;
  transactionId?: unknown;
  expectedContract?: unknown;
  expectedSymbol?: unknown;
  cancelledAt?: unknown;
  initialState?: unknown;
  initialStateExpiresAt?: unknown;
}

const ALLOWED_POST_CANCEL_STATES: ReadonlySet<PostCancelMonitorState> = new Set([
  PostCancelMonitorStates.PostCancel24h,
  PostCancelMonitorStates.PostCancel7d,
  PostCancelMonitorStates.PostCancel30d,
]);

/**
 * POST /api/monitor/post-cancel-start — backend asks the sidecar to begin
 * (or resume, on recovery) post-cancel monitoring of a deposit address
 * (T75 — 08 §3.4 gecikmeli ödeme). Idempotent on <c>address</c>: a duplicate
 * call returns <c>started=false</c> with the existing state and otherwise
 * no-ops.
 */
export function postCancelStartHandler(registry: PostCancelMonitorRegistry) {
  return (req: Request, res: Response): void => {
    const body = (req.body ?? {}) as PostCancelStartRequestBody;

    const address = pickString(body.address);
    const paymentAddressId = pickString(body.paymentAddressId);
    const transactionId = pickString(body.transactionId);
    const expectedContract = pickString(body.expectedContract);
    const expectedSymbolRaw = pickString(body.expectedSymbol);
    const cancelledAtRaw = pickString(body.cancelledAt);

    if (
      !address ||
      !paymentAddressId ||
      !transactionId ||
      !expectedContract ||
      !expectedSymbolRaw ||
      !cancelledAtRaw
    ) {
      res.status(400).json({
        error: 'INVALID_POST_CANCEL_REQUEST',
        message:
          'Required fields: address, paymentAddressId, transactionId, expectedContract, expectedSymbol, cancelledAt.',
      });
      return;
    }

    if (!ALLOWED_SYMBOLS.has(expectedSymbolRaw as StablecoinSymbol)) {
      res.status(400).json({
        error: 'UNSUPPORTED_SYMBOL',
        message: `expectedSymbol must be one of: ${[...ALLOWED_SYMBOLS].join(', ')}`,
      });
      return;
    }

    const cancelledAt = parseIsoDate(cancelledAtRaw);
    if (cancelledAt === null) {
      res.status(400).json({
        error: 'INVALID_CANCELLED_AT',
        message: 'cancelledAt must be a valid ISO-8601 timestamp.',
      });
      return;
    }

    const initialStateRaw = pickString(body.initialState);
    let initialState: PostCancelMonitorState | undefined;
    if (initialStateRaw !== null) {
      if (!ALLOWED_POST_CANCEL_STATES.has(initialStateRaw as PostCancelMonitorState)) {
        res.status(400).json({
          error: 'INVALID_INITIAL_STATE',
          message: `initialState must be one of: ${[...ALLOWED_POST_CANCEL_STATES].join(', ')}`,
        });
        return;
      }
      initialState = initialStateRaw as PostCancelMonitorState;
    }

    const initialStateExpiresAtRaw = pickString(body.initialStateExpiresAt);
    let initialStateExpiresAt: Date | undefined;
    if (initialStateExpiresAtRaw !== null) {
      const parsed = parseIsoDate(initialStateExpiresAtRaw);
      if (parsed === null) {
        res.status(400).json({
          error: 'INVALID_STATE_EXPIRES_AT',
          message: 'initialStateExpiresAt must be a valid ISO-8601 timestamp.',
        });
        return;
      }
      initialStateExpiresAt = parsed;
    }

    const options: PostCancelMonitorStartOptions = {
      address,
      paymentAddressId,
      transactionId,
      expectedContract,
      expectedSymbol: expectedSymbolRaw as StablecoinSymbol,
      cancelledAt,
      initialState,
      initialStateExpiresAt,
    };

    try {
      const result = registry.start(options);
      logger.info(
        {
          address,
          transactionId,
          paymentAddressId,
          state: result.state,
          started: result.started,
        },
        'Post-cancel monitor start handled',
      );
      res.status(200).json({
        acknowledged: true,
        started: result.started,
        state: result.state,
        address,
      });
    } catch (err) {
      logger.error(
        { err: (err as Error).message, address, transactionId },
        'Post-cancel monitor start failed',
      );
      res.status(500).json({
        error: 'POST_CANCEL_MONITOR_START_FAILED',
        message: (err as Error).message,
      });
    }
  };
}

/**
 * POST /api/monitor/post-cancel-stop — backend asks the sidecar to drop a
 * post-cancel entry (admin manual stop, successful late refund, or
 * transaction terminal cleanup). Idempotent like the active counterpart.
 */
export function postCancelStopHandler(registry: PostCancelMonitorRegistry) {
  return (req: Request, res: Response): void => {
    const body = (req.body ?? {}) as StopRequestBody;
    const address = pickString(body.address);
    if (!address) {
      res.status(400).json({
        error: 'INVALID_POST_CANCEL_REQUEST',
        message: 'Required field: address.',
      });
      return;
    }
    const result = registry.stop(address);
    logger.info({ address, stopped: result.stopped }, 'Post-cancel monitor stop handled');
    res.status(200).json({ acknowledged: true, stopped: result.stopped, address });
  };
}

function parseIsoDate(value: string): Date | null {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return null;
  return date;
}
