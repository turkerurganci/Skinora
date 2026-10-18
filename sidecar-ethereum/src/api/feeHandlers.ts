import type { Request, Response } from 'express';
import { SidecarError } from '../errors/SidecarError.js';
import { checksummedEvmAddress } from '../address/evmAddress.js';
import type { EthFeeEstimationService, EthTokenSymbol } from '../fee/FeeEstimationService.js';

/** Positive decimal with at most 6 fraction digits — the TRON service's amount rule. */
const AMOUNT = /^(?:0|[1-9]\d*)(?:\.\d{1,6})?$/;
const TOKENS: readonly EthTokenSymbol[] = ['USDT', 'USDC'];

interface EstimateFeeBody {
  fromAddress?: unknown;
  toAddress?: unknown;
  amount?: unknown;
  token?: unknown;
}

function address(value: unknown): string | null {
  return typeof value === 'string' ? checksummedEvmAddress(value) : null;
}

/**
 * `POST /api/transfer/estimate-fee { network, fromAddress?, toAddress, amount, token }`
 * (T154 — 05 §3.3a, 08 §3.7.7). Same body as the TRON service: `fromAddress`
 * absent = hot wallet, an explicit `null` is refused. 200 carries `feeUsdt`
 * and the exact `feeWei` (decimal strings); 400 = this request cannot be
 * priced, 502 = try again (price or RPC outage). The backend answers every
 * non-200 with its static fallback.
 */
export function estimateFeeHandler(service: EthFeeEstimationService) {
  return async (req: Request, res: Response): Promise<void> => {
    const body = (req.body ?? {}) as EstimateFeeBody;
    const toAddress = address(body.toAddress);
    const fromAddress = body.fromAddress === undefined ? undefined : address(body.fromAddress);
    const amount = typeof body.amount === 'string' ? body.amount : '';
    const token = TOKENS.find((t) => t === body.token);

    if (
      toAddress === null ||
      fromAddress === null ||
      !AMOUNT.test(amount) ||
      !/[1-9]/.test(amount) ||
      token === undefined
    ) {
      res.status(400).json({
        error: 'INVALID_ESTIMATE_REQUEST',
        message:
          'Fields {toAddress (0x address), amount (positive, at most 6 decimals), token=USDT|USDC} are required; fromAddress, when sent, must be a 0x address — omit it (do not send null) to price a hot-wallet transfer.',
      });
      return;
    }

    try {
      const result = await service.estimate({
        fromAddress,
        toAddress,
        amount,
        token,
        correlationId: req.correlationId,
      });
      req.log.info(
        {
          feeUsdt: result.feeUsdt,
          feeWei: result.feeWei,
          sender: result.sender,
          gasLimit: result.gasLimit,
          maxFeePerGasWei: result.maxFeePerGasWei,
          ethPriceUsdt: result.ethPriceUsdt,
          priceSource: result.priceSource,
        },
        'Ethereum fee estimate',
      );
      res.status(200).json(result);
    } catch (err) {
      if (err instanceof SidecarError) {
        req.log.warn(
          { code: err.code, retryable: err.retryable, err: err.message },
          'Ethereum fee estimate rejected',
        );
        res
          .status(err.retryable ? 502 : 400)
          .json({ error: err.code, message: err.message, retryable: err.retryable });
        return;
      }
      req.log.error({ err: (err as Error).message }, 'Ethereum fee estimate unexpected error');
      res.status(500).json({
        error: 'INTERNAL_ERROR',
        message: 'Unexpected fee estimate failure.',
        retryable: true,
      });
    }
  };
}
