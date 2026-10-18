import { canonicalAddress, sameAddress } from '../address/canonicalAddress.js';
import type { EthereumRpcClient } from '../rpc/RpcClient.js';
import { TRANSFER_TOPIC, addressTopic } from './erc20.js';

/**
 * Does a transaction that is on the chain really carry the stablecoin transfer
 * an outbound row stands for? (T157 — the admin tool that closes a row with a
 * chain hash after the signing journal was lost, 07 §9.32.)
 *
 * The status endpoint answers "mined, status 1" for a hash the journal does not
 * know — it cannot tell a payout from someone's unrelated transaction. This
 * check reads the receipt and requires a `Transfer(from, to, value)` log
 * emitted by the platform's token contract (USDT; USDC is allowlisted too) to
 * the expected recipient for exactly the expected amount — and, when the sender
 * is known, from that sender. It is a read: it signs nothing and writes nothing.
 *
 * Fail-closed by construction: an RPC error propagates (the endpoint answers
 * 502 and the backend refuses), and only an exact match is `MATCH`.
 */

export type VerifyOutcome =
  /** The receipt carries the expected, non-removed Transfer log and the transaction succeeded. */
  | 'MATCH'
  /** Neither the journal's chain nor the node knows the hash. */
  | 'NOT_FOUND'
  /** The transaction reverted: nothing moved. */
  | 'REVERTED'
  /** Succeeded, but no log moves the expected amount of the token to the recipient (from the sender). */
  | 'NO_MATCHING_TRANSFER';

export interface VerifyRequest {
  readonly txHash: string;
  readonly token: 'USDT' | 'USDC';
  /** The account the tokens must leave; `null` = not checked (the backend does not know it). */
  readonly from: string | null;
  readonly to: string;
  readonly amountUnits: bigint;
}

export interface VerifyResult {
  readonly outcome: VerifyOutcome;
  readonly blockNumber: number | null;
  /** How many logs match (a batch can carry several equal transfers). */
  readonly matchingTransfers: number;
}

export interface VerifierDeps {
  readonly rpc: Pick<EthereumRpcClient, 'getTransactionReceipt'>;
  readonly tokenContracts: { readonly USDT: string; readonly USDC: string };
}

export class TransferVerifier {
  constructor(private readonly deps: VerifierDeps) {}

  async verify(request: VerifyRequest): Promise<VerifyResult> {
    const receipt = await this.deps.rpc.getTransactionReceipt(request.txHash);
    if (!receipt) return { outcome: 'NOT_FOUND', blockNumber: null, matchingTransfers: 0 };

    const blockNumber = Number(receipt.blockNumber);
    if (receipt.status !== 1) return { outcome: 'REVERTED', blockNumber, matchingTransfers: 0 };

    const contract = this.deps.tokenContracts[request.token];
    const to = canonicalAddress(request.to);
    const from = request.from === null ? null : canonicalAddress(request.from);

    const matching = receipt.logs.filter(
      (log) =>
        !log.removed &&
        sameAddress(log.address, contract) &&
        log.topics.length === 3 &&
        log.topics[0]?.toLowerCase() === TRANSFER_TOPIC.toLowerCase() &&
        log.topics[2]?.toLowerCase() === addressTopic(to) &&
        (from === null || log.topics[1]?.toLowerCase() === addressTopic(from)) &&
        valueOf(log.data) === request.amountUnits,
    ).length;

    return {
      outcome: matching > 0 ? 'MATCH' : 'NO_MATCHING_TRANSFER',
      blockNumber,
      matchingTransfers: matching,
    };
  }
}

/** The uint256 in a Transfer log's data, or `null` when the data is not exactly one word. */
function valueOf(data: string): bigint | null {
  if (!/^0x[0-9a-fA-F]{64}$/.test(data)) return null;
  return BigInt(data);
}
