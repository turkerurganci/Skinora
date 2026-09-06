import type { Request, Response } from 'express';
import { logger } from '../logger.js';
import { SidecarError } from '../errors/SidecarError.js';
import { FeeEstimationService } from '../fee/FeeEstimationService.js';
import type { TokenSymbol } from '../transfer/TransferService.js';

interface EstimateFeeBody {
  fromAddress?: unknown;
  toAddress?: unknown;
  amount?: unknown;
  token?: unknown;
}

const TOKEN_SYMBOLS: readonly TokenSymbol[] = ['USDT', 'USDC'];

function asTokenSymbol(value: unknown): TokenSymbol | null {
  return typeof value === 'string' && (TOKEN_SYMBOLS as readonly string[]).includes(value)
    ? (value as TokenSymbol)
    : null;
}

function isNonEmptyString(value: unknown): value is string {
  return typeof value === 'string' && value.length > 0;
}

/**
 * POST /api/transfer/estimate-fee — pre-send gas cost of an outbound TRC-20
 * transfer in USDT (Prova-GasFeeChargedIsFixedGuess). Read-only: nothing is
 * signed or broadcast. The backend charges the returned <c>feeUsdt</c>; any
 * failure here is answered by the caller with its static-setting fallback,
 * so this endpoint never has to be available for money to move.
 */
export function estimateFeeHandler(service: FeeEstimationService) {
  return async (req: Request, res: Response): Promise<void> => {
    const body = (req.body ?? {}) as EstimateFeeBody;
    const token = asTokenSymbol(body.token);
    if (
      !isNonEmptyString(body.toAddress) ||
      !isNonEmptyString(body.amount) ||
      token === null ||
      (body.fromAddress !== undefined && !isNonEmptyString(body.fromAddress))
    ) {
      res.status(400).json({
        error: 'INVALID_ESTIMATE_REQUEST',
        message: 'Fields {toAddress, amount, token=USDT|USDC} are required; fromAddress optional.',
      });
      return;
    }

    try {
      const result = await service.estimate({
        fromAddress: body.fromAddress as string | undefined,
        toAddress: body.toAddress,
        amount: body.amount,
        token,
        correlationId: req.correlationId,
      });
      res.status(200).json(result);
    } catch (err) {
      if (err instanceof SidecarError) {
        const status = err.retryable ? 502 : 400;
        logger.warn(
          {
            code: err.code,
            retryable: err.retryable,
            err: err.message,
            correlationId: req.correlationId,
          },
          'Fee estimate rejected',
        );
        res
          .status(status)
          .json({ error: err.code, message: err.message, retryable: err.retryable });
        return;
      }
      logger.error(
        { err: (err as Error).message, correlationId: req.correlationId },
        'Fee estimate unexpected error',
      );
      res.status(500).json({
        error: 'INTERNAL_ERROR',
        message: 'Unexpected fee estimate failure.',
        retryable: true,
      });
    }
  };
}
