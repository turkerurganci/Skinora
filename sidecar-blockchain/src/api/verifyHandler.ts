import type { Request, Response } from 'express';
import { logger } from '../logger.js';
import { SidecarError } from '../errors/SidecarError.js';
import { tronHex20, type TransferVerifier } from '../transfer/TransferVerifier.js';

/**
 * `POST /api/transfer/verify { network, txHash, token, from?, to, amount }`
 * (T172a; 08 §3.7.6 "Admin aracı", same contract as the Ethereum service's
 * T157 endpoint) → `200 { outcome, matches, blockNumber, matchingTransfers }`
 * where `outcome` is `MATCH` | `NOT_FOUND` | `REVERTED` |
 * `NO_MATCHING_TRANSFER` and `matches` is true only for `MATCH`.
 *
 * Read-only. The backend's two-person closure tool calls it when the second
 * admin approves: the TRON transaction id they typed must be a solidified,
 * successful transaction that moved exactly this amount of this token to this
 * recipient (from this sender when the backend knows it). `txHash` is the
 * TRON id — 64 hex digits, no `0x`; addresses are base58. `amount` is a
 * decimal string with at most six fraction digits — never rounded, an amount
 * that does not parse exactly is a 400. 502 when the node cannot answer: the
 * backend reads that as "not verified", never as a pass.
 */

const TX_ID = /^[0-9a-fA-F]{64}$/;
const AMOUNT = /^\d+(\.\d{1,6})?$/;

/** "98.2" → 98_200_000n (6 decimals), exact. */
export function amountUnits6(text: string): bigint {
  const [whole, fraction = ''] = text.split('.');
  return BigInt(whole) * 1_000_000n + BigInt((fraction + '000000').slice(0, 6));
}

export function verifyHandler(verifier: TransferVerifier) {
  return async (req: Request, res: Response): Promise<void> => {
    const b = (req.body ?? {}) as Record<string, unknown>;
    const txHash =
      typeof b.txHash === 'string' && TX_ID.test(b.txHash) ? b.txHash.toLowerCase() : null;
    const token = b.token === 'USDT' || b.token === 'USDC' ? b.token : null;
    const to = typeof b.to === 'string' && tronHex20(b.to) !== null ? b.to : null;
    const fromGiven = b.from !== undefined && b.from !== null;
    const from = typeof b.from === 'string' && tronHex20(b.from) !== null ? b.from : null;
    const amountText = typeof b.amount === 'string' && AMOUNT.test(b.amount) ? b.amount : null;

    if (!txHash || !token || !to || (fromGiven && from === null) || amountText === null) {
      res.status(400).json({
        error: 'INVALID_VERIFY_REQUEST',
        message:
          'Fields {txHash (64 hex, no 0x), token (USDT|USDC), to (TRON address), amount (≤ 6 decimals)} are required; from, when present, must be a TRON address.',
      });
      return;
    }

    try {
      const result = await verifier.verify({
        txHash,
        token,
        from,
        to,
        amountUnits: amountUnits6(amountText),
      });
      res.status(200).json({
        outcome: result.outcome,
        matches: result.outcome === 'MATCH',
        blockNumber: result.blockNumber,
        matchingTransfers: result.matchingTransfers,
      });
    } catch (err) {
      if (err instanceof SidecarError) {
        res.status(err.retryable ? 502 : 400).json({ error: err.code, message: err.message });
        return;
      }
      logger.error({ err: (err as Error).message, txHash }, 'Transfer verification failed');
      res
        .status(502)
        .json({ error: 'TRANSFER_VERIFY_FAILED', message: 'The node could not answer.' });
    }
  };
}
