import { SigningKey, Transaction, computeAddress } from 'ethers';
import { canonicalAddress, sameAddress } from '../address/canonicalAddress.js';
import { SidecarError } from '../errors/SidecarError.js';
import { NATIVE_TRANSFER_GAS, isPlausibleGasEstimate, tokenGasLimit } from '../fee/feeMath.js';
import { logger } from '../logger.js';
import {
  fuelSendsTotal,
  gasEstimateAbnormalTotal,
  transferStatusUnverifiedTotal,
} from '../metrics.js';
import {
  RpcError,
  isExecutionRevert,
  type BlockHeader,
  type EthereumRpcClient,
  type TransactionReceipt,
} from '../rpc/RpcClient.js';
import type { EthereumHdWallet } from '../wallet/HdWalletService.js';
import { TOKEN_ABI, TRANSFER_TOPIC, addressTopic, encodeTransfer } from './erc20.js';
import {
  TransferDeferredError,
  capShareWei,
  cappedMaxFee,
  feeCeilingPerGas,
  quoteFees,
  type CappedFlow,
  type EthUsdPriceSource,
  type FeeQuote,
} from './FeePolicy.js';
import type { FeeWaitTracker } from './FeeWaitTracker.js';
import { KeyedMutex } from './KeyedMutex.js';
import {
  assertSignableFuel,
  assertSignableTokenTransfer,
  type FuelIntent,
  type SignerPolicyConfig,
} from './SignerPolicy.js';
import { assertUsdtSendable } from './TokenSafety.js';
import type { TransferGuard } from './TransferGuard.js';
import { recordFeeCapWei } from './TxJournal.js';
import type {
  JournalAttempt,
  JournalRecord,
  MinedState,
  SignerRole,
  TransferPurpose,
} from './TxJournal.js';
import type { TxJournal } from './TxJournal.js';
import { formatTokenUnits, toTokenUnits, usdToMicros } from './units.js';

/**
 * Outbound transfers of the Ethereum service — sweep, payout, refund, cold
 * consolidation (T152, 08 §3.7.6, §3.7.7).
 *
 * <para>
 * <b>Double payment is made impossible by construction</b> (08 §3.7.6):
 * <list type="number">
 *   <item>A request id (`BlockchainTransaction.Id`, or the cold transfer's id)
 *     gets one journal record and therefore one nonce, ever. A second call with
 *     the same id — backend timeout, retry, a parallel tick, a restart —
 *     returns the recorded hash and re-broadcasts the recorded bytes; with
 *     different parameters it is refused (`IDEMPOTENCY_CONFLICT`). The journal
 *     is asked first, before any check that reads configuration, and under a
 *     lock per id: a second request with the same id — even another flow —
 *     waits for the first and never signs (T152 düzeltme turu 2).</item>
 *   <item>Every send of one account runs under that account's lock; the nonce
 *     is max(chain count incl. pending, journal's highest + 1). If the chain
 *     is AHEAD of what the journal knows for that account (a lost journal, an
 *     old backup, a send outside the service), nothing is signed: 503
 *     `JOURNAL_BEHIND_CHAIN` and a critical alert, until an operator
 *     acknowledges (yönetici kararı, T152 doğrulaması).</item>
 *   <item>The signed bytes are journalled (fsync) <b>before</b> broadcast. Once
 *     journalled, the send is the service's to deliver: the call answers 200
 *     with the hash even when this broadcast attempt failed, and the keeper
 *     re-broadcasts until it is mined — so the backend never marks a row
 *     `FAILED` (and an operator never re-queues it) while signed bytes can
 *     still land.</item>
 *   <item>A fee bump is a new signature at the SAME nonce; status reads look
 *     at every attempt, so whichever enters the chain is reported.</item>
 *   <item>If the account's nonce moves past a record with none of its attempts
 *     mined (on two reads one keeper interval apart), something outside this
 *     journal used the nonce — key compromise or a manual send. The record is
 *     marked `conflict`, nothing is re-signed automatically, status answers 409
 *     and an alert fires.</item>
 * </list>
 * </para>
 *
 * <para>
 * <b>Before signing</b>, at one explicitly numbered `latest` block: USDT state
 * and recipient blacklist (TokenSafety), daily limit (payouts), fee quote, gas
 * estimate and an `eth_call` simulation from the sender (a revert — e.g. a
 * blacklisted SENDER — refuses; any other node error is transient), the fee
 * cap per transaction (sweep / payout / refund / cold wait above it), the
 * journal-behind-chain check and the sender's ETH for gas.
 * </para>
 *
 * <para>
 * <b>No gas is assumed (T159a — yönetici kararı, ⚠ PARA).</b> Every gas limit
 * comes from `eth_estimateGas` of exactly that transaction at the priced
 * block: the token send's (+10 %), and in a deposit flow the fuel's — a plain
 * ETH send from the fuel wallet to this deposit, so the deposit's account
 * state is in it (Sepolia 2026-10-19: 207,391 to a new account, 21,000 to an
 * existing one). The fee cap is shared out by those estimates. An estimate
 * the node cannot give (a transient RPC answer) makes the send WAIT
 * (`GAS_ESTIMATE_UNAVAILABLE`, never FAILED — proje sahibi kararı); one
 * outside 21,000…1,000,000 is not believed (`GAS_ESTIMATE_ABNORMAL`, nothing
 * signed, alarm); a revert of the token send's estimate stays a refusal (the
 * chain's answer about the transfer).
 * </para>
 *
 * <para>
 * <b>Fuel (T153, 08 §3.7.5).</b> A deposit pays its own gas in ETH it does
 * not hold, so a sweep or a refund before the sweep is preceded by a fuel
 * send from the fuel wallet — and only by this path: there is no fuel
 * endpoint. When every check above has passed and the deposit (after what
 * its other sends and other requests' unspent fuel reserve) holds less than
 * the send's worst-case fee (`gasLimit × maxFeePerGas`), the fuel key signs
 * <b>exactly the missing amount</b> to the deposit — re-derived from this
 * service's seed, never a platform wallet — under its own journal id
 * (`fuel:<request id>`: one fuel per request, ever; a retry, a parallel tick
 * or a restart finds it), within its share of the fee cap (its estimated gas
 * of the flow's estimated gas, T159a), the fuel single-send and 24-hour caps and the fuel
 * wallet's balance. The request answers 503 `FUEL_PENDING`; the backend asks
 * again a minute later and the token send is signed only when the fuel is
 * <b>in a block at or below the priced head</b> (and so in the deposit's
 * balance) — never before. A fuel mined but short of a later, higher fee is
 * spent as it is (the send is signed at the fee it affords) or the send
 * waits; nothing tops it up. Any reason fuel cannot go out is a wait (503
 * `deferred`), never a refusal: a refused sweep would be re-queued with a new
 * id and fuelled twice.
 * </para>
 */

export type TokenSymbol = 'USDT' | 'USDC';

export const REFUND_TYPES = [
  'BUYER_REFUND',
  'EXCESS_REFUND',
  'WRONG_TOKEN_REFUND',
  'INCORRECT_AMOUNT_REFUND',
  'LATE_PAYMENT_REFUND',
] as const;
export type RefundType = (typeof REFUND_TYPES)[number];

export interface PayoutRequest {
  blockchainTransactionId: string;
  toAddress: string;
  amount: string;
  token: TokenSymbol;
  feeCapUsd: string;
}

/**
 * T158 (f) — where a refund leaves from (08 §3.7.6 "Kaynaklar"): the deposit
 * before the sweep, the HOT wallet after it (the deposit is empty then). The
 * backend decides it from its sweep row; absent = `DEPOSIT` (the pre-T158 body).
 */
export type RefundSource = 'DEPOSIT' | 'HOT_WALLET';

export interface RefundRequest {
  blockchainTransactionId: string;
  /** T158 (f) — default `DEPOSIT`. */
  source?: RefundSource;
  /** Required for a `DEPOSIT` refund; must be absent (null) for a `HOT_WALLET` one. */
  depositIndex: number | null;
  depositAddress: string | null;
  toBuyerAddress: string;
  amount: string;
  token: TokenSymbol;
  refundType: RefundType;
  /** T152 düzeltme turu — refunds are capped too (yönetici kararı 2026-10-18). */
  feeCapUsd: string;
}

export interface SweepRequest {
  blockchainTransactionId: string;
  depositIndex: number;
  depositAddress: string;
  toHotWalletAddress: string;
  amount: string;
  token: TokenSymbol;
  feeCapUsd: string;
}

export interface ColdTransferRequest {
  coldTransferId: string;
  toColdAddress: string;
  amount: string;
  token: TokenSymbol;
  feeCapUsd: string;
}

export interface SendResult {
  readonly txHash: string;
  readonly nonce: number;
  /** True when the id was already journalled — nothing new was signed. */
  readonly replayed: boolean;
}

/**
 * T158 (g) — what the backend's row says the transfer is, sent with every
 * status read (`?contract=&from=&to=&amount=`). Used only for a hash this
 * service's journal does not hold (a lost or restored journal): the receipt's
 * `Transfer` log must then match it, or the answer is `UNKNOWN`.
 */
export interface TransferExpectation {
  /** Canonical token contract (must be one of this service's). */
  readonly contract: string;
  /** Canonical sender (the deposit or the hot wallet). */
  readonly from: string;
  /** Canonical recipient. */
  readonly to: string;
  /** Raw token units (6 decimals). */
  readonly amountUnits: bigint;
}

export interface TransferStatus {
  readonly txHash: string;
  readonly blockNumber: number | null;
  /**
   * `SUCCESS`, `REVERT`, `NO_TRANSFER_LOG`; `UNKNOWN` (T158 (g)) for a mined
   * hash the journal does not hold whose `Transfer` log does not match the
   * backend's expectation (or no expectation was sent) — fail closed: the
   * backend never confirms it. Null while not mined.
   */
  readonly contractRet: string | null;
  /** Blocks from the transaction's block to `finalized`; -1 while not mined. */
  readonly confirmations: number;
  readonly finalized: boolean;
  /**
   * T154 karar (vi) — the fee the final transaction paid, wei, as a decimal
   * string: receipt `gasUsed × effectiveGasPrice`. Null until final (and when
   * the receipt has no effective price). `contracts/transfer-status/`.
   */
  readonly realizedFeeWei: string | null;
}

