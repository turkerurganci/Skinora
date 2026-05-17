import type { Request, Response } from 'express';
import { config } from '../config/index.js';
import { logger } from '../logger.js';
import { SidecarError } from '../errors/SidecarError.js';
import {
  HdWalletNotConfiguredError,
  InvalidDerivationIndexError,
} from '../wallet/HdWalletService.js';
import { WalletManager } from '../wallet/WalletManager.js';
import { TronGridClient } from '../tron/TronGridClient.js';

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

interface BalancesRequestBody {
  addresses?: unknown;
}

/**
 * Cap on the number of addresses a single reconciliation call may snapshot
 * in one request. Backend ReconciliationService is expected to chunk its
 * deposit-address list against this limit; the cap keeps a runaway caller
 * from exhausting the TronGrid rate budget in a single round-trip.
 */
const MAX_BALANCE_ADDRESSES = 100;

/**
 * Per-address TRX + TRC-20 balance snapshot returned by
 * `POST /api/wallet/balances` (T76 — 05 §3.3). Raw integer amounts; the
 * caller (.NET ReconciliationService) applies token decimals (USDT/USDC = 6,
 * TRX in SUN = 6) to compare against the ledger total.
 */
interface AddressBalances {
  address: string;
  tokens: Record<string, string>;
}

interface BalancesResponse {
  blockNumber: number;
  balances: AddressBalances[];
}

// Reverse map: contract address → token symbol. Reconciliation reports in
// admin-readable symbols (USDT/USDC) rather than raw contract strings.
// Built per request from current config so live env overrides
// (TRON_USDT_CONTRACT / TRON_USDC_CONTRACT) take effect without a restart.
function buildContractToSymbol(): Record<string, string> {
  const entries: Array<[string, string]> = [];
  if (config.usdtContract) entries.push([config.usdtContract, 'USDT']);
  if (config.usdcContract) entries.push([config.usdcContract, 'USDC']);
  return Object.fromEntries(entries);
}

/**
 * Reconciliation snapshot endpoint (T76 — 05 §3.3). Takes a list of Tron
 * addresses, queries TronGrid for the current TRX + supported-TRC-20
 * balances of each, and returns them alongside the solid block height
 * captured once for the whole batch. The caller compares this snapshot
 * against the platform ledger; any mismatch is recorded as a
 * `RECONCILIATION_MISMATCH` AuditLog row and pushed to admin clients.
 *
 * Response token map is keyed by symbol (USDT/USDC/TRX) — the contract
 * address mapping is owned here so the backend never has to learn
 * network-specific contract strings.
 */
export function walletBalancesHandler(
  client?: TronGridClient,
  contractToSymbolOverride?: Record<string, string>,
) {
  return async (req: Request, res: Response): Promise<void> => {
    const body = (req.body ?? {}) as BalancesRequestBody;
    const rawAddresses = body.addresses;

    if (!Array.isArray(rawAddresses) || rawAddresses.length === 0) {
      res.status(400).json({
        error: 'INVALID_BALANCES_REQUEST',
        message: 'Field "addresses" must be a non-empty array.',
      });
      return;
    }
    if (rawAddresses.length > MAX_BALANCE_ADDRESSES) {
      res.status(400).json({
        error: 'INVALID_BALANCES_REQUEST',
        message: `Field "addresses" exceeds maximum ${MAX_BALANCE_ADDRESSES}.`,
      });
      return;
    }

    const addresses: string[] = [];
    for (const candidate of rawAddresses) {
      if (typeof candidate !== 'string' || candidate.length === 0) {
        res.status(400).json({
          error: 'INVALID_BALANCES_REQUEST',
          message: 'Every entry of "addresses" must be a non-empty string.',
        });
        return;
      }
      addresses.push(candidate);
    }

    const tronClient = client ?? new TronGridClient();
    const contractToSymbol = contractToSymbolOverride ?? buildContractToSymbol();

    try {
      const blockNumber = await tronClient.getNowSolidBlock();
      const balances: AddressBalances[] = [];
      for (const address of addresses) {
        const snapshot = await tronClient.getAccountBalances(address);
        const tokens: Record<string, string> = { TRX: snapshot.trx };
        for (const [contract, rawValue] of Object.entries(snapshot.trc20)) {
          const symbol = contractToSymbol[contract];
          if (symbol) tokens[symbol] = rawValue;
        }
        // Ensure supported tokens always appear (zero balance is a valid
        // reconciliation result; absence would otherwise collapse to "no
        // comparison made").
        for (const symbol of Object.values(contractToSymbol)) {
          if (!(symbol in tokens)) tokens[symbol] = '0';
        }
        balances.push({ address, tokens });
      }

      const payload: BalancesResponse = { blockNumber, balances };
      logger.info(
        { addressCount: addresses.length, blockNumber },
        'wallet.balances snapshot computed',
      );
      res.status(200).json(payload);
    } catch (err) {
      if (err instanceof SidecarError) {
        logger.warn(
          { code: err.code, err: err.message, addressCount: addresses.length },
          'wallet.balances sidecar error',
        );
        res.status(502).json({ error: err.code, message: err.message });
        return;
      }
      logger.error(
        { err: (err as Error).message, addressCount: addresses.length },
        'wallet.balances unexpected failure',
      );
      res.status(502).json({
        error: 'BALANCE_SNAPSHOT_FAILED',
        message: 'Failed to retrieve on-chain balances.',
      });
    }
  };
}
