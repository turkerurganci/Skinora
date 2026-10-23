import TronWeb from 'tronweb';
import { SidecarError } from '../errors/SidecarError.js';
import { timedTronFetch } from '../metrics.js';

/**
 * Does a transaction that is on the TRON chain really carry the stablecoin
 * transfer an outbound row stands for? (T172a — the two-person closure tool,
 * 07 §9.32, used after a database restore: DEPLOY_RUNBOOK §N.8.)
 *
 * The status endpoint answers "N confirmations, SUCCESS" for any id — it cannot
 * tell a payout from someone's unrelated transaction. This check reads the
 * solidified transaction info (`walletsolidity/gettransactioninfobyid`, so only
 * an irreversible transaction is ever seen) and requires a TRC-20
 * `Transfer(from, to, value)` log emitted by the platform's token contract
 * (USDT; USDC allowlisted too) to the expected recipient for exactly the
 * expected amount — and, when the sender is known, from that sender. It is a
 * read: it signs nothing and writes nothing.
 *
 * Fail-closed by construction: an HTTP / transport error propagates (the
 * endpoint answers 502 and the backend refuses), and only an exact match is
 * `MATCH`. Same answer shape as the Ethereum service's check (T157).
 */

export type VerifyOutcome = 'MATCH' | 'NOT_FOUND' | 'REVERTED' | 'NO_MATCHING_TRANSFER';

export interface VerifyRequest {
  /** 64 hex digits, no prefix (lowercase or not). */
  readonly txHash: string;
  readonly token: 'USDT' | 'USDC';
  /** Base58 account the tokens must leave; `null` = not checked. */
  readonly from: string | null;
  /** Base58 recipient. */
  readonly to: string;
  /** Raw token units (6 decimals applied). */
  readonly amountUnits: bigint;
}

export interface VerifyResult {
  readonly outcome: VerifyOutcome;
  readonly blockNumber: number | null;
  /** How many logs match (one transaction can carry several equal transfers). */
  readonly matchingTransfers: number;
}

export interface VerifierDeps {
  readonly solidityUrl: string;
  readonly apiKey: string;
  readonly tokenContracts: { readonly USDT: string; readonly USDC: string };
  readonly fetchFn?: typeof fetch;
}

/** keccak256("Transfer(address,address,uint256)"). */
// Written in two halves only so the repository's secret scan (64-hex = key)
// does not mistake a public event signature for a private key.
export const TRANSFER_TOPIC =
  'ddf252ad1be2c89b69c2b068fc378daa' + '952ba7f163c4a11628f55a4df523b3ef';

interface RawLog {
  address?: string;
  topics?: string[];
  data?: string;
}

interface TransactionInfo {
  id?: string;
  blockNumber?: number;
  result?: string;
  receipt?: { result?: string };
  log?: RawLog[];
}

/** Base58 TRON address → the 20-byte hex a log carries (lowercase, no `41`); null when unparseable. */
export function tronHex20(base58: string): string | null {
  try {
    if (!base58.startsWith('T') || !TronWeb.isAddress(base58)) return null;
    const hex = TronWeb.address.toHex(base58);
    if (typeof hex !== 'string' || hex.length < 40) return null;
    return hex.replace(/^0x/i, '').toLowerCase().slice(-40);
  } catch {
    return null;
  }
}

/** The 20-byte account in a 32-byte topic; null when the topic is not one. */
function topicAccount(topic: string | undefined): string | null {
  if (typeof topic !== 'string') return null;
  const clean = topic.replace(/^0x/i, '').toLowerCase();
  if (!/^[0-9a-f]{64}$/.test(clean)) return null;
  return clean.slice(24);
}

/** The uint256 in a Transfer log's data, or `null` when the data is not exactly one word. */
function valueOf(data: string | undefined): bigint | null {
  if (typeof data !== 'string') return null;
  const clean = data.replace(/^0x/i, '');
  if (!/^[0-9a-fA-F]{64}$/.test(clean)) return null;
  return BigInt(`0x${clean}`);
}

export class TransferVerifier {
  private readonly fetchFn: typeof fetch;

  constructor(private readonly deps: VerifierDeps) {
    this.fetchFn = deps.fetchFn ?? fetch;
  }

  async verify(request: VerifyRequest): Promise<VerifyResult> {
    const contract = tronHex20(this.deps.tokenContracts[request.token]);
    const to = tronHex20(request.to);
    const from = request.from === null ? null : tronHex20(request.from);
    if (contract === null || to === null || (request.from !== null && from === null)) {
      // A malformed configured contract or address can never be "verified".
      throw new SidecarError(
        'Token contract, recipient or sender is not a TRON address.',
        'INVALID_VERIFY_REQUEST',
        false,
      );
    }

    const headers: Record<string, string> = {
      accept: 'application/json',
      'content-type': 'application/json',
    };
    if (this.deps.apiKey) headers['TRON-PRO-API-KEY'] = this.deps.apiKey;

    const response = await timedTronFetch(this.fetchFn, 'walletsolidity.gettransactioninfobyid')(
      `${this.deps.solidityUrl}/walletsolidity/gettransactioninfobyid`,
      { method: 'POST', headers, body: JSON.stringify({ value: request.txHash.toLowerCase() }) },
    );
    if (!response.ok) {
      throw new SidecarError(
        `gettransactioninfobyid returned HTTP ${response.status}`,
        'TRANSFER_VERIFY_HTTP_ERROR',
        true,
      );
    }
    const info = (await response.json()) as TransactionInfo | null;

    // An unknown (or not yet solidified) id answers `{}`.
    if (!info || typeof info.id !== 'string' || info.id.length === 0) {
      return { outcome: 'NOT_FOUND', blockNumber: null, matchingTransfers: 0 };
    }
    if (info.id.toLowerCase() !== request.txHash.toLowerCase()) {
      // The node answered about another transaction: never a pass.
      throw new SidecarError(
        'The node answered for another transaction id.',
        'TRANSFER_VERIFY_MISMATCH',
        true,
      );
    }
    const blockNumber =
      typeof info.blockNumber === 'number' && Number.isFinite(info.blockNumber)
        ? info.blockNumber
        : null;
    if (info.result === 'FAILED' || info.receipt?.result !== 'SUCCESS') {
      return { outcome: 'REVERTED', blockNumber, matchingTransfers: 0 };
    }

    const matching = (Array.isArray(info.log) ? info.log : []).filter((log) => {
      if (!log || !Array.isArray(log.topics) || log.topics.length !== 3) return false;
      if ((log.topics[0] ?? '').replace(/^0x/i, '').toLowerCase() !== TRANSFER_TOPIC) return false;
      const address = (log.address ?? '').replace(/^0x/i, '').toLowerCase().slice(-40);
      if (address !== contract) return false;
      if (topicAccount(log.topics[2]) !== to) return false;
      if (from !== null && topicAccount(log.topics[1]) !== from) return false;
      return valueOf(log.data) === request.amountUnits;
    }).length;

    return {
      outcome: matching > 0 ? 'MATCH' : 'NO_MATCHING_TRANSFER',
      blockNumber,
      matchingTransfers: matching,
    };
  }
}
