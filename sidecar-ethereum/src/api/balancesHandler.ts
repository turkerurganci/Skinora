import type { Request, Response } from 'express';
import { checksummedEvmAddress } from '../address/evmAddress.js';
import type { FinalityStatus } from '../finality/FinalityTracker.js';
import type { BalanceReader } from '../reconcile/BalanceReader.js';

/**
 * `POST /api/wallet/balances { network, addresses }` (05 §3.3a "Uçlar", T76
 * contract) → `200 { blockNumber, balances: [{ address, tokens: { ETH, USDT,
 * USDC } }] }` — raw base units as strings (ETH in wei), every configured
 * symbol present even at zero. The backend's ledger reconciliation (05 §3.3)
 * compares USDT / USDC against its records.
 *
 * <para>
 * Read at the `finalized` block, never `latest`, through the same rate pacer
 * as the hourly reconciliation (08 §3.7.4). Each address costs one
 * `eth_getBalance` and one `eth_call` per token, so a full 100-address request
 * is spread over tens of seconds at the default budget. When the finalized
 * head is older than the archive-safe age the request is refused with 502
 * `FINALIZED_STALE` rather than answered from a state the provider may not
 * serve (T151 — 25 min rule).
 * </para>
 */

const MAX_ADDRESSES = 100;

export interface BalancesHandlerDeps {
  reader: Pick<BalanceReader, 'read' | 'configuredSymbols'>;
  finality: () => FinalityStatus;
  maxFinalizedAgeSeconds: number;
}

export function walletBalancesHandler(deps: BalancesHandlerDeps) {
  return async (req: Request, res: Response): Promise<void> => {
    const raw = (req.body as { addresses?: unknown } | undefined)?.addresses;
    if (!Array.isArray(raw) || raw.length === 0 || raw.length > MAX_ADDRESSES) {
      res.status(400).json({
        error: 'INVALID_BALANCES_REQUEST',
        message: `Field "addresses" must be a non-empty array of at most ${MAX_ADDRESSES}.`,
      });
      return;
    }
    const addresses: string[] = [];
    for (const a of raw) {
      if (typeof a !== 'string' || checksummedEvmAddress(a) === null) {
        res.status(400).json({
          error: 'INVALID_BALANCES_REQUEST',
          message: 'Every entry of "addresses" must be an Ethereum address.',
        });
        return;
      }
      addresses.push(a);
    }

    const status = deps.finality();
    if (!status.snapshot) {
      res.status(502).json({ error: 'FINALIZED_UNKNOWN', message: 'No finalized block read yet.' });
      return;
    }
    if (status.ageSeconds !== null && status.ageSeconds > deps.maxFinalizedAgeSeconds) {
      res.status(502).json({
        error: 'FINALIZED_STALE',
        message: `The finalized block is ${status.ageSeconds}s old; state that deep may need archive access.`,
      });
      return;
    }
    const block = status.snapshot.number;
    try {
      const balances = [];
      for (const address of addresses) {
        const b = await deps.reader.read(address, block);
        const tokens: Record<string, string> = { ETH: b.ETH.toString() };
        for (const symbol of deps.reader.configuredSymbols()) {
          tokens[symbol] = (b.tokens[symbol] ?? 0n).toString();
        }
        balances.push({ address, tokens });
      }
      res.status(200).json({ blockNumber: Number(block), balances });
    } catch (err) {
      req.log.warn({ reason: (err as Error).message }, 'wallet.balances read failed');
      res.status(502).json({
        error: 'BALANCE_SNAPSHOT_FAILED',
        message: 'Failed to read on-chain balances.',
      });
    }
  };
}
