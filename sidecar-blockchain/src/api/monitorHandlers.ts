import type { Request, Response } from 'express';
import { logger } from '../logger.js';
import type { MonitorRegistry, MonitorStartOptions } from '../monitor/MonitorRegistry.js';
import type { StablecoinSymbol } from '../monitor/PaymentMonitorRules.js';

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