/** T153 — the fuel wallet (08 §3.7.5). */
export interface FuelDeps {
  /** Fuel wallet signing key, or null while not provisioned (then every fuel waits). Never logged. */
  readonly key: string | null;
  /** `ETHEREUM_FUEL_MAX_SINGLE_ETH` / `_DAILY_ETH` in wei; null = no fuel (the send waits). */
  readonly maxSingleWei: bigint | null;
  readonly maxDailyWei: bigint | null;
  /** Canonical cold wallet (with the hot wallet and the fuel wallet itself: never a fuel destination). */
  readonly coldWalletAddress: string | null;
  /** A fuel transaction seen in a block — the reconciliation's FuelLedger (T151) is fed from here. */
  readonly onMined?: (deposit: string, wei: bigint, block: bigint, hash: string) => void;
}

/** 24 hours, for the fuel daily cap (counted from the journal — T152 karar (xii)). */
export const FUEL_DAILY_WINDOW_MS = 24 * 60 * 60 * 1000;

export interface TransferServiceDeps {
  rpc: Pick<
    EthereumRpcClient,
    | 'getBlockHeader'
    | 'getBalance'
    | 'call'
    | 'callFrom'
    | 'estimateGas'
    | 'getTransactionCount'
    | 'getPendingTransactionCount'
    | 'medianPriorityFee'
    | 'sendRawTransaction'
    | 'getTransactionReceipt'
  >;
  journal: TxJournal;
  feeWaits: FeeWaitTracker;
  guard: TransferGuard;
  wallet: Pick<EthereumHdWallet, 'deriveSigner' | 'derive'>;
  policy: SignerPolicyConfig;
  /** Hot wallet signing key, or null while not provisioned. Never logged. */
  hotWalletKey: string | null;
  /** Canonical hot wallet address (the key's own, checked at startup). */
  hotWalletAddress: string | null;
  tokenContracts: { readonly USDT: string; readonly USDC: string };
  prices: EthUsdPriceSource;
  /** Number of the last `finalized` block the service read (FinalityTracker), or null. */
  finalizedHead: () => bigint | null;
  /** A head-of-line transaction not mined after this long is re-signed with higher fees. */
  bumpAfterMs: number;
  /** A nonce conflict is declared only when seen again at least this much later. */
  conflictConfirmMs: number;
  /**
   * Operator acknowledgements of nonces used outside the journal
   * (`ETHEREUM_JOURNAL_ACKNOWLEDGED_NONCES`, DEPLOY_RUNBOOK §D): canonical
   * address → the account nonce up to which the chain is accounted for.
   */
  acknowledgedNonces?: ReadonlyMap<string, number>;
  /** T153 — absent: no fuel is ever sent (a deposit without ETH waits, `FUEL_REQUIRED`). */
  fuel?: FuelDeps;
  now?: () => number;
}

/** What the deposit's send needs, as priced in signNew (T153). */
interface FuelContext {
  readonly head: BlockHeader;
  readonly quote: FeeQuote;
  readonly gasLimit: bigint;
  /**
   * T159a — the fuel send's gas limit: its `eth_estimateGas` to this deposit
   * (or the recorded fuel's); the cap is shared by `gasLimit + fuelGasLimit`.
   */
  readonly fuelGasLimit: bigint;
  /** The token send's fee ceiling per gas (capped). */
  readonly maxFeePerGas: bigint;
  /** ETH on the deposit not reserved by its other sends (≥ 0). */
  readonly available: bigint;
  /** ETH price of the cap conversion (null: uncapped — never for a deposit flow). */
  readonly ethUsdMicros: bigint | null;
}

interface SendPlan {
  readonly id: string;
  readonly purpose: TransferPurpose;
  readonly refundType: RefundType | null;
  readonly signer: SignerRole;
  readonly depositIndex: number | null;
  readonly from: string;
  readonly contract: string;
  readonly token: TokenSymbol;
  readonly recipient: string;
  readonly amountUnits: bigint;
  readonly capFlow: CappedFlow | null;
  readonly capUsdMicros: bigint | null;
}

/** What a request IS — compared with a journalled record; nothing here reads configuration. */
interface RequestIdentity {
  readonly purpose: TransferPurpose;
  readonly token: TokenSymbol;
  readonly recipient: string;
  readonly amountUnits: bigint;
  readonly depositIndex: number | null;
  /** Canonical deposit (the sender of a deposit flow), null for hot-wallet flows. */
  readonly depositAddress: string | null;
  readonly refundType: RefundType | null;
  /** T158 (f) — which key signs: a deposit refund and a hot-wallet refund are different requests. */
  readonly signer: SignerRole;
}

/** An account whose chain nonce is ahead of what the journal knows (journal-behind-chain). */
export interface JournalBehind {
  readonly from: string;
  readonly chainNonce: number;
  readonly journalNext: number;
  readonly detectedAt: number;
}

/**
 * An operator acknowledgement (`ETHEREUM_JOURNAL_ACKNOWLEDGED_NONCES`) above
 * the account's nonce on chain (pending included) — a typo or a stale value.
 * It is ignored, so it cannot open a stopped account (T152 doğrulaması,
 * yönetici kararı (a)); alert `ethereum-journal-ack-ahead-of-chain`.
 */
export interface AcknowledgementAhead {
  readonly from: string;
  readonly acknowledged: number;
  readonly chainNonce: number;
  readonly detectedAt: number;
}

/**
 * T159a — the value of the fuel's gas probe: `eth_estimateGas` of a plain ETH
 * send from the fuel wallet to the deposit. What a fuel costs in gas depends
 * on whether the send creates the account (any value above 0 does), not on
 * how much it carries, and the real amount is only known once the cap — which
 * needs this estimate — is shared out; 1 wei is the smallest value that
 * creates the account. The node checks the fuel wallet holds it.
 */
const FUEL_ESTIMATE_PROBE_WEI = 1n;

/** EIP-1559 replacement: both fee fields at least +10 % (geth); we bump by 12.5 %. */
const BUMP_NUMERATOR = 1125n;
const BUMP_DENOMINATOR = 1000n;

const ALREADY_KNOWN = /already known|known transaction|already imported|alreadyknown/i;

function ceilDiv(a: bigint, b: bigint): bigint {
  return (a + b - 1n) / b;
}

function maxBig(a: bigint, b: bigint): bigint {
  return a > b ? a : b;
}

/**
 * T152 düzeltme turu 2 (bulgu 2): a JSON-RPC answer that is not the EVM's
 * revert (`header not found`, `missing trie node`, a node's internal error)
 * says nothing about the transfer — it is transient (502), never a refusal
 * (400 → backend `FAILED`).
 */
function transientUnlessRevert(err: unknown): unknown {
  if (err instanceof RpcError && err.kind === 'rpc' && !isExecutionRevert(err)) {
    return new SidecarError(
      `${err.message} — a node-side error, not an answer about the transfer; nothing signed, retried.`,
      'RPC_UNAVAILABLE',
      true,
    );
  }
  return err;
}

export class TransferService {
  /** Per sender: nonce reads and journal writes of one account never interleave. */
  private readonly locks = new KeyedMutex();
  /** Per request id: a second request with the same id waits for the first (bulgu 4). */
  private readonly idLocks = new KeyedMutex();
  private readonly behind = new Map<string, JournalBehind>();
  private readonly ackAhead = new Map<string, AcknowledgementAhead>();
  private readonly now: () => number;
  /** Canonical fuel wallet address (from its key), or null. */
  private readonly fuelAddress: string | null;

  constructor(private readonly deps: TransferServiceDeps) {
    this.now = deps.now ?? (() => Date.now());
    this.fuelAddress = deps.fuel?.key ? canonicalAddress(computeAddress(deps.fuel.key)) : null;
  }

  // ---- Requests ---------------------------------------------------------------
  //
  // T152 düzeltme turu 2 (bulgu 1): an id the journal already holds is
  // answered from the journal BEFORE any check that reads configuration — the
  // hot key, the pinned destinations, the single-transfer cap, the fee cap in
  // the request, the deposit derivation. Once signed, the bytes are the
  // service's to deliver (08 §3.7.6); a check that reads today's settings
  // would answer 400 to a send already on its way, the backend would mark the
  // row FAILED and a re-opened row with a new id would pay twice. Only the
  // request's own identity (purpose, token, recipient, amount, deposit, refund
  // type) is compared with the record: a different one is still
  // IDEMPOTENCY_CONFLICT.

  async payout(request: PayoutRequest): Promise<SendResult> {
    const units = toTokenUnits(request.amount);
    const identity: RequestIdentity = {
      purpose: 'PAYOUT',
      token: request.token,
      recipient: canonicalAddress(request.toAddress),
      amountUnits: units,
      depositIndex: null,
      depositAddress: null,
      refundType: null,
      signer: 'HOT',
    };
    return this.submit(`transfer:${request.blockchainTransactionId}`, identity, (id) => {
      const hot = this.requireHotWallet();
      this.requireToken(request.token, 'USDT', 'payout');
      this.deps.guard.assertSingleTransferLimit(units);
      return {
        id,
        purpose: 'PAYOUT',
        refundType: null,
        signer: 'HOT',
        depositIndex: null,
        from: hot,
        contract: this.contractOf('USDT'),
        token: 'USDT',
        recipient: this.recipient(request.toAddress, hot),
        amountUnits: units,
        capFlow: 'PAYOUT',
        capUsdMicros: this.capOf(request.feeCapUsd),
      };
    });
  }

