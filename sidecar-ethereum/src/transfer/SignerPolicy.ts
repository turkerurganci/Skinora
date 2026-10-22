import { Transaction } from 'ethers';
import { canonicalAddress, sameAddress } from '../address/canonicalAddress.js';
import { SidecarError } from '../errors/SidecarError.js';
import { NATIVE_TRANSFER_GAS } from '../fee/feeMath.js';
import { decodeTransferCall } from './erc20.js';

/**
 * The signer's allowlist (08 §3.7.6 "İmzacı izin listesi", 11 §F8 T141 (d) —
 * öneriyle kaydedildi — ⚠). Every transaction is checked here, field by field,
 * immediately before a key touches it; a transaction that fails any rule is
 * never signed. The rules are about the transaction object itself, not about
 * the request that produced it, so a bug anywhere upstream (calldata built for
 * the wrong recipient, a wrong chain, a stray `value`) stops here.
 *
 * <list type="bullet">
 *   <item>Only the configured `chainId` (mainnet 1, Sepolia 11155111).</item>
 *   <item>Only transaction types 0 and 2. Type 4 (EIP-7702 delegation), type 3
 *     (blobs) and type 1 never.</item>
 *   <item><b>Token keys</b> (hot wallet, deposits): only a call of
 *     `transfer(address,uint256)` on an allowlisted token contract — USDT, and
 *     USDC only when the request is a wrong-token refund or (T160c) a sweep —
 *     the deposit → pinned hot wallet send that collects the refund fee a
 *     wrong-token refund withheld in USDC — with `value = 0`,
 *     exactly the recipient and amount asked for, no access list, a bounded
 *     gas limit, and (when the flow has one) a worst-case fee
 *     (`gasLimit × maxFeePerGas`) within the cap.</item>
 *   <item><b>The one exception — the fuel key</b> (T153): a plain ETH send
 *     (no calldata; T159a: the gas limit the caller estimated for it — at
 *     least 21,000, at most the signable ceiling — not a fixed 21,000) to an
 *     address the service itself derives
 *     from its seed — never a platform wallet — of no more than the token send
 *     it pays for lacks, within a single-send cap (value) and a 24-hour cap
 *     (value + the fuel's own worst-case fee) and (as every
 *     capped send) within its share of the fee cap. Unset caps refuse (fail
 *     closed).</item>
 * </list>
 */

export interface SignerPolicyConfig {
  readonly chainId: bigint;
  /** Canonical USDT contract — '' when not configured (nothing may be signed then). */
  readonly paymentToken: string;
  /** Canonical USDC contract — the only extra token, and only for a wrong-token refund or a sweep (T160c). */
  readonly wrongTokenRefundToken: string;
  /**
   * Upper bound on any transaction's gas limit (T159a: `MAX_SIGNABLE_GAS_LIMIT`
   * — the abnormal-estimate ceiling of 1,000,000 + the 10 % headroom; was
   * 150,000 for token sends, which a first USDC receipt on Sepolia — 169,926
   * estimated — already exceeded).
   */
  readonly maxGasLimit: bigint;
}

export interface TokenTransferIntent {
  readonly contract: string;
  readonly recipient: string;
  readonly amountUnits: bigint;
  /**
   * True only for a `WRONG_TOKEN_REFUND` and (T160c) a `SWEEP` — the flows that
   * may move USDC. A sweep's destination is pinned to the service's own hot
   * wallet (`TransferGuard.assertSweepDestination`), so USDC can only reach a
   * platform wallet this way.
   */
  readonly wrongTokenRefund: boolean;
  /**
   * Capped flows (sweep, payout, refund, cold): the most the transaction may
   * cost, wei — `gasLimit × fee ceiling` may not exceed it (08 §3.7.7; per
   * transaction, yönetici kararı T152 doğrulaması).
   */
  readonly feeCapWei?: bigint;
}

