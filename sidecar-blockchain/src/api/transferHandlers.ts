import type { Request, Response } from 'express';
import { logger } from '../logger.js';
import { SidecarError } from '../errors/SidecarError.js';
import { TransferService, TokenSymbol } from '../transfer/TransferService.js';
import { RefundService } from '../transfer/RefundService.js';
import { transfersTotal } from '../metrics.js';

interface PayoutBody {
  blockchainTransactionId?: unknown;
  toAddress?: unknown;
  amount?: unknown;
  token?: unknown;
}

interface RefundBody {
  blockchainTransactionId?: unknown;
  depositIndex?: unknown;
  depositAddress?: unknown;
  toBuyerAddress?: unknown;
  amount?: unknown;
  token?: unknown;
}

interface SweepBody {
  blockchainTransactionId?: unknown;
  depositIndex?: unknown;
  depositAddress?: unknown;
  toHotWalletAddress?: unknown;
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

function isNonNegativeInt(value: unknown): value is number {
  return typeof value === 'number' && Number.isInteger(value) && value >= 0;
}

export function payoutHandler(service: TransferService) {
  return async (req: Request, res: Response): Promise<void> => {
    const body = (req.body ?? {}) as PayoutBody;
    if (
      !isNonEmptyString(body.blockchainTransactionId) ||
      !isNonEmptyString(body.toAddress) ||
      !isNonEmptyString(body.amount) ||
      asTokenSymbol(body.token) === null
    ) {
      res.status(400).json({
        error: 'INVALID_TRANSFER_REQUEST',
        message:
          'Fields {blockchainTransactionId, toAddress, amount, token=USDT|USDC} are required.',
      });
      return;
    }

    try {
      const result = await service.payout({
        blockchainTransactionId: body.blockchainTransactionId,
        toAddress: body.toAddress,
        amount: body.amount,
        token: asTokenSymbol(body.token)!,
        correlationId: req.correlationId,
      });
      res.status(200).json({ txHash: result.txHash });
    } catch (err) {
      handleTransferError(err, res, req, 'payout');
    }
  };
}

export function refundHandler(service: RefundService) {
  return async (req: Request, res: Response): Promise<void> => {
    const body = (req.body ?? {}) as RefundBody;
    if (
      !isNonEmptyString(body.blockchainTransactionId) ||
      !isNonNegativeInt(body.depositIndex) ||
      !isNonEmptyString(body.depositAddress) ||
      !isNonEmptyString(body.toBuyerAddress) ||
      !isNonEmptyString(body.amount) ||
      asTokenSymbol(body.token) === null
    ) {
      res.status(400).json({
        error: 'INVALID_TRANSFER_REQUEST',
        message:
          'Fields {blockchainTransactionId, depositIndex>=0, depositAddress, toBuyerAddress, amount, token=USDT|USDC} are required.',
      });
      return;
    }

    try {
      const result = await service.refund({
        blockchainTransactionId: body.blockchainTransactionId,
        depositIndex: body.depositIndex,
        depositAddress: body.depositAddress,
        toBuyerAddress: body.toBuyerAddress,
        amount: body.amount,
        token: asTokenSymbol(body.token)!,
        correlationId: req.correlationId,
      });
      res.status(200).json({
        txHash: result.txHash,
        delegationMode: result.delegationMode,
        delegationAmountSun: result.delegationAmountSun,
        fallbackAmountSun: result.fallbackAmountSun,
      });
    } catch (err) {
      handleTransferError(err, res, req, 'refund');
    }
  };
}

export function sweepHandler(service: TransferService) {
  return async (req: Request, res: Response): Promise<void> => {
    const body = (req.body ?? {}) as SweepBody;
    if (
      !isNonEmptyString(body.blockchainTransactionId) ||
      !isNonNegativeInt(body.depositIndex) ||
      !isNonEmptyString(body.depositAddress) ||
      !isNonEmptyString(body.toHotWalletAddress) ||
      !isNonEmptyString(body.amount) ||
      asTokenSymbol(body.token) === null
    ) {
      res.status(400).json({
        error: 'INVALID_TRANSFER_REQUEST',
        message:
          'Fields {blockchainTransactionId, depositIndex>=0, depositAddress, toHotWalletAddress, amount, token=USDT|USDC} are required.',
      });
      return;
    }

    try {
      const result = await service.sweep({
        blockchainTransactionId: body.blockchainTransactionId,
        depositIndex: body.depositIndex,
        depositAddress: body.depositAddress,
        toHotWalletAddress: body.toHotWalletAddress,
        amount: body.amount,
        token: asTokenSymbol(body.token)!,
        correlationId: req.correlationId,
      });
      res.status(200).json({
        txHash: result.txHash,
        delegationMode: result.delegationMode,
        delegationAmountSun: result.delegationAmountSun,
        fallbackAmountSun: result.fallbackAmountSun,
      });
    } catch (err) {
      handleTransferError(err, res, req, 'sweep');
    }
  };
}

export function transferStatusHandler(service: TransferService) {
  return async (req: Request, res: Response): Promise<void> => {
    const txHash = req.params.txHash;
    if (!txHash || typeof txHash !== 'string') {
      res
        .status(400)
        .json({ error: 'INVALID_TX_HASH', message: 'Path parameter :txHash is required.' });
      return;
    }
    try {
      const status = await service.getStatus(txHash);
      res.status(200).json(status);
    } catch (err) {
      if (err instanceof SidecarError) {
        const status = err.retryable ? 502 : 400;
        res.status(status).json({ error: err.code, message: err.message });
        return;
      }
      logger.error(
        { err: (err as Error).message, txHash },
        'Unexpected error reading transfer status',
      );
      res.status(500).json({ error: 'INTERNAL_ERROR', message: 'Status lookup failed.' });
    }
  };
}

function handleTransferError(
  err: unknown,
  res: Response,
  req: Request,
  flow: 'payout' | 'refund' | 'sweep',
): void {
  transfersTotal.inc({ type: flow, status: 'error' });
  if (err instanceof SidecarError) {
    const status = err.retryable ? 502 : 400;
    logger.warn(
      { code: err.code, retryable: err.retryable, flow, err: err.message },
      `${flow} request rejected`,
    );
    res.status(status).json({ error: err.code, message: err.message, retryable: err.retryable });
    return;
  }
  logger.error(
    { err: (err as Error).message, flow, correlationId: req.correlationId },
    `${flow} unexpected error`,
  );
  res
    .status(500)
    .json({ error: 'INTERNAL_ERROR', message: 'Unexpected transfer failure.', retryable: true });
}