  async refund(request: RefundRequest): Promise<SendResult> {
    if ((request.source ?? 'DEPOSIT') === 'HOT_WALLET') return this.hotWalletRefund(request);
    if (request.depositIndex === null || request.depositAddress === null) {
      throw new SidecarError(
        'A deposit refund needs depositIndex and depositAddress.',
        'INVALID_TRANSFER_REQUEST',
        false,
      );
    }
    const depositIndex = request.depositIndex;
    const depositAddress = request.depositAddress;
    const units = toTokenUnits(request.amount);
    const identity: RequestIdentity = {
      purpose: 'REFUND',
      token: request.token,
      recipient: canonicalAddress(request.toBuyerAddress),
      amountUnits: units,
      depositIndex,
      depositAddress: canonicalAddress(depositAddress),
      refundType: request.refundType,
      signer: 'DEPOSIT',
    };
    return this.submit(`transfer:${request.blockchainTransactionId}`, identity, (id) => {
      const wrongToken = request.refundType === 'WRONG_TOKEN_REFUND';
      if (request.token === 'USDC' && !wrongToken) {
        throw new SidecarError(
          'USDC may only be moved by a WRONG_TOKEN_REFUND (08 §3.7.6).',
          'TOKEN_NOT_ALLOWED',
          false,
        );
      }
      this.deps.guard.assertSingleTransferLimit(units);
      const deposit = this.depositOf(depositIndex, depositAddress);
      return {
        id,
        purpose: 'REFUND',
        refundType: request.refundType,
        signer: 'DEPOSIT',
        depositIndex,
        from: deposit,
        contract: this.contractOf(request.token),
        token: request.token,
        recipient: this.recipient(request.toBuyerAddress, deposit),
        amountUnits: units,
        capFlow: 'REFUND',
        capUsdMicros: this.capOf(request.feeCapUsd),
      };
    });
  }

  /**
   * T158 (f) — a refund after the sweep (08 §3.7.6 "Kaynaklar"): the deposit
   * is empty, the money is in the hot wallet, so the hot key signs it —
   * payout-shaped: USDT only (a wrong token is never swept, it is refunded
   * from the deposit), the single-transfer cap, the hot wallet's daily cap
   * (it is hot-wallet outflow), the whole fee cap (no fuel), the hot wallet's
   * own ETH for gas. A different request than a deposit refund under the
   * same id (`IDEMPOTENCY_CONFLICT`): the source is part of what was signed.
   */
  private async hotWalletRefund(request: RefundRequest): Promise<SendResult> {
    if (request.depositIndex !== null || request.depositAddress !== null) {
      throw new SidecarError(
        'A HOT_WALLET refund names no deposit (depositIndex / depositAddress must be absent).',
        'INVALID_TRANSFER_REQUEST',
        false,
      );
    }
    const units = toTokenUnits(request.amount);
    const identity: RequestIdentity = {
      purpose: 'REFUND',
      token: request.token,
      recipient: canonicalAddress(request.toBuyerAddress),
      amountUnits: units,
      depositIndex: null,
      depositAddress: null,
      refundType: request.refundType,
      signer: 'HOT',
    };
    return this.submit(`transfer:${request.blockchainTransactionId}`, identity, (id) => {
      const hot = this.requireHotWallet();
      if (request.refundType === 'WRONG_TOKEN_REFUND') {
        throw new SidecarError(
          'A wrong token is never swept — its refund leaves the deposit, not the hot wallet (08 §3.7.6).',
          'TOKEN_NOT_ALLOWED',
          false,
        );
      }
      this.requireToken(request.token, 'USDT', 'hot-wallet refund');
      this.deps.guard.assertSingleTransferLimit(units);
      return {
        id,
        purpose: 'REFUND',
        refundType: request.refundType,
        signer: 'HOT',
        depositIndex: null,
        from: hot,
        contract: this.contractOf('USDT'),
        token: 'USDT',
        recipient: this.recipient(request.toBuyerAddress, hot),
        amountUnits: units,
        capFlow: 'HOT_REFUND',
        capUsdMicros: this.capOf(request.feeCapUsd),
      };
    });
  }

  async sweep(request: SweepRequest): Promise<SendResult> {
    const units = toTokenUnits(request.amount);
    const identity: RequestIdentity = {
      purpose: 'SWEEP',
      token: request.token,
      recipient: canonicalAddress(request.toHotWalletAddress),
      amountUnits: units,
      depositIndex: request.depositIndex,
      depositAddress: canonicalAddress(request.depositAddress),
      refundType: null,
      signer: 'DEPOSIT',
    };
    return this.submit(`transfer:${request.blockchainTransactionId}`, identity, (id) => {
      // T160c — USDT (the sale's sweep and the refund fees it withheld) or
      // USDC (the refund fee a wrong-token refund withheld in the token it
      // returned). Either way the destination is the pinned hot wallet, and
      // the signer policy allows USDC for no other deposit flow but a
      // wrong-token refund.
      this.deps.guard.assertSweepDestination(request.toHotWalletAddress);
      const deposit = this.depositOf(request.depositIndex, request.depositAddress);
      return {
        id,
        purpose: 'SWEEP',
        refundType: null,
        signer: 'DEPOSIT',
        depositIndex: request.depositIndex,
        from: deposit,
        contract: this.contractOf(request.token),
        token: request.token,
        recipient: canonicalAddress(request.toHotWalletAddress),
        amountUnits: units,
        capFlow: 'SWEEP',
        capUsdMicros: this.capOf(request.feeCapUsd),
      };
    });
  }

  async coldWalletTransfer(request: ColdTransferRequest): Promise<SendResult> {
    const units = toTokenUnits(request.amount);
    const identity: RequestIdentity = {
      purpose: 'COLD',
      token: request.token,
      recipient: canonicalAddress(request.toColdAddress),
      amountUnits: units,
      depositIndex: null,
      depositAddress: null,
      refundType: null,
      signer: 'HOT',
    };
    return this.submit(`cold:${request.coldTransferId}`, identity, (id) => {
      const hot = this.requireHotWallet();
      this.requireToken(request.token, 'USDT', 'cold transfer');
      this.deps.guard.assertColdDestination(request.toColdAddress);
      return {
        id,
        purpose: 'COLD',
        refundType: null,
        signer: 'HOT',
        depositIndex: null,
        from: hot,
        contract: this.contractOf('USDT'),
        token: 'USDT',
        recipient: canonicalAddress(request.toColdAddress),
        amountUnits: units,
        capFlow: 'COLD',
        capUsdMicros: this.capOf(request.feeCapUsd),
      };
    });
  }

  // ---- Status (GET /api/transfer/status/:txHash) -----------------------------

  async status(
    txHash: string,
    expected: TransferExpectation | null = null,
  ): Promise<TransferStatus> {
    const record = this.deps.journal.findByHash(txHash);
    if (record?.conflict) {
      throw new SidecarError(
        `The nonce of ${txHash} was used by a transaction this service did not sign — not delivered; investigate (08 §3.7.6).`,
        'TRANSFER_NONCE_CONFLICT',
        false,
      );
    }
    const receipt = record
      ? await this.findMined(record)
      : await this.deps.rpc.getTransactionReceipt(txHash);
    if (!receipt) {
      if (!record) {
        throw new SidecarError(
          `Transaction ${txHash} is neither in the journal nor on chain.`,
          'TRANSFER_NOT_FOUND',
          false,
        );
      }
      return {
        txHash: record.attempts[record.attempts.length - 1].hash,
        blockNumber: null,
        contractRet: null,
        confirmations: -1,
        finalized: false,
        realizedFeeWei: null,
      };
    }
    const finalized = this.deps.finalizedHead();
    const final = await this.isFinal(receipt);
    return {
      txHash: receipt.transactionHash,
      blockNumber: Number(receipt.blockNumber),
      contractRet: record
        ? contractRetOf(record, receipt)
        : this.unjournalledContractRet(txHash, receipt, expected),
      confirmations: final && finalized !== null ? Number(finalized - receipt.blockNumber + 1n) : 0,
      finalized: final,
      realizedFeeWei:
        final && receipt.effectiveGasPrice !== null
          ? (receipt.gasUsed * receipt.effectiveGasPrice).toString()
          : null,
    };
  }

  /**
   * T158 (g) — a mined hash the journal does not hold (lost or restored
   * journal; 08 §3.7.6 "Kesinleşme"). Nothing this service knows says what
   * it was meant to move, so status 1 alone is not a success: the receipt
   * must carry the `Transfer` log the backend's row expects — from one of
   * this service's token contracts, from the expected sender, to the expected
   * recipient, for the exact amount, not removed. Otherwise `UNKNOWN`: the
   * backend leaves the row unconfirmed (never CONFIRMED, never FAILED) and the
   * `ethereum-transfer-status-unverified` alarm fires. A revert stays REVERT
   * (the chain says the transfer did not happen).
   */
  private unjournalledContractRet(
    txHash: string,
    receipt: TransactionReceipt,
    expected: TransferExpectation | null,
  ): string {
    if (receipt.status !== 1) return 'REVERT';
    const known =
      expected !== null &&
      (sameAddress(expected.contract, this.deps.tokenContracts.USDT) ||
        (this.deps.tokenContracts.USDC !== '' &&
          sameAddress(expected.contract, this.deps.tokenContracts.USDC)));
    const verified =
      expected !== null &&
      known &&
      hasTransferLog(receipt, expected.contract, expected.from, expected.to, expected.amountUnits);
    if (verified) {
      logger.warn(
        { txHash, from: expected.from, to: expected.to },
        'Status of a hash the journal does not hold — its Transfer log matches the backend row; SUCCESS',
      );
      return 'SUCCESS';
    }
    const reason =
      expected === null ? 'no_expectation' : known ? 'no_matching_log' : 'unknown_contract';
    transferStatusUnverifiedTotal.inc({ reason });
    logger.error(
      {
        txHash,
        reason,
        expected:
          expected === null
            ? null
            : {
                contract: expected.contract,
                from: expected.from,
                to: expected.to,
                amountUnits: expected.amountUnits.toString(),
              },
      },
      'UNVERIFIED TRANSFER — a mined hash the journal does not hold does not carry the Transfer the backend expects; UNKNOWN (the row is not confirmed; investigate, DEPLOY_RUNBOOK §J)',
    );
    return 'UNKNOWN';
  }

  // ---- Keeper (re-broadcast, fee bump, finality, conflicts) -------------------

  /** One pass over every unsettled record. Never throws. */
  async keepAll(): Promise<void> {
    for (const record of this.deps.journal.unsettled()) {
      try {
        await this.locks.run(record.from, () => this.keep(record.id));
      } catch (err) {
        logger.warn(
          { id: record.id, reason: (err as Error).message },
          'Outgoing transfer keeper step failed — retried next pass',
        );
      }
    }
  }

  /** Longest time a signed transfer has been waiting to be mined, seconds (0 when none). */
  oldestUnminedSeconds(): number {
    const now = this.now();
    let longest = 0;
    for (const record of this.deps.journal.unsettled()) {
      if (record.mined) continue;
      longest = Math.max(longest, Math.floor((now - record.createdAt) / 1000));
    }
    return longest;
  }