export interface FuelIntent {
  readonly recipient: string;
  readonly amountWei: bigint;
  /** Re-derives the address from the service's own seed (08 §3.7.5). */
  readonly isOwnDeposit: (address: string) => boolean;
  readonly maxSingleWei: bigint | null;
  readonly maxDailyWei: bigint | null;
  /**
   * ETH the fuel key already signed away in the last 24 h (journal): each
   * fuel's value plus its own worst-case fee (`gasLimit × maxFeePerGas`) — the
   * daily cap counts both (yönetici kararı, T153 doğrulaması bulgu a). This
   * transaction's value and worst-case fee are added on top here.
   */
  readonly spentLast24hWei: bigint;
  /**
   * T153 — the most this ONE fuel may carry, computed by the caller
   * independently of the amount: the fee budget of the token transaction it
   * pays for (`gasLimit × maxFeePerGas` of that send) less the deposit's ETH
   * that is this request's to spend (balance − what the deposit's other
   * sends and other requests' fuel reserve). Fuel above what the send lacks
   * is never signed (08 §3.7.5 "tam gereken kadar", 11 T153 (ii)); a bump
   * re-signs the recorded amount and passes it here.
   */
  readonly maxForSendWei: bigint;
  /** Platform wallets (hot, cold, fuel) — never a fuel destination. */
  readonly platformAddresses: readonly string[];
  /** The fuel send's share of the flow's fee cap, wei: `gasLimit × maxFeePerGas` may not exceed it. */
  readonly feeCapWei: bigint;
  /**
   * T159a — the gas limit the caller decided for this fuel: its
   * `eth_estimateGas` to this deposit (the recipient's account state
   * included), or the recorded one on a fee bump. The transaction must carry
   * exactly it.
   */
  readonly gasLimit: bigint;
}

/** The intrinsic gas of a plain ETH send — the lowest gas limit a fuel may carry (T159a: a floor, not the limit). */
export { NATIVE_TRANSFER_GAS };

function refuse(rule: string, detail: string): never {
  throw new SidecarError(
    `Signer policy refused the transaction (${rule}): ${detail}`,
    'SIGNER_POLICY_REJECTED',
    false,
  );
}

function effectiveFeeCeiling(tx: Transaction): bigint {
  return tx.type === 2 ? (tx.maxFeePerGas ?? 0n) : (tx.gasPrice ?? 0n);
}

function assertCommon(tx: Transaction, config: SignerPolicyConfig): void {
  if (tx.signature !== null) refuse('unsigned', 'the transaction already carries a signature');
  if (tx.type !== 0 && tx.type !== 2) {
    refuse('type', `type ${String(tx.type)} is not signable — only 0 and 2 (never 4 / EIP-7702)`);
  }
  if (tx.chainId !== config.chainId) {
    refuse('chainId', `chainId ${tx.chainId} is not the configured ${config.chainId}`);
  }
  if (tx.to === null) refuse('to', 'contract creation is not signable');
  if (tx.type === 2) {
    if ((tx.accessList ?? []).length > 0) refuse('accessList', 'an access list is not signable');
    const maxFee = tx.maxFeePerGas ?? 0n;
    const tip = tx.maxPriorityFeePerGas ?? 0n;
    if (maxFee <= 0n || tip < 0n || tip > maxFee) {
      refuse('fees', 'maxFeePerGas must be positive and at least maxPriorityFeePerGas');
    }
  } else if ((tx.gasPrice ?? 0n) <= 0n) {
    refuse('fees', 'gasPrice must be positive');
  }
  if (tx.gasLimit <= 0n) refuse('gasLimit', 'gas limit must be positive');
}

