import type { Request, Response } from 'express';
import { checksummedEvmAddress } from '../address/evmAddress.js';
import { SidecarError } from '../errors/SidecarError.js';
import { transfersTotal } from '../metrics.js';
import { TransferDeferredError } from '../transfer/FeePolicy.js';
import {
  REFUND_TYPES,
  type RefundType,
  type SendResult,
  type TokenSymbol,
  type TransferService,
} from '../transfer/TransferService.js';

/**
 * HTTP surface of the Ethereum sends (05 §3.3a "Uçlar" — same paths and
 * bodies as the TRON service, plus the Ethereum-only fields; T152).
 *
 * <para>
 * <b>Answers and what the backend does with them</b> (HttpBlockchainTransferClient):
 * <list type="bullet">
 *   <item>200 `{ txHash, nonce, replayed }` — signed and journalled (the
 *     service delivers it; also when the same id is asked again).</item>
 *   <item>400 `{ error }` — refused for good (policy, limits, blacklist, token
 *     state, simulation revert, idempotency conflict, bad request): the row
 *     becomes `FAILED` and the admins are alerted. Nothing was signed.</item>
 *   <item>503 `{ error, deferred: true, waitingSince }` — the send waits (fee
 *     above the cap, no ETH price, no gas on the sender). Nothing was signed;
 *     the backend keeps the row `PENDING` without spending a retry.</item>
 *   <item>502 / 500 — transient (RPC down, a node-side JSON-RPC error that
 *     is not a revert …). Nothing was signed unless the id was already
 *     journalled, in which case the next call returns it.</item>
 *   <item>503 `JOURNAL_BEHIND_CHAIN` (no `deferred`) — the account's chain
 *     nonce is ahead of the journal; nothing is signed until an operator
 *     acknowledges (DEPLOY_RUNBOOK §D). Transient for the backend.</item>
 * </list>
 * </para>
 */

const GUID = /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/;
const TX_HASH = /^0x[0-9a-fA-F]{64}$/;

type Body = Record<string, unknown>;

function str(body: Body, field: string): string | null {
  const v = body[field];
  return typeof v === 'string' && v.length > 0 ? v : null;
}

function guid(body: Body, field: string): string | null {
  const v = str(body, field);
  return v !== null && GUID.test(v) ? v.toLowerCase() : null;
}

function address(body: Body, field: string): string | null {
  const v = str(body, field);
  return v !== null && checksummedEvmAddress(v) !== null ? v : null;
}

function index(body: Body, field: string): number | null {
  const v = body[field];
  return typeof v === 'number' && Number.isInteger(v) && v >= 0 ? v : null;
}

function token(body: Body): TokenSymbol | null {
  const v = body.token;
  return v === 'USDT' || v === 'USDC' ? v : null;
}

function invalid(res: Response, message: string): void {
  res.status(400).json({ error: 'INVALID_TRANSFER_REQUEST', message, retryable: false });
}

type Flow = 'payout' | 'refund' | 'sweep' | 'cold-wallet';

async function respond(
  req: Request,
  res: Response,
  flow: Flow,
  run: () => Promise<SendResult>,
): Promise<void> {
  try {
    const result = await run();
    transfersTotal.inc({ flow, outcome: result.replayed ? 'replayed' : 'signed' });
    res.status(200).json(result);
  } catch (err) {
    if (err instanceof TransferDeferredError) {
      transfersTotal.inc({ flow, outcome: 'deferred' });
      res.status(503).json({
        error: err.reason,
        message: err.message,
        deferred: true,
        retryable: true,
        waitingSince: err.waitingSince === null ? null : new Date(err.waitingSince).toISOString(),
      });
      return;
    }
    if (err instanceof SidecarError) {
      transfersTotal.inc({ flow, outcome: err.retryable ? 'error' : 'refused' });
      req.log.warn({ code: err.code, flow, reason: err.message }, `${flow} not sent`);
      // JOURNAL_BEHIND_CHAIN: signing is stopped for the account until an
      // operator acknowledges — the service, not the RPC, is unavailable (503;
      // the backend keeps the row PENDING with the same id).
      res
        .status(err.code === 'JOURNAL_BEHIND_CHAIN' ? 503 : err.retryable ? 502 : 400)
        .json({ error: err.code, message: err.message, retryable: err.retryable });
      return;
    }
    transfersTotal.inc({ flow, outcome: 'error' });
    req.log.error({ flow, reason: (err as Error).message }, `${flow} unexpected error`);
    res
      .status(500)
      .json({ error: 'INTERNAL_ERROR', message: 'Unexpected transfer failure.', retryable: true });
  }
}

/** POST /api/transfer/payout { network, blockchainTransactionId, toAddress, amount, token, feeCapUsd } */
export function payoutHandler(service: TransferService) {
  return async (req: Request, res: Response): Promise<void> => {
    const b = (req.body ?? {}) as Body;
    const id = guid(b, 'blockchainTransactionId');
    const to = address(b, 'toAddress');
    const amount = str(b, 'amount');
    const tok = token(b);
    const cap = str(b, 'feeCapUsd');
    if (!id || !to || !amount || !tok || !cap) {
      invalid(
        res,
        'Fields {blockchainTransactionId (GUID), toAddress (0x…), amount, token, feeCapUsd} are required.',
      );
      return;
    }
    await respond(req, res, 'payout', () =>
      service.payout({
        blockchainTransactionId: id,
        toAddress: to,
        amount,
        token: tok,
        feeCapUsd: cap,
      }),
    );
  };
}