  /** T153 — the fuel wallet's address (from its key), or null while not provisioned. */
  fuelWalletAddress(): string | null {
    return this.fuelAddress;
  }

  /**
   * T153 — ETH the fuel wallet signed away in the last 24 h, wei (what the
   * daily cap counts): each fuel's value plus its own worst-case fee
   * (its recorded gas limit × `maxFeePerGas` of its latest attempt — a fee bump replaces the
   * earlier price, it is not added to it). Yönetici kararı (T153 doğrulaması,
   * bulgu a): the fuel's fee leaves the platform too, so it is under the cap.
   */
  fuelSpentLast24hWei(excludeId: string | null = null): bigint {
    const since = this.now() - FUEL_DAILY_WINDOW_MS;
    let sum = 0n;
    for (const record of this.deps.journal.all()) {
      // Conservative: a reverted or conflicted fuel still counts — it was signed.
      if (record.purpose !== 'FUEL' || record.createdAt < since || record.id === excludeId)
        continue;
      const latest = record.attempts[record.attempts.length - 1];
      sum += BigInt(record.amountUnits) + BigInt(record.gasLimit) * BigInt(latest.maxFeePerGas);
    }
    return sum;
  }

  nonceConflictCount(): number {
    return this.deps.journal.all().filter((r) => r.conflict !== null).length;
  }

  /** Accounts whose last pre-signing check found the chain ahead of the journal. */
  journalBehindChain(): JournalBehind[] {
    return [...this.behind.values()];
  }

  /** Accounts whose configured acknowledgement is above the chain nonce — ignored (yönetici kararı (a)). */
  acknowledgementsAheadOfChain(): AcknowledgementAhead[] {
    return [...this.ackAhead.values()];
  }

  private async keep(id: string): Promise<void> {
    const record = this.deps.journal.get(id);
    if (!record || record.conflict || record.mined?.final) return;

    const receipt = await this.findMined(record);
    if (receipt) {
      const final = await this.isFinal(receipt);
      const mined: MinedState = {
        hash: receipt.transactionHash.toLowerCase(),
        blockNumber: String(receipt.blockNumber),
        blockHash: receipt.blockHash.toLowerCase(),
        status: receipt.status,
        final,
      };
      if (JSON.stringify(mined) !== JSON.stringify(record.mined)) {
        this.deps.journal.put({ ...record, mined, conflictSuspectedAt: null });
        this.noteFuelMined(record, receipt);
        if (final) {
          logger.info(
            { id, hash: mined.hash, block: mined.blockNumber, status: mined.status },
            'Outgoing transfer final',
          );
        }
      }
      return;
    }

    let current: JournalRecord = record;
    if (current.mined) {
      // Was in a block, no longer is (re-org above finalized) — deliver again.
      current = { ...current, mined: null };
      this.deps.journal.put(current);
    }

    const head = await this.deps.rpc.getBlockHeader('latest');
    const chainNonce = await this.deps.rpc.getTransactionCount(current.from, head.number);
    if (chainNonce > current.nonce) {
      const now = this.now();
      if (
        current.conflictSuspectedAt !== null &&
        now - current.conflictSuspectedAt >= this.deps.conflictConfirmMs
      ) {
        this.deps.journal.put({ ...current, conflict: { detectedAt: now, chainNonce } });
        logger.error(
          { id, from: current.from, nonce: current.nonce, chainNonce },
          'NONCE CONFLICT — the account nonce moved past a journalled transfer none of whose attempts is on chain. Nothing is re-signed; investigate the key (08 §3.7.6).',
        );
      } else if (current.conflictSuspectedAt === null) {
        this.deps.journal.put({ ...current, conflictSuspectedAt: now });
      }
      return;
    }
    if (current.conflictSuspectedAt !== null) {
      current = { ...current, conflictSuspectedAt: null };
      this.deps.journal.put(current);
    }

    const latest = current.attempts[current.attempts.length - 1];
    if (chainNonce === current.nonce && this.now() - latest.signedAt >= this.deps.bumpAfterMs) {
      if (await this.tryBump(current, head)) return;
    }
    await this.broadcast(current, latest);
  }

  // ---- Internals --------------------------------------------------------------

  /**
   * One request: under the id's lock, the journal answers first (replay, no
   * configuration read); only an id the journal does not hold is planned —
   * every configuration check runs there — and signed under the sender's lock.
   */
  private submit(
    rawId: string,
    identity: RequestIdentity,
    plan: (id: string) => SendPlan,
  ): Promise<SendResult> {
    const id = rawId.toLowerCase();
    return this.idLocks.run(id, async () => {
      const existing = this.deps.journal.get(id);
      if (existing) return this.replay(existing, id, identity);
      // Only a fuel so far (bulgu e): the id is taken by the request it fuels.
      const fuel = id.startsWith('transfer:')
        ? this.deps.journal.get(TransferService.fuelIdOf(id))
        : undefined;
      if (fuel && !TransferService.fuelMatches(fuel, identity)) {
        throw this.idempotencyConflict(
          id,
          { purpose: 'FUEL', to: fuel.recipient, amount: fuel.fuelFor?.amountUnits ?? null },
          identity,
        );
      }
      const planned = plan(id);
      return this.locks.run(planned.from, async () => {
        try {
          return await this.signNew(planned);
        } catch (err) {
          throw transientUnlessRevert(err);
        }
      });
    });
  }

  private async signNew(plan: SendPlan): Promise<SendResult> {
    const rpc = this.deps.rpc;
    const head = await rpc.getBlockHeader('latest');

    if (sameAddress(plan.contract, this.deps.tokenContracts.USDT)) {
      await assertUsdtSendable(rpc, plan.contract, plan.recipient, head.number);
    }
    // Hot-wallet outflow: payouts and (T158 (f)) refunds after the sweep.
    if (plan.purpose === 'PAYOUT' || (plan.purpose === 'REFUND' && plan.signer === 'HOT')) {
      await this.deps.guard.assertHotWalletDailyLimit(
        plan.amountUnits,
        head.number,
        this.deps.journal.all(),
      );
    }

    const quote = await quoteFees(rpc, head);
    const data = encodeTransfer(plan.recipient, plan.amountUnits);
    const gasLimit = await this.simulate(plan, data, head.number);
    // T159a: a deposit flow shares the cap with its fuel, by the fuel's own
    // estimate (the deposit's account state decides it) — never 21,000.
    const fuelGasLimit =
      plan.signer === 'DEPOSIT' ? await this.fuelGasLimitOf(plan, head.number) : 0n;

    let maxFeePerGas = quote.maxFeePerGas;
    let capWei: bigint | null = null;
    let ethUsdMicros: bigint | null = null;
    if (plan.capFlow !== null && plan.capUsdMicros !== null) {
      try {
        const capped = await cappedMaxFee(
          quote,
          plan.capUsdMicros,
          this.deps.prices,
          gasLimit,
          gasLimit + fuelGasLimit,
        );
        maxFeePerGas = capped.maxFeePerGas;
        capWei = capped.capWei;
        ethUsdMicros = capped.ethUsdMicros;
      } catch (err) {
        throw this.deferral(plan, err);
      }
    }
    // The sender's nonce first: a deposit whose journal is behind the chain
    // is never fuelled (the fuel would only wait there).
    const nonce = await this.nextNonce(plan.from, head.number);

    const balance = await rpc.getBalance(plan.from, head.number);
    if (plan.signer === 'DEPOSIT') {
      // T153: what the deposit's other unmined sends (and other requests'
      // fuel not yet spent) reserve is not this send's gas.
      const reserved = await this.reservedOn(plan.from, plan.id, head.number);
      const available = balance > reserved ? balance - reserved : 0n;
      if (available < gasLimit * maxFeePerGas) {
        maxFeePerGas = await this.fuelFor(plan, {
          head,
          quote,
          gasLimit,
          fuelGasLimit,
          maxFeePerGas,
          available,
          ethUsdMicros,
        });
      }
    } else if (balance < gasLimit * maxFeePerGas) {
      throw this.deferral(
        plan,
        new TransferDeferredError(
          `Hot wallet ${plan.from} holds ${balance} wei, the send needs up to ${gasLimit * maxFeePerGas} wei of gas — it waits (hot wallet ETH top-up, 08 §3.7.5).`,
          'HOT_WALLET_GAS_LOW',
        ),
      );
    }
    const priorityFee =
      quote.priorityFeePerGas < maxFeePerGas ? quote.priorityFeePerGas : maxFeePerGas;

    const tx = Transaction.from({
      type: 2,
      chainId: this.deps.policy.chainId,
      nonce,
      gasLimit,
      maxFeePerGas,
      maxPriorityFeePerGas: priorityFee,
      to: plan.contract,
      value: 0n,
      data,
      accessList: [],
    });
    const attempt = this.sign(tx, plan, capWei);

    const record: JournalRecord = {
      id: plan.id,
      purpose: plan.purpose,
      refundType: plan.refundType,
      signer: plan.signer,
      from: plan.from,
      depositIndex: plan.depositIndex,
      contract: canonicalAddress(plan.contract),
      token: plan.token,
      recipient: plan.recipient,
      amountUnits: plan.amountUnits.toString(),
      nonce,
      gasLimit: gasLimit.toString(),
      feeCapWei: capWei === null ? null : capWei.toString(),
      attempts: [attempt],
      createdAt: this.now(),
      lastBroadcastAt: null,
      lastBroadcastError: null,
      mined: null,
      conflictSuspectedAt: null,
      conflict: null,
    };
    // Durable BEFORE broadcast (08 §3.7.6): from here on the id owns this nonce.
    this.deps.journal.put(record);
    this.deps.feeWaits.cleared(plan.id);
    logger.info(
      {
        id: plan.id,
        purpose: plan.purpose,
        from: plan.from,
        to: plan.recipient,
        token: plan.token,
        amount: formatTokenUnits(plan.amountUnits),
        nonce,
        hash: attempt.hash,
        gasLimit: gasLimit.toString(),
        maxFeePerGas: attempt.maxFeePerGas,
        capWei: capWei?.toString() ?? null,
      },
      'Outgoing transfer signed and journalled',
    );
    await this.broadcast(record, attempt);
    return { txHash: attempt.hash, nonce, replayed: false };
  }

