import type { Request, Response } from 'express';
import { checksummedEvmAddress } from '../address/evmAddress.js';
import { SidecarError } from '../errors/SidecarError.js';
import { parseScaled } from '../fee/feeMath.js';
import type { TransferVerifier } from '../transfer/TransferVerifier.js';

/**
 * `POST /api/transfer/verify { network, txHash, token, from?, to, amount }`
 * (T157; 08 §3.7.6 "Admin aracı") →
 * `200 { outcome, matches, blockNumber, matchingTransfers }` where `outcome` is
 * `MATCH` | `NOT_FOUND` | `REVERTED` | `NO_MATCHING_TRANSFER` and `matches` is
 * true only for `MATCH`.
 *
 * Read-only. The backend's two-person closure tool calls it when the second
 * admin approves: the hash they typed must be a successful transaction that
 * moved exactly this amount of this token to this recipient (from this sender
 * when the backend knows it). `amount` is a decimal string with at most six
 * fraction digits — never rounded, an amount that does not parse exactly is a
 * 400. 502 when the node cannot answer: the backend reads that as "not
 * verified", never as a pass.
 */

const TX_HASH = /^0x[0-9a-fA-F]{64}$/;
const AMOUNT = /^\d+(\.\d{1,6})?$/;

export function verifyHandler(verifier: TransferVerifier) {
  return async (req: Request, res: Response): Promise<void> => {
    const b = (req.body ?? {}) as Record<string, unknown>;
    const txHash = typeof b.txHash === 'string' && TX_HASH.test(b.txHash) ? b.txHash : null;
    const token = b.token === 'USDT' || b.token === 'USDC' ? b.token : null;
    const to = typeof b.to === 'string' && checksummedEvmAddress(b.to) !== null ? b.to : null;
    const fromGiven = b.from !== undefined && b.from !== null;
    const from =
      typeof b.from === 'string' && checksummedEvmAddress(b.from) !== null ? b.from : null;
    const amountText = typeof b.amount === 'string' && AMOUNT.test(b.amount) ? b.amount : null;
    const amountUnits = amountText === null ? null : parseScaled(amountText, 6);

    if (!txHash || !token || !to || (fromGiven && from === null) || amountUnits === null) {
      res.status(400).json({
        error: 'INVALID_VERIFY_REQUEST',
        message:
          'Fields {txHash (0x + 64 hex), token (USDT|USDC), to (address), amount (≤ 6 decimals)} are required; from, when present, must be an address.',
      });
      return;
    }

    try {
      const result = await verifier.verify({ txHash, token, from, to, amountUnits });
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
      req.log.error({ reason: (err as Error).message, txHash }, 'Transfer verification failed');
      res.status(502).json({ error: 'RPC_UNAVAILABLE', message: 'The node could not answer.' });
    }
  };
}