/** Token keys (hot wallet, deposit): one `transfer` call, nothing else. */
export function assertSignableTokenTransfer(
  tx: Transaction,
  intent: TokenTransferIntent,
  config: SignerPolicyConfig,
): void {
  assertCommon(tx, config);
  const to = canonicalAddress(tx.to ?? '');
  const allowed = new Set(
    [config.paymentToken, intent.wrongTokenRefund ? config.wrongTokenRefundToken : '']
      .filter((c) => c !== '')
      .map(canonicalAddress),
  );
  if (!allowed.has(to)) {
    refuse('contract', `${to} is not an allowlisted token contract for this flow`);
  }
  if (!sameAddress(to, intent.contract)) {
    refuse('contract', `${to} is not the contract this request names`);
  }
  if (tx.value !== 0n) refuse('value', 'a token transfer carries no ETH');
  const call = decodeTransferCall(tx.data);
  if (call === null) refuse('calldata', 'only transfer(address,uint256) is signable');
  if (!sameAddress(call.to, intent.recipient)) {
    refuse(
      'recipient',
      `calldata pays ${call.to}, the request names ${canonicalAddress(intent.recipient)}`,
    );
  }
  if (call.amountUnits !== intent.amountUnits) {
    refuse('amount', `calldata moves ${call.amountUnits}, the request names ${intent.amountUnits}`);
  }
  if (call.amountUnits <= 0n) refuse('amount', 'amount must be positive');
  if (tx.gasLimit > config.maxGasLimit) {
    refuse('gasLimit', `gas limit ${tx.gasLimit} is above ${config.maxGasLimit}`);
  }
  if (intent.feeCapWei !== undefined && tx.gasLimit * effectiveFeeCeiling(tx) > intent.feeCapWei) {
    refuse(
      'feeCap',
      `worst-case fee ${tx.gasLimit} gas × ${effectiveFeeCeiling(tx)} wei/gas is above the transaction's cap ${intent.feeCapWei} wei`,
    );
  }
}

/** The fuel key (T153): plain ETH to the service's own deposit, within its caps. */
export function assertSignableFuel(
  tx: Transaction,
  intent: FuelIntent,
  config: SignerPolicyConfig,
): void {
  assertCommon(tx, config);
  const to = canonicalAddress(tx.to ?? '');
  if (!sameAddress(to, intent.recipient)) refuse('recipient', 'transaction and request disagree');
  if (!intent.isOwnDeposit(to)) {
    refuse('recipient', `${to} is not an address this service derives from its own seed`);
  }
  if (intent.platformAddresses.some((a) => a !== '' && sameAddress(a, to))) {
    refuse('recipient', `${to} is a platform wallet, not a deposit`);
  }
  if (tx.data !== '0x') refuse('calldata', 'a fuel send carries no calldata');
  if (tx.gasLimit !== intent.gasLimit) {
    refuse(
      'gasLimit',
      `gas limit ${tx.gasLimit} is not the ${intent.gasLimit} decided for this fuel`,
    );
  }
  if (tx.gasLimit < NATIVE_TRANSFER_GAS || tx.gasLimit > config.maxGasLimit) {
    refuse(
      'gasLimit',
      `a fuel send's gas limit must be within ${NATIVE_TRANSFER_GAS}…${config.maxGasLimit}, not ${tx.gasLimit}`,
    );
  }
  if (tx.value <= 0n || tx.value !== intent.amountWei) {
    refuse('value', 'fuel value must be positive and the amount asked for');
  }
  if (tx.gasLimit * effectiveFeeCeiling(tx) > intent.feeCapWei) {
    refuse(
      'feeCap',
      `worst-case fee ${tx.gasLimit} gas × ${effectiveFeeCeiling(tx)} wei/gas is above the fuel send's cap ${intent.feeCapWei} wei`,
    );
  }
  if (tx.value > intent.maxForSendWei) {
    refuse(
      'amount',
      `fuel ${tx.value} wei is more than the send it pays for needs (${intent.maxForSendWei} wei)`,
    );
  }
  if (intent.maxSingleWei === null || intent.maxDailyWei === null) {
    refuse('fuelCaps', 'fuel single / daily caps are not configured (fail closed)');
  }
  if (tx.value > intent.maxSingleWei) {
    refuse('fuelCaps', `fuel ${tx.value} wei is above the single-send cap ${intent.maxSingleWei}`);
  }
  const outflow = intent.spentLast24hWei + tx.value + tx.gasLimit * effectiveFeeCeiling(tx);
  if (outflow > intent.maxDailyWei) {
    refuse(
      'fuelCaps',
      `fuel value + fee would reach ${outflow} wei in 24 h, above ${intent.maxDailyWei}`,
    );
  }
}