  /**
   * The nonce of a new signature — or a stop. The account's next nonce on
   * chain (`latest` count and the node's `pending` count) may not be ahead
   * of what the journal knows (its highest nonce + 1, or an operator
   * acknowledgement): if it is, transactions the journal does not hold used
   * those nonces — the journal was lost or restored from an old backup, or
   * someone sent with the key. A request id whose record is no longer in the
   * journal would be signed AGAIN with a new nonce (a second payment), so
   * nothing is signed until an
   * operator has looked (yönetici kararı, T152 doğrulaması; 08 §3.7.6,
   * DEPLOY_RUNBOOK §D). Retryable 503: the backend keeps the row PENDING.
   *
   * An acknowledgement ABOVE the chain nonce accounts for nothing that
   * happened — a typo (1000 for 10) would otherwise open the check for good,
   * and after a lost journal the same id would be signed again. It is
   * ignored (the account stays JOURNAL_BEHIND_CHAIN if the journal is
   * behind), logged and published on its own gauge (T152 doğrulaması,
   * yönetici kararı (a)).
   */
  private async nextNonce(from: string, block: bigint): Promise<number> {
    const latestCount = await this.deps.rpc.getTransactionCount(from, block);
    const pendingCount = await this.deps.rpc.getPendingTransactionCount(from);
    const journalNext = this.deps.journal
      .bySender(from)
      .reduce((next, r) => (r.nonce + 1 > next ? r.nonce + 1 : next), 0);
    const configured = this.deps.acknowledgedNonces?.get(from) ?? 0;
    const chainNext = Math.max(latestCount, pendingCount);
    let acknowledged = configured;
    if (configured > chainNext) {
      acknowledged = 0;
      const detectedAt = this.ackAhead.get(from)?.detectedAt ?? this.now();
      this.ackAhead.set(from, {
        from,
        acknowledged: configured,
        chainNonce: chainNext,
        detectedAt,
      });
      logger.error(
        { from, acknowledged: configured, chainNonce: chainNext, latestCount, pendingCount },
        'JOURNAL ACKNOWLEDGEMENT AHEAD OF CHAIN — ETHEREUM_JOURNAL_ACKNOWLEDGED_NONCES is above the account nonce on chain (typo or stale value); it is IGNORED. Correct it to the chain nonce (DEPLOY_RUNBOOK §D).',
      );
    } else {
      this.ackAhead.delete(from);
    }
    const known = Math.max(journalNext, acknowledged);
    if (chainNext > known) {
      const detectedAt = this.behind.get(from)?.detectedAt ?? this.now();
      this.behind.set(from, { from, chainNonce: chainNext, journalNext: known, detectedAt });
      logger.error(
        { from, chainNonce: chainNext, latestCount, pendingCount, journalNext, acknowledged },
        'JOURNAL BEHIND CHAIN — the account has used nonces the journal does not know (lost journal, old backup or a send outside the service). Nothing is signed until an operator acknowledges (DEPLOY_RUNBOOK §D).',
      );
      throw new SidecarError(
        `Account ${from} is at nonce ${chainNext} on chain but the journal knows only up to ${known} — nonces ${known}…${chainNext - 1} were used by transactions this journal does not hold; nothing signed until an operator acknowledges (08 §3.7.6).`,
        'JOURNAL_BEHIND_CHAIN',
        true,
      );
    }
    this.behind.delete(from);
    return Math.max(chainNext, journalNext);
  }

  private idempotencyConflict(
    id: string,
    recorded: { purpose: string; to: string; amount: string | null },
    identity: RequestIdentity,
  ): SidecarError {
    logger.error(
      {
        id,
        recorded,
        asked: {
          purpose: identity.purpose,
          to: identity.recipient,
          amount: identity.amountUnits.toString(),
        },
      },
      'IDEMPOTENCY CONFLICT — an id already signed is asked again with different parameters; refused',
    );
    return new SidecarError(
      `Request ${id} was already signed with different parameters — refused (08 §3.7.6).`,
      'IDEMPOTENCY_CONFLICT',
      false,
    );
  }

  /**
   * A request id that so far holds only its fuel (bulgu e): the same deposit
   * (index and address) and — when the record keeps it — the same request
   * identity. Anything else is another request under a used id.
   */
  private static fuelMatches(fuel: JournalRecord, identity: RequestIdentity): boolean {
    if (fuel.depositIndex !== identity.depositIndex) return false;
    if (identity.depositAddress === null || fuel.recipient !== identity.depositAddress)
      return false;
    const asked = fuel.fuelFor;
    if (!asked) return true;
    return (
      asked.purpose === identity.purpose &&
      asked.token === identity.token &&
      asked.recipient === identity.recipient &&
      asked.amountUnits === identity.amountUnits.toString() &&
      asked.refundType === identity.refundType
    );
  }

  private async replay(
    existing: JournalRecord,
    id: string,
    identity: RequestIdentity,
  ): Promise<SendResult> {
    const same =
      existing.purpose === identity.purpose &&
      existing.token === identity.token &&
      existing.recipient === identity.recipient &&
      existing.amountUnits === identity.amountUnits.toString() &&
      existing.depositIndex === identity.depositIndex &&
      (identity.depositAddress === null || existing.from === identity.depositAddress) &&
      existing.refundType === identity.refundType &&
      existing.signer === identity.signer;
    if (!same) {
      throw this.idempotencyConflict(
        id,
        { purpose: existing.purpose, to: existing.recipient, amount: existing.amountUnits },
        identity,
      );
    }
    if (existing.conflict) {
      throw new SidecarError(
        `Request ${id}: its nonce was used by a transaction this service did not sign — not delivered; investigate.`,
        'TRANSFER_NONCE_CONFLICT',
        false,
      );
    }
    const latest = existing.attempts[existing.attempts.length - 1];
    if (!existing.mined) {
      // Same lock as the keeper: a replay's broadcast never races a fee bump.
      await this.locks.run(existing.from, () => this.broadcast(existing, latest));
    }
    logger.info(
      { id, hash: existing.mined?.hash ?? latest.hash },
      'Outgoing transfer replayed from the journal — nothing new signed',
    );
    return { txHash: existing.mined?.hash ?? latest.hash, nonce: existing.nonce, replayed: true };
  }

  /**
   * Gas estimate + `eth_call` from the sender at the priced block; returns
   * the gas limit to sign with (estimate + 10 %). Only the EVM's revert — or
   * a token answering `false` — refuses for good. T159a: any other failure of
   * the estimate makes the send wait (`GAS_ESTIMATE_UNAVAILABLE`), an
   * implausible estimate too (`GAS_ESTIMATE_ABNORMAL`, alarm); any other node
   * error of the `eth_call` is left to the caller, which makes it transient.
   */
  private async simulate(plan: SendPlan, data: string, block: bigint): Promise<bigint> {
    const call = { from: plan.from, to: plan.contract, data };
    const revertRefusal = (err: unknown) =>
      new SidecarError(
        `Simulation of the transfer reverted (${(err as Error).message}) — a blacklisted or empty sender, or a paused token; nothing signed (08 §3.7.6, §3.7.8).`,
        'TRANSFER_SIMULATION_REVERTED',
        false,
      );
    let estimate: bigint;
    try {
      estimate = await this.deps.rpc.estimateGas(call, block);
    } catch (err) {
      if (isExecutionRevert(err)) throw revertRefusal(err);
      throw this.deferral(
        plan,
        new TransferDeferredError(
          `The gas of the ${plan.purpose.toLowerCase()} could not be estimated (${(err as Error).message}) — nothing signed, the send waits (T159a).`,
          'GAS_ESTIMATE_UNAVAILABLE',
        ),
      );
    }
    this.assertPlausibleEstimate(plan, 'token', estimate);
    let result: string;
    try {
      result = await this.deps.rpc.callFrom(call, block);
    } catch (err) {
      if (isExecutionRevert(err)) throw revertRefusal(err);
      throw err;
    }
    // USDT returns nothing; USDC returns a 32-byte bool. False is a refusal.
    if (result !== '0x') {
      let ok = false;
      try {
        ok = TOKEN_ABI.getAbiCoder().decode(['bool'], result)[0] === true;
      } catch {
        ok = false;
      }
      if (!ok) {
        throw new SidecarError(
          'Simulation of the transfer returned false — nothing signed.',
          'TRANSFER_SIMULATION_REVERTED',
          false,
        );
      }
    }
    return tokenGasLimit(estimate);
  }

  /**
   * T159a — the gas limit of this request's fuel, for the cap split and the
   * fuel itself: the fuel already journalled for it (its fee is spent or on
   * its way — the split it was signed under stands), else `eth_estimateGas`
   * of a plain ETH send from the fuel wallet to the deposit at the priced
   * block — the deposit's account state decides it (a new account costs more
   * on a chain that prices state creation). Exactly the estimate, no
   * headroom: that state only moves one way (an account, once there, stays),
   * so the send can only get cheaper. Without a fuel wallet no fuel can go
   * out; the split then counts the intrinsic 21,000.
   */
  private async fuelGasLimitOf(plan: SendPlan, block: bigint): Promise<bigint> {
    const recorded = this.deps.journal.get(TransferService.fuelIdOf(plan.id));
    if (recorded) return BigInt(recorded.gasLimit);
    if (!this.deps.fuel?.key || this.fuelAddress === null) return NATIVE_TRANSFER_GAS;
    let estimate: bigint;
    try {
      estimate = await this.deps.rpc.estimateGas(
        { from: this.fuelAddress, to: plan.from, data: '0x', value: FUEL_ESTIMATE_PROBE_WEI },
        block,
      );
    } catch (err) {
      throw this.deferral(
        plan,
        new TransferDeferredError(
          `The gas of the fuel to ${plan.from} could not be estimated (${(err as Error).message}) — nothing signed, the send waits (T159a).`,
          'GAS_ESTIMATE_UNAVAILABLE',
        ),
      );
    }
    this.assertPlausibleEstimate(plan, 'fuel', estimate);
    return estimate;
  }