/** POST /api/transfer/refund { network, blockchainTransactionId, depositIndex, depositAddress, toBuyerAddress, amount, token, refundType, feeCapUsd } */
export function refundHandler(service: TransferService) {
  return async (req: Request, res: Response): Promise<void> => {
    const b = (req.body ?? {}) as Body;
    const id = guid(b, 'blockchainTransactionId');
    const depositIndex = index(b, 'depositIndex');
    const deposit = address(b, 'depositAddress');
    const to = address(b, 'toBuyerAddress');
    const amount = str(b, 'amount');
    const tok = token(b);
    const refundType = str(b, 'refundType');
    const cap = str(b, 'feeCapUsd');
    if (
      !id ||
      depositIndex === null ||
      !deposit ||
      !to ||
      !amount ||
      !tok ||
      !refundType ||
      !cap ||
      !(REFUND_TYPES as readonly string[]).includes(refundType)
    ) {
      invalid(
        res,
        'Fields {blockchainTransactionId (GUID), depositIndex>=0, depositAddress, toBuyerAddress, amount, token, refundType, feeCapUsd} are required.',
      );
      return;
    }
    await respond(req, res, 'refund', () =>
      service.refund({
        blockchainTransactionId: id,
        depositIndex,
        depositAddress: deposit,
        toBuyerAddress: to,
        amount,
        token: tok,
        refundType: refundType as RefundType,
        feeCapUsd: cap,
      }),
    );
  };
}

/** POST /api/transfer/sweep { network, blockchainTransactionId, depositIndex, depositAddress, toHotWalletAddress, amount, token, feeCapUsd } */
export function sweepHandler(service: TransferService) {
  return async (req: Request, res: Response): Promise<void> => {
    const b = (req.body ?? {}) as Body;
    const id = guid(b, 'blockchainTransactionId');
    const depositIndex = index(b, 'depositIndex');
    const deposit = address(b, 'depositAddress');
    const to = address(b, 'toHotWalletAddress');
    const amount = str(b, 'amount');
    const tok = token(b);
    const cap = str(b, 'feeCapUsd');
    if (!id || depositIndex === null || !deposit || !to || !amount || !tok || !cap) {
      invalid(
        res,
        'Fields {blockchainTransactionId (GUID), depositIndex>=0, depositAddress, toHotWalletAddress, amount, token, feeCapUsd} are required.',
      );
      return;
    }
    await respond(req, res, 'sweep', () =>
      service.sweep({
        blockchainTransactionId: id,
        depositIndex,
        depositAddress: deposit,
        toHotWalletAddress: to,
        amount,
        token: tok,
        feeCapUsd: cap,
      }),
    );
  };
}

/** POST /api/transfer/cold-wallet { network, coldTransferId, toColdAddress, amount, token, feeCapUsd } */
export function coldWalletTransferHandler(service: TransferService) {
  return async (req: Request, res: Response): Promise<void> => {
    const b = (req.body ?? {}) as Body;
    const id = guid(b, 'coldTransferId');
    const to = address(b, 'toColdAddress');
    const amount = str(b, 'amount');
    const tok = token(b);
    const cap = str(b, 'feeCapUsd');
    if (!id || !to || !amount || !tok || !cap) {
      invalid(
        res,
        'Fields {coldTransferId (GUID), toColdAddress, amount, token, feeCapUsd} are required.',
      );
      return;
    }
    await respond(req, res, 'cold-wallet', () =>
      service.coldWalletTransfer({
        coldTransferId: id,
        toColdAddress: to,
        amount,
        token: tok,
        feeCapUsd: cap,
      }),
    );
  };
}

/**
 * GET /api/transfer/status/:txHash →
 *   200 `{ txHash, blockNumber, contractRet, confirmations, finalized }` — `txHash` is the
 *       attempt that is in a block (a fee bump changes it);
 *   404 `TRANSFER_NOT_FOUND`, 409 `TRANSFER_NONCE_CONFLICT`, 400 bad hash — row-specific, the
 *       backend's confirmation job does not stop the network's other rows for them;
 *   502 — RPC unavailable (the job stops this network's turn).
 */
export function transferStatusHandler(service: TransferService) {
  return async (req: Request, res: Response): Promise<void> => {
    const hash = req.params.txHash;
    if (typeof hash !== 'string' || !TX_HASH.test(hash)) {
      res
        .status(400)
        .json({ error: 'INVALID_TX_HASH', message: 'Path parameter :txHash must be 0x + 64 hex.' });
      return;
    }
    try {
      res.status(200).json(await service.status(hash));
    } catch (err) {
      if (err instanceof SidecarError) {
        const status =
          err.code === 'TRANSFER_NOT_FOUND'
            ? 404
            : err.code === 'TRANSFER_NONCE_CONFLICT'
              ? 409
              : err.retryable
                ? 502
                : 400;
        res.status(status).json({ error: err.code, message: err.message });
        return;
      }
      req.log.error({ reason: (err as Error).message, hash }, 'Transfer status lookup failed');
      res.status(500).json({ error: 'INTERNAL_ERROR', message: 'Status lookup failed.' });
    }
  };
}
