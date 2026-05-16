import type { Request, Response } from 'express';
import { logger } from '../logger.js';
import { SidecarError } from '../errors/SidecarError.js';
import {
  HdWalletNotConfiguredError,
  InvalidDerivationIndexError,
} from '../wallet/HdWalletService.js';
import { WalletManager } from '../wallet/WalletManager.js';

interface DeriveRequestBody {
  index?: unknown;
  transactionId?: unknown;
}

// The .NET backend owns index allocation (08 §3.2, 05 §3.3) — this endpoint
// is a pure derivation primitive. The optional transactionId rides through
// for correlation/logging only; we never persist it in the sidecar.
export function deriveAddressHandler(wallet: WalletManager) {
  return (req: Request, res: Response): void => {
    const body = (req.body ?? {}) as DeriveRequestBody;
    const rawIndex = body.index;
    const transactionId = typeof body.transactionId === 'string' ? body.transactionId : undefined;

    if (typeof rawIndex !== 'number' || !Number.isInteger(rawIndex) || rawIndex < 0) {
      res.status(400).json({
        error: 'INVALID_DERIVATION_INDEX',
        message: 'Field "index" must be a non-negative integer.',
      });
      return;
    }

    try {
      const result = wallet.derive(rawIndex);
      logger.info(
        { index: rawIndex, transactionId, derivationPath: result.derivationPath },
        'HD wallet address derived',
      );
      res.status(200).json(result);
    } catch (err) {
      if (err instanceof HdWalletNotConfiguredError) {
        res.status(503).json({ error: err.code, message: err.message });
        return;
      }
      if (err instanceof InvalidDerivationIndexError) {
        res.status(400).json({ error: err.code, message: err.message });
        return;
      }
      if (err instanceof SidecarError) {
        logger.error({ code: err.code, err: err.message, index: rawIndex }, 'HD derive failed');
        res.status(500).json({ error: err.code, message: err.message });
        return;
      }
      logger.error({ err: (err as Error).message, index: rawIndex }, 'HD derive unexpected error');
      res.status(500).json({ error: 'INTERNAL_ERROR', message: 'Unexpected derivation failure.' });
    }
  };
}