  /**
   * T159a (yönetici kararı, ⚠ PARA) — an estimate below the intrinsic 21,000
   * or above `MAX_GAS_ESTIMATE` (1,000,000) is not believed: nothing is
   * signed, the send waits and `ethereum-gas-estimate-abnormal` fires.
   */
  private assertPlausibleEstimate(plan: SendPlan, leg: 'token' | 'fuel', estimate: bigint): void {
    if (isPlausibleGasEstimate(estimate)) return;
    gasEstimateAbnormalTotal.inc({ flow: plan.purpose.toLowerCase(), leg });
    logger.error(
      { id: plan.id, purpose: plan.purpose, leg, from: plan.from, estimate: estimate.toString() },
      'ABNORMAL GAS ESTIMATE — outside 21,000…1,000,000; nothing signed, the send waits (investigate the token contract and the RPC provider, DEPLOY_RUNBOOK §J)',
    );
    throw this.deferral(
      plan,
      new TransferDeferredError(
        `The ${leg === 'fuel' ? 'fuel' : plan.purpose.toLowerCase()} gas estimate ${estimate} is outside 21,000…1,000,000 — nothing signed, the send waits (T159a).`,
        'GAS_ESTIMATE_ABNORMAL',
      ),
    );
  }

  /** Policy check on the exact object, then signature, then sender re-check. */
  private sign(
    tx: Transaction,
    plan: Pick<
      SendPlan,
      | 'purpose'
      | 'signer'
      | 'depositIndex'
      | 'from'
      | 'contract'
      | 'recipient'
      | 'amountUnits'
      | 'refundType'
    >,
    capWei: bigint | null,
  ): JournalAttempt {
    assertSignableTokenTransfer(
      tx,
      {
        contract: plan.contract,
        recipient: plan.recipient,
        amountUnits: plan.amountUnits,
        // T160c — a sweep may move USDC too (the fee a wrong-token refund withheld).
        wrongTokenRefund: plan.refundType === 'WRONG_TOKEN_REFUND' || plan.purpose === 'SWEEP',
        feeCapWei: capWei ?? undefined,
      },
      this.deps.policy,
    );
    const key =
      plan.signer === 'HOT'
        ? this.deps.hotWalletKey
        : this.deps.wallet.deriveSigner(plan.depositIndex ?? -1).privateKey;
    if (!key) {
      throw new SidecarError(
        'Hot wallet key is not provisioned.',
        'HOT_WALLET_NOT_CONFIGURED',
        false,
      );
    }
    tx.signature = new SigningKey(key).sign(tx.unsignedHash);
    const signed = Transaction.from(tx.serialized);
    if (!sameAddress(signed.from, plan.from)) {
      throw new SidecarError(
        `Signed transaction recovers to ${signed.from}, expected ${plan.from} — refusing to broadcast.`,
        'SIGNER_KEY_MISMATCH',
        false,
      );
    }
    return {
      hash: (signed.hash ?? '').toLowerCase(),
      raw: signed.serialized,
      maxFeePerGas: (signed.maxFeePerGas ?? 0n).toString(),
      maxPriorityFeePerGas: (signed.maxPriorityFeePerGas ?? 0n).toString(),
      signedAt: this.now(),
    };
  }

  /** Never throws: the record is journalled; the keeper retries. */
  private async broadcast(record: JournalRecord, attempt: JournalAttempt): Promise<void> {
    let error: string | null = null;
    try {
      await this.deps.rpc.sendRawTransaction(attempt.raw);
    } catch (err) {
      const message = (err as Error).message;
      if (!ALREADY_KNOWN.test(message)) error = message.slice(0, 300);
    }
    const latest = this.deps.journal.get(record.id) ?? record;
    this.deps.journal.put({ ...latest, lastBroadcastAt: this.now(), lastBroadcastError: error });
    if (error) {
      logger.warn(
        { id: record.id, hash: attempt.hash, nonce: record.nonce, reason: error },
        'Broadcast not accepted — the journalled bytes are re-sent by the keeper',
      );
    }
  }

  /**
   * Same nonce, fees +12.5 %, never above the record's budget per gas
   * (⌊cap ÷ gasLimit⌋ — the same per-transaction cap as the first signature);
   * false when no valid bump exists.
   */
  private async tryBump(record: JournalRecord, head: BlockHeader): Promise<boolean> {
    const previous = record.attempts[record.attempts.length - 1];
    const oldMax = BigInt(previous.maxFeePerGas);
    const oldTip = BigInt(previous.maxPriorityFeePerGas);
    const gasLimit = BigInt(record.gasLimit);
    const quote = await quoteFees(this.deps.rpc, head);
    let tip = maxBig(quote.priorityFeePerGas, ceilDiv(oldTip * BUMP_NUMERATOR, BUMP_DENOMINATOR));
    let maxFee = maxBig(
      2n * quote.baseFeePerGas + tip,
      ceilDiv(oldMax * BUMP_NUMERATOR, BUMP_DENOMINATOR),
    );
    const capWei = recordFeeCapWei(record);
    const ceiling = capWei === null ? null : feeCeilingPerGas(capWei, gasLimit);
    if (ceiling !== null && maxFee > ceiling) maxFee = ceiling;
    if (tip > maxFee) tip = maxFee;
    if (maxFee * 10n < oldMax * 11n || tip * 10n < oldTip * 11n) {
      return false; // At the cap: it waits for the base fee to fall (08 §3.7.7).
    }
    const isFuel = record.purpose === 'FUEL';
    const value = isFuel ? BigInt(record.amountUnits) : 0n;
    const balance = await this.deps.rpc.getBalance(record.from, head.number);
    if (balance < value + gasLimit * maxFee) {
      logger.warn(
        { id: record.id, from: record.from, balance: String(balance) },
        'Fee bump skipped — sender has too little ETH for the higher fee',
      );
      return false;
    }
    const tx = Transaction.from({
      type: 2,
      chainId: this.deps.policy.chainId,
      nonce: record.nonce,
      gasLimit,
      maxFeePerGas: maxFee,
      maxPriorityFeePerGas: tip,
      to: isFuel ? record.recipient : (record.contract ?? ''),
      value,
      data: isFuel ? '0x' : encodeTransfer(record.recipient, BigInt(record.amountUnits)),
      accessList: [],
    });
    const attempt = isFuel
      ? this.bumpFuel(tx, record, capWei)
      : this.sign(
          tx,
          {
            purpose: record.purpose,
            signer: record.signer,
            depositIndex: record.depositIndex,
            from: record.from,
            contract: record.contract ?? '',
            recipient: record.recipient,
            amountUnits: BigInt(record.amountUnits),
            refundType: (record.refundType as RefundType | null) ?? null,
          },
          capWei,
        );
    const bumped: JournalRecord = { ...record, attempts: [...record.attempts, attempt] };
    this.deps.journal.put(bumped);
    logger.warn(
      {
        id: record.id,
        nonce: record.nonce,
        from: previous.hash,
        to: attempt.hash,
        maxFeePerGas: attempt.maxFeePerGas,
      },
      'Fee bump — same nonce, new signature (08 §3.7.6)',
    );
    await this.broadcast(bumped, attempt);
    return true;
  }

  /**
   * A fuel's fee bump: the same value to the same deposit at the same nonce,
   * re-checked by the fuel policy (its own amount is not counted twice
   * against the daily cap — the bump replaces it).
   */
  private bumpFuel(tx: Transaction, record: JournalRecord, capWei: bigint | null): JournalAttempt {
    const fuel = this.deps.fuel;
    if (!fuel?.key || this.fuelAddress === null || !sameAddress(record.from, this.fuelAddress)) {
      throw new SidecarError(
        'The fuel key that signed this record is not provisioned — no bump.',
        'FUEL_WALLET_NOT_CONFIGURED',
        false,
      );
    }
    const depositIndex = record.depositIndex ?? -1;
    return this.signFuel(tx, fuel.key, this.fuelAddress, {
      recipient: record.recipient,
      amountWei: BigInt(record.amountUnits),
      isOwnDeposit: (address) =>
        depositIndex >= 0 && sameAddress(this.deps.wallet.derive(depositIndex).address, address),
      maxSingleWei: fuel.maxSingleWei,
      maxDailyWei: fuel.maxDailyWei,
      spentLast24hWei: this.fuelSpentLast24hWei(record.id),
      maxForSendWei: BigInt(record.amountUnits),
      platformAddresses: [
        this.deps.hotWalletAddress ?? '',
        fuel.coldWalletAddress ?? '',
        this.fuelAddress,
      ],
      feeCapWei: capWei ?? 0n,
      // The bump keeps the gas limit the fuel was first signed with.
      gasLimit: BigInt(record.gasLimit),
    });
  }

  private async findMined(record: JournalRecord): Promise<TransactionReceipt | null> {
    for (let i = record.attempts.length - 1; i >= 0; i--) {
      const receipt = await this.deps.rpc.getTransactionReceipt(record.attempts[i].hash);
      if (receipt) return receipt;
    }
    return null;
  }

  /** At or below `finalized`, and the block at that height is still the one the receipt names. */
  private async isFinal(receipt: TransactionReceipt): Promise<boolean> {
    const finalized = this.deps.finalizedHead();
    if (finalized === null || receipt.blockNumber > finalized) return false;
    const header = await this.deps.rpc.getBlockHeader(receipt.blockNumber);
    return header.hash.toLowerCase() === receipt.blockHash.toLowerCase();
  }

  // ---- Fuel (T153 — 08 §3.7.5) -----------------------------------------------

  /** `transfer:<guid>` → `fuel:<guid>` — the one fuel record a request may ever have. */
  private static fuelIdOf(requestId: string): string {
    return `fuel:${requestId.slice(requestId.indexOf(':') + 1)}`;
  }

  /**
   * ETH on the deposit that is not this request's to spend: the worst-case
   * fee of the deposit's own sends not yet in a block (the node holds that
   * much back), and the fuel other requests sent here that is already in the
   * balance but whose own send is not signed yet (it is theirs — taking it
   * would leave their send short, with nothing to top it up).
   */
  private async reservedOn(deposit: string, requestId: string, head: bigint): Promise<bigint> {
    let reserved = 0n;
    for (const record of this.deps.journal.bySender(deposit)) {
      if (record.mined || record.conflict) continue;
      const latest = record.attempts[record.attempts.length - 1];
      reserved += BigInt(record.gasLimit) * BigInt(latest.maxFeePerGas);
    }
    const own = TransferService.fuelIdOf(requestId);
    for (const fuel of this.deps.journal.all()) {
      if (fuel.purpose !== 'FUEL' || fuel.id === own || fuel.recipient !== deposit) continue;
      if (this.deps.journal.get(`transfer:${fuel.id.slice('fuel:'.length)}`)) continue;
      if (fuel.conflict) continue;
      const receipt = fuel.mined ? null : await this.findMined(fuel);
      const inBlock = fuel.mined
        ? fuel.mined.status === 1 && BigInt(fuel.mined.blockNumber) <= head
        : receipt !== null && receipt.status === 1 && receipt.blockNumber <= head;
      if (inBlock) reserved += BigInt(fuel.amountUnits);
    }
    return reserved;
  }

  /**
   * The deposit cannot pay the send's fee. Either this request's fuel is
   * already in a block — then the send is signed at the fee it affords, if
   * that still clears the base fee and tip (returned) — or the request waits:
   * its fuel is signed now (`FUEL_PENDING`), is still waiting for a block, or
   * cannot be sent. Never returns without the fuel being in a block.
   */
  private async fuelFor(plan: SendPlan, ctx: FuelContext): Promise<bigint> {
    const fuelId = TransferService.fuelIdOf(plan.id);
    const existing = this.deps.journal.get(fuelId);
    if (!existing) {
      const withoutFuel = await this.sendFuel(plan, fuelId, ctx);
      if (withoutFuel !== null) return withoutFuel;
      throw this.deferral(
        plan,
        new TransferDeferredError(
          `Fuel for ${plan.from} is signed and broadcast — the ${plan.purpose.toLowerCase()} is signed once it is in a block (08 §3.7.5).`,
          'FUEL_PENDING',
        ),
      );
    }
    if (existing.conflict) {
      logger.error(
        { id: plan.id, fuelId, deposit: plan.from },
        'Fuel for this send is in NONCE CONFLICT — it will not arrive; the send waits (investigate the fuel key, 08 §3.7.6).',
      );
      throw this.deferral(
        plan,
        new TransferDeferredError(
          `The fuel for ${plan.from} is in nonce conflict — the send waits (08 §3.7.6).`,
          'FUEL_REQUIRED',
        ),
      );
    }
    const receipt = await this.findMined(existing);
    if (!receipt || receipt.blockNumber > ctx.head.number) {
      throw this.deferral(
        plan,
        new TransferDeferredError(
          `Fuel ${existing.attempts[existing.attempts.length - 1].hash} for ${plan.from} is not in a block yet — the send waits for it (08 §3.7.5).`,
          'FUEL_PENDING',
        ),
      );
    }
    this.noteFuelMined(existing, receipt);
    if (receipt.status !== 1) {
      logger.error(
        { id: plan.id, fuelId, hash: receipt.transactionHash, deposit: plan.from },
        'Fuel transaction reverted — the deposit has no gas; the send waits (investigate).',
      );
      throw this.deferral(
        plan,
        new TransferDeferredError(
          `The fuel for ${plan.from} reverted — the send waits.`,
          'FUEL_REQUIRED',
        ),
      );
    }
    // Fuel in a block, but today's fee asks more than it carries: spend it
    // as it is when that still clears base fee + tip; nothing tops it up.
    return this.feeTheDepositAffords(plan, ctx, 'its fuel is in a block');
  }

  /**
   * No (more) fuel for this send: sign it at the fee the deposit's own ETH
   * affords (`⌊available ÷ gasLimit⌋`) when that still clears today's base
   * fee + tip (returned), otherwise wait for the fee to fall (`FUEL_REQUIRED`).
   * Used when the request's one fuel is in a block but short of a later,
   * higher fee, and when the deposit lacks less than a fuel's own fee
   * (bulgu a: no fuel smaller than what it costs to send).
   */
  private feeTheDepositAffords(plan: SendPlan, ctx: FuelContext, why: string): bigint {
    const affordable = ctx.available / ctx.gasLimit;
    if (affordable >= ctx.quote.baseFeePerGas + ctx.quote.priorityFeePerGas) {
      logger.warn(
        {
          id: plan.id,
          deposit: plan.from,
          why,
          available: ctx.available.toString(),
          asked: (ctx.gasLimit * ctx.maxFeePerGas).toString(),
          maxFeePerGas: affordable.toString(),
        },
        'Deposit ETH is short of the quoted fee ceiling — signing at the fee it affords, no further fuel',
      );
      return affordable;
    }
    logger.warn(
      {
        id: plan.id,
        deposit: plan.from,
        why,
        available: ctx.available.toString(),
        needsPerGas: (ctx.quote.baseFeePerGas + ctx.quote.priorityFeePerGas).toString(),
      },
      "Deposit ETH is below today's base fee + tip and no further fuel goes out — the send waits for the fee to fall",
    );
    throw this.deferral(
      plan,
      new TransferDeferredError(
        `The ETH on ${plan.from} (${ctx.available} wei; ${why}) no longer covers today's fee — the send waits for the base fee to fall (08 §3.7.5).`,
        'FUEL_REQUIRED',
      ),
    );
  }

  /**
   * Signs, journals and broadcasts the one fuel of a request: the missing
   * amount, from the fuel wallet, to the re-derived deposit. Every reason it
   * cannot is a wait ({@link TransferDeferredError}), so a sweep or refund is
   * never refused (→ backend FAILED → re-queued with a new id → fuelled twice)
   * for want of fuel. Returns null once the fuel is broadcast — or, when the
   * deposit lacks less than the fuel send's own worst-case fee, signs no fuel
   * and returns the fee the deposit affords on its own (bulgu a).
   */
  private async sendFuel(plan: SendPlan, fuelId: string, ctx: FuelContext): Promise<bigint | null> {
    const fuel = this.deps.fuel;
    const wait = (
      message: string,
      reason: 'FUEL_REQUIRED' | 'FUEL_LIMIT_REACHED' | 'FUEL_WALLET_LOW' | 'FEE_ABOVE_CAP',
    ) => this.deferral(plan, new TransferDeferredError(message, reason));
    if (!fuel || !fuel.key || this.fuelAddress === null) {
      throw wait(
        `Deposit ${plan.from} has no gas and the fuel wallet is not provisioned (ethereum_fuel_wallet_private_key) — the send waits.`,
        'FUEL_REQUIRED',
      );
    }
    if (fuel.maxSingleWei === null || fuel.maxDailyWei === null) {
      throw wait(
        'Fuel caps are not configured (ETHEREUM_FUEL_MAX_SINGLE_ETH / _DAILY_ETH) — no fuel, the send waits (fail closed).',
        'FUEL_REQUIRED',
      );
    }
    if (
      plan.depositIndex === null ||
      plan.capUsdMicros === null ||
      (plan.capFlow !== 'SWEEP' && plan.capFlow !== 'REFUND') ||
      ctx.ethUsdMicros === null
    ) {
      throw wait(`No fee cap for the fuel of ${plan.id} — the send waits.`, 'FUEL_REQUIRED');
    }
    const fuelKey = fuel.key;
    const fuelAddress = this.fuelAddress;
    const maxSingleWei = fuel.maxSingleWei;
    const maxDailyWei = fuel.maxDailyWei;
    const depositIndex = plan.depositIndex;
    // The token send's whole fee budget; fuel is what the deposit lacks of it.
    const sendNeeds = ctx.gasLimit * ctx.maxFeePerGas;
    const amount = sendNeeds - ctx.available;
    // The fuel send's own share of the one cap — its estimated gas of the
    // flow's estimated gas (T159a) — priced with the same ETH reading as the
    // token send's share.
    const fuelGas = ctx.fuelGasLimit;
    const capWei = capShareWei(
      plan.capUsdMicros,
      ctx.ethUsdMicros,
      fuelGas,
      ctx.gasLimit + fuelGas,
    );
    const ceiling = feeCeilingPerGas(capWei, fuelGas);
    if (ctx.quote.baseFeePerGas + ctx.quote.priorityFeePerGas > ceiling) {
      throw wait(
        `Fuel send ${fuelGas} gas × ${ctx.quote.baseFeePerGas + ctx.quote.priorityFeePerGas} wei/gas is above its share of the cap ${capWei} wei — no fuel, the send waits (08 §3.7.7).`,
        'FEE_ABOVE_CAP',
      );
    }
    const maxFee = ctx.quote.maxFeePerGas < ceiling ? ctx.quote.maxFeePerGas : ceiling;
    const tip = ctx.quote.priorityFeePerGas < maxFee ? ctx.quote.priorityFeePerGas : maxFee;
    // Yönetici kararı (T153 doğrulaması, bulgu a): a fuel smaller than its
    // own worst-case fee is never sent — it would cost more to send than it
    // delivers, and many of them would spend the fuel wallet on fees. The
    // deposit then signs at the fee it affords, or waits.
    //
    // T159a (öneriyle kaydedildi — ⚠ PARA): only when the deposit holds ETH
    // of its own for this send. With none (`available` 0 — every fresh
    // deposit) it can sign nothing at any fee, so "the fee it affords" is a
    // wait that never ends; on a tariff that prices account creation (Sepolia
    // 2026-10-19: 207,391 gas to a new account, more than the ~53,000-gas
    // sweep it pays for) the fuel is then the only way out, and the cap split
    // above already bounds fuel + send. On the mainnet tariff this case cannot
    // arise (an empty deposit lacks the whole token budget, ≥ 23,100 gas ×
    // the fee, more than a 21,000-gas fuel's), so the rule's decided
    // behaviour is unchanged wherever it applied.
    const fuelFee = fuelGas * maxFee;
    if (amount < fuelFee && ctx.available > 0n) {
      return this.feeTheDepositAffords(
        plan,
        ctx,
        `it lacks ${amount} wei, less than a fuel's own fee ${fuelFee} wei`,
      );
    }
    if (amount < fuelFee) {
      logger.warn(
        {
          id: plan.id,
          deposit: plan.from,
          amount: amount.toString(),
          fuelFee: fuelFee.toString(),
          fuelGas: fuelGas.toString(),
        },
        'Fuel costs more than it delivers (the deposit holds no ETH; its account is created by the fuel) — sent, within the cap',
      );
    }
    if (amount > maxSingleWei) {
      logger.error(
        {
          id: plan.id,
          deposit: plan.from,
          amount: amount.toString(),
          cap: maxSingleWei.toString(),
        },
        'Fuel above ETHEREUM_FUEL_MAX_SINGLE_ETH — not sent; the send waits',
      );
      throw wait(
        `Fuel ${amount} wei for ${plan.from} is above the single-send cap ${maxSingleWei} wei — the send waits.`,
        'FUEL_LIMIT_REACHED',
      );
    }

    await this.locks.run(fuelAddress, async () => {
      // Value + the fuel's own worst-case fee, past fuels counted the same way (bulgu a).
      const spent = this.fuelSpentLast24hWei();
      if (spent + amount + fuelFee > maxDailyWei) {
        logger.error(
          {
            id: plan.id,
            spent: spent.toString(),
            amount: amount.toString(),
            fee: fuelFee.toString(),
            cap: maxDailyWei.toString(),
          },
          'Fuel would exceed ETHEREUM_FUEL_MAX_DAILY_ETH — not sent; the send waits',
        );
        throw wait(
          `Fuel value + fee in 24 h would reach ${spent + amount + fuelFee} wei, above the daily cap ${maxDailyWei} wei — the send waits.`,
          'FUEL_LIMIT_REACHED',
        );
      }
      // The fuel wallet's own unmined fuel is still in its balance on chain.
      let reserved = 0n;
      for (const record of this.deps.journal.bySender(fuelAddress)) {
        if (record.mined || record.conflict) continue;
        const latest = record.attempts[record.attempts.length - 1];
        reserved +=
          BigInt(record.amountUnits) + BigInt(record.gasLimit) * BigInt(latest.maxFeePerGas);
      }
      const balance = await this.deps.rpc.getBalance(fuelAddress, ctx.head.number);
      const needed = amount + fuelFee;
      if (balance < reserved + needed) {
        logger.error(
          {
            fuelWallet: fuelAddress,
            balance: balance.toString(),
            reserved: reserved.toString(),
            needed: needed.toString(),
          },
          'FUEL WALLET LOW — cannot fuel a deposit; the send waits (top up the fuel wallet, DEPLOY_RUNBOOK §D)',
        );
        throw wait(
          `Fuel wallet holds ${balance} wei (${reserved} reserved), the fuel needs ${needed} wei — the send waits.`,
          'FUEL_WALLET_LOW',
        );
      }
      const nonce = await this.nextNonce(fuelAddress, ctx.head.number);
      const tx = Transaction.from({
        type: 2,
        chainId: this.deps.policy.chainId,
        nonce,
        gasLimit: fuelGas,
        maxFeePerGas: maxFee,
        maxPriorityFeePerGas: tip,
        to: plan.from,
        value: amount,
        data: '0x',
        accessList: [],
      });
      const attempt = this.signFuel(tx, fuelKey, fuelAddress, {
        recipient: plan.from,
        amountWei: amount,
        isOwnDeposit: (address) =>
          sameAddress(this.deps.wallet.derive(depositIndex).address, address),
        maxSingleWei: maxSingleWei,
        maxDailyWei: maxDailyWei,
        spentLast24hWei: spent,
        // Computed apart from `amount` (bulgu b): what the send lacks once the
        // deposit's ETH that is this request's is counted — a fuel above it
        // is refused by the signer.
        maxForSendWei: ctx.gasLimit * ctx.maxFeePerGas - ctx.available,
        platformAddresses: [
          this.deps.hotWalletAddress ?? '',
          fuel.coldWalletAddress ?? '',
          fuelAddress,
        ],
        feeCapWei: capWei,
        gasLimit: fuelGas,
      });
      const record: JournalRecord = {
        id: fuelId,
        purpose: 'FUEL',
        refundType: null,
        signer: 'FUEL',
        from: fuelAddress,
        depositIndex,
        contract: null,
        token: null,
        recipient: plan.from,
        amountUnits: amount.toString(),
        nonce,
        gasLimit: fuelGas.toString(),
        feeCapWei: capWei.toString(),
        attempts: [attempt],
        createdAt: this.now(),
        lastBroadcastAt: null,
        lastBroadcastError: null,
        mined: null,
        conflictSuspectedAt: null,
        conflict: null,
        fuelFor: {
          purpose: plan.purpose,
          token: plan.token,
          recipient: plan.recipient,
          amountUnits: plan.amountUnits.toString(),
          refundType: plan.refundType,
        },
      };
      // Durable BEFORE broadcast, like every send (08 §3.7.6).
      this.deps.journal.put(record);
      logger.info(
        {
          id: fuelId,
          forRequest: plan.id,
          purpose: plan.purpose,
          from: fuelAddress,
          to: plan.from,
          wei: amount.toString(),
          nonce,
          hash: attempt.hash,
          maxFeePerGas: attempt.maxFeePerGas,
          capWei: capWei.toString(),
        },
        'Fuel signed and journalled',
      );
      fuelSendsTotal.inc({ flow: plan.purpose.toLowerCase() });
      await this.broadcast(record, attempt);
    });
    return null;
  }

  /** The fuel key's one kind of transaction: policy on the exact object, signature, sender re-check. */
  private signFuel(
    tx: Transaction,
    key: string,
    fuelAddress: string,
    intent: FuelIntent,
  ): JournalAttempt {
    assertSignableFuel(tx, intent, this.deps.policy);
    tx.signature = new SigningKey(key).sign(tx.unsignedHash);
    const signed = Transaction.from(tx.serialized);
    if (!sameAddress(signed.from, fuelAddress)) {
      throw new SidecarError(
        `Signed fuel recovers to ${signed.from}, expected ${fuelAddress} — refusing to broadcast.`,
        'SIGNER_KEY_MISMATCH',
        false,
      );
    }
    return {
      hash: (signed.hash ?? '').toLowerCase(),
      raw: signed.serialized,
      maxFeePerGas: (signed.maxFeePerGas ?? 0n).toString(),
      maxPriorityFeePerGas: (signed.maxPriorityFeePerGas ?? 0n).toString(),
      signedAt: this.now(),
    };
  }

  /** A fuel transaction in a block feeds the reconciliation's FuelLedger (T151: not an ETH alarm). */
  private noteFuelMined(record: JournalRecord, receipt: TransactionReceipt): void {
    if (record.purpose !== 'FUEL' || receipt.status !== 1) return;
    this.deps.fuel?.onMined?.(
      record.recipient,
      BigInt(record.amountUnits),
      receipt.blockNumber,
      receipt.transactionHash,
    );
  }

  private deferral(plan: SendPlan, err: unknown): unknown {
    if (err instanceof TransferDeferredError) {
      err.waitingSince = this.deps.feeWaits.deferred(plan.id, err.reason);
      logger.warn(
        {
          id: plan.id,
          purpose: plan.purpose,
          reason: err.reason,
          waitingSince: new Date(err.waitingSince).toISOString(),
        },
        err.message,
      );
    }
    return err;
  }

  private requireHotWallet(): string {
    if (!this.deps.hotWalletKey || !this.deps.hotWalletAddress) {
      throw new SidecarError(
        'Hot wallet is not provisioned (ethereum_hot_wallet_private_key + ETHEREUM_HOT_WALLET_ADDRESS).',
        'HOT_WALLET_NOT_CONFIGURED',
        false,
      );
    }
    return this.deps.hotWalletAddress;
  }

  private requireToken(token: TokenSymbol, expected: TokenSymbol, flow: string): void {
    if (token !== expected) {
      throw new SidecarError(
        `A ${flow} moves ${expected} only (08 §3.7.6), not ${token}.`,
        'TOKEN_NOT_ALLOWED',
        false,
      );
    }
  }

  private contractOf(token: TokenSymbol): string {
    const contract = this.deps.tokenContracts[token];
    if (!contract) {
      throw new SidecarError(
        `Token contract for ${token} is not configured.`,
        'TOKEN_CONTRACT_NOT_CONFIGURED',
        false,
      );
    }
    return canonicalAddress(contract);
  }

  private capOf(feeCapUsd: string): bigint {
    const micros = usdToMicros(feeCapUsd);
    if (micros === null) {
      throw new SidecarError(
        `feeCapUsd "${feeCapUsd}" is not a positive USD amount.`,
        'INVALID_TRANSFER_REQUEST',
        false,
      );
    }
    return micros;
  }

  /** The deposit key is derived from this service's seed and must control the named address. */
  private depositOf(index: number, address: string): string {
    const derived = this.deps.wallet.deriveSigner(index).address;
    if (!sameAddress(derived, address)) {
      throw new SidecarError(
        `Index ${index} derives ${derived}, not the deposit ${address} — refusing to sign.`,
        'DEPOSIT_ADDRESS_MISMATCH',
        false,
      );
    }
    return canonicalAddress(derived);
  }

  private recipient(address: string, sender: string): string {
    const canonical = canonicalAddress(address);
    if (/^0x0{40}$/.test(canonical) || canonical === sender) {
      throw new SidecarError(
        `Recipient ${address} is not a valid destination for this send.`,
        'INVALID_TRANSFER_REQUEST',
        false,
      );
    }
    return canonical;
  }
}

/**
 * A mined receipt's outcome. Status 1 is a success only when the receipt
 * carries the Transfer event this send asked for — from the record's token
 * contract, from the sender, to the recipient, for the amount. Without it
 * (a deprecated USDT forwarding to another contract, a token that "succeeds"
 * without moving anything) the money did not arrive: `NO_TRANSFER_LOG`, which
 * the backend records as `FAILED` — never `CONFIRMED`.
 */
function contractRetOf(record: JournalRecord, receipt: TransactionReceipt): string {
  if (receipt.status !== 1) return 'REVERT';
  if (record.contract === null) return 'SUCCESS';
  return hasTransferLog(
    receipt,
    record.contract,
    record.from,
    record.recipient,
    BigInt(record.amountUnits),
  )
    ? 'SUCCESS'
    : 'NO_TRANSFER_LOG';
}

/** The receipt carries `Transfer(from, to, amount)` from `contract`, not removed. */
function hasTransferLog(
  receipt: TransactionReceipt,
  contract: string,
  from: string,
  to: string,
  amountUnits: bigint,
): boolean {
  return receipt.logs.some((log) => {
    if (log.removed || !sameAddress(log.address, contract)) return false;
    if (log.topics.length !== 3) return false;
    if (log.topics[0]?.toLowerCase() !== TRANSFER_TOPIC) return false;
    if (log.topics[1]?.toLowerCase() !== addressTopic(from)) return false;
    if (log.topics[2]?.toLowerCase() !== addressTopic(to)) return false;
    try {
      return BigInt(log.data === '0x' ? 0 : log.data) === amountUnits;
    } catch {
      return false;
    }
  });
}
