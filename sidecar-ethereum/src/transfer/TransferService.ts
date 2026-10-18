import { SigningKey, Transaction } from 'ethers';
import { canonicalAddress, sameAddress } from '../address/canonicalAddress.js';
import { SidecarError } from '../errors/SidecarError.js';
import { logger } from '../logger.js';
import {
  RpcError,
  type BlockHeader,
  type EthereumRpcClient,
  type TransactionReceipt,
} from '../rpc/RpcClient.js';
import type { EthereumHdWallet } from '../wallet/HdWalletService.js';
import { TOKEN_ABI, TRANSFER_TOPIC, addressTopic, encodeTransfer } from './erc20.js';
import {
  TransferDeferredError,
  cappedMaxFee,
  quoteFees,
  type CappedFlow,
  type EthUsdPriceSource,
} from './FeePolicy.js';
import type { FeeWaitTracker } from './FeeWaitTracker.js';
import { KeyedMutex } from './KeyedMutex.js';
import { assertSignableTokenTransfer, type SignerPolicyConfig } from './SignerPolicy.js';
import { assertUsdtSendable } from './TokenSafety.js';
import type { TransferGuard } from './TransferGuard.js';
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
 *     different parameters it is refused (`IDEMPOTENCY_CONFLICT`).</item>
 *   <item>Every send of one account runs under that account's lock; the nonce
 *     is max(chain count at the priced block, journal's highest + 1).</item>
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
 * and recipient blacklist (TokenSafety), daily limit (payouts), fee quote and
 * cap (sweep / payout / cold wait above it), gas estimate and an `eth_call`
 * simulation from the sender (a revert — e.g. a blacklisted SENDER — refuses),
 * and the sender's ETH for gas (a deposit without fuel waits for T153).
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

export interface RefundRequest {
  blockchainTransactionId: string;
  depositIndex: number;
  depositAddress: string;
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

export interface TransferStatus {
  readonly txHash: string;
  readonly blockNumber: number | null;
  /** `SUCCESS`, `REVERT`, `NO_TRANSFER_LOG`; null while not mined. */
  readonly contractRet: string | null;
  /** Blocks from the transaction's block to `finalized`; -1 while not mined. */
  readonly confirmations: number;
  readonly finalized: boolean;
}

export interface TransferServiceDeps {
  rpc: Pick<
    EthereumRpcClient,
    | 'getBlockHeader'
    | 'getBalance'
    | 'call'
    | 'callFrom'
    | 'estimateGas'
    | 'getTransactionCount'
    | 'medianPriorityFee'
    | 'sendRawTransaction'
    | 'getTransactionReceipt'
  >;
  journal: TxJournal;
  feeWaits: FeeWaitTracker;
  guard: TransferGuard;
  wallet: Pick<EthereumHdWallet, 'deriveSigner'>;
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
  now?: () => number;
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

/** Gas-limit headroom over `eth_estimateGas` (10 %). Unused gas is not charged. */
const GAS_LIMIT_HEADROOM_PERCENT = 110n;

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

export class TransferService {
  private readonly locks = new KeyedMutex();
  private readonly now: () => number;

  constructor(private readonly deps: TransferServiceDeps) {
    this.now = deps.now ?? (() => Date.now());
  }

  // ---- Requests ---------------------------------------------------------------

  async payout(request: PayoutRequest): Promise<SendResult> {
    const hot = this.requireHotWallet();
    const units = toTokenUnits(request.amount);
    this.requireToken(request.token, 'USDT', 'payout');
    this.deps.guard.assertSingleTransferLimit(units);
    return this.send({
      id: `transfer:${request.blockchainTransactionId}`,
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
    });
  }

  async refund(request: RefundRequest): Promise<SendResult> {
    const units = toTokenUnits(request.amount);
    const wrongToken = request.refundType === 'WRONG_TOKEN_REFUND';
    if (request.token === 'USDC' && !wrongToken) {
      throw new SidecarError(
        'USDC may only be moved by a WRONG_TOKEN_REFUND (08 §3.7.6).',
        'TOKEN_NOT_ALLOWED',
        false,
      );
    }
    this.deps.guard.assertSingleTransferLimit(units);
    const deposit = this.depositOf(request.depositIndex, request.depositAddress);
    return this.send({
      id: `transfer:${request.blockchainTransactionId}`,
      purpose: 'REFUND',
      refundType: request.refundType,
      signer: 'DEPOSIT',
      depositIndex: request.depositIndex,
      from: deposit,
      contract: this.contractOf(request.token),
      token: request.token,
      recipient: this.recipient(request.toBuyerAddress, deposit),
      amountUnits: units,
      capFlow: 'REFUND',
      capUsdMicros: this.capOf(request.feeCapUsd),
    });
  }

  async sweep(request: SweepRequest): Promise<SendResult> {
    const units = toTokenUnits(request.amount);
    this.requireToken(request.token, 'USDT', 'sweep');
    this.deps.guard.assertSweepDestination(request.toHotWalletAddress);
    const deposit = this.depositOf(request.depositIndex, request.depositAddress);
    return this.send({
      id: `transfer:${request.blockchainTransactionId}`,
      purpose: 'SWEEP',
      refundType: null,
      signer: 'DEPOSIT',
      depositIndex: request.depositIndex,
      from: deposit,
      contract: this.contractOf('USDT'),
      token: 'USDT',
      recipient: canonicalAddress(request.toHotWalletAddress),
      amountUnits: units,
      capFlow: 'SWEEP',
      capUsdMicros: this.capOf(request.feeCapUsd),
    });
  }

  async coldWalletTransfer(request: ColdTransferRequest): Promise<SendResult> {
    const hot = this.requireHotWallet();
    const units = toTokenUnits(request.amount);
    this.requireToken(request.token, 'USDT', 'cold transfer');
    this.deps.guard.assertColdDestination(request.toColdAddress);
    return this.send({
      id: `cold:${request.coldTransferId}`,
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
    });
  }

  // ---- Status (GET /api/transfer/status/:txHash) -----------------------------

  async status(txHash: string): Promise<TransferStatus> {
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
      };
    }
    const finalized = this.deps.finalizedHead();
    const final = await this.isFinal(receipt);
    return {
      txHash: receipt.transactionHash,
      blockNumber: Number(receipt.blockNumber),
      contractRet: contractRetOf(record ?? null, receipt),
      confirmations: final && finalized !== null ? Number(finalized - receipt.blockNumber + 1n) : 0,
      finalized: final,
    };
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

  nonceConflictCount(): number {
    return this.deps.journal.all().filter((r) => r.conflict !== null).length;
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

  private send(plan: SendPlan): Promise<SendResult> {
    return this.locks.run(plan.from, async () => {
      const existing = this.deps.journal.get(plan.id);
      if (existing) return this.replay(existing, plan);

      const rpc = this.deps.rpc;
      const head = await rpc.getBlockHeader('latest');

      if (sameAddress(plan.contract, this.deps.tokenContracts.USDT)) {
        await assertUsdtSendable(rpc, plan.contract, plan.recipient, head.number);
      }
      if (plan.purpose === 'PAYOUT') {
        await this.deps.guard.assertHotWalletDailyLimit(
          plan.amountUnits,
          head.number,
          this.deps.journal.all(),
        );
      }

      const quote = await quoteFees(rpc, head);
      let maxFeePerGas = quote.maxFeePerGas;
      let capWei: bigint | null = null;
      if (plan.capFlow !== null && plan.capUsdMicros !== null) {
        try {
          const capped = await cappedMaxFee(
            quote,
            plan.capFlow,
            plan.capUsdMicros,
            this.deps.prices,
          );
          maxFeePerGas = capped.maxFeePerGas;
          capWei = capped.capWeiPerGas;
        } catch (err) {
          throw this.deferral(plan, err);
        }
      }
      const priorityFee =
        quote.priorityFeePerGas < maxFeePerGas ? quote.priorityFeePerGas : maxFeePerGas;

      const data = encodeTransfer(plan.recipient, plan.amountUnits);
      const gasLimit = await this.simulate(plan, data, head.number);

      const balance = await rpc.getBalance(plan.from, head.number);
      const needed = gasLimit * maxFeePerGas;
      if (balance < needed) {
        throw this.deferral(
          plan,
          new TransferDeferredError(
            `${plan.signer === 'DEPOSIT' ? 'Deposit' : 'Hot wallet'} ${plan.from} holds ${balance} wei, the send needs up to ${needed} wei of gas — it waits (${plan.signer === 'DEPOSIT' ? 'fuel, T153' : 'hot wallet ETH top-up, 08 §3.7.5'}).`,
            plan.signer === 'DEPOSIT' ? 'FUEL_REQUIRED' : 'HOT_WALLET_GAS_LOW',
          ),
        );
      }

      const chainNonce = await rpc.getTransactionCount(plan.from, head.number);
      const journalNext = this.deps.journal
        .bySender(plan.from)
        .reduce((next, r) => (r.nonce + 1 > next ? r.nonce + 1 : next), 0);
      const nonce = chainNonce > journalNext ? chainNonce : journalNext;

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
        feeCapWeiPerGas: capWei === null ? null : capWei.toString(),
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
          maxFeePerGas: attempt.maxFeePerGas,
          capWeiPerGas: capWei?.toString() ?? null,
        },
        'Outgoing transfer signed and journalled',
      );
      await this.broadcast(record, attempt);
      return { txHash: attempt.hash, nonce, replayed: false };
    });
  }

  private async replay(existing: JournalRecord, plan: SendPlan): Promise<SendResult> {
    const same =
      existing.purpose === plan.purpose &&
      existing.from === plan.from &&
      existing.recipient === plan.recipient &&
      existing.contract === canonicalAddress(plan.contract) &&
      existing.amountUnits === plan.amountUnits.toString() &&
      existing.depositIndex === plan.depositIndex &&
      existing.refundType === plan.refundType;
    if (!same) {
      logger.error(
        {
          id: plan.id,
          recorded: { to: existing.recipient, amount: existing.amountUnits },
          asked: { to: plan.recipient, amount: plan.amountUnits.toString() },
        },
        'IDEMPOTENCY CONFLICT — an id already signed is asked again with different parameters; refused',
      );
      throw new SidecarError(
        `Request ${plan.id} was already signed with different parameters — refused (08 §3.7.6).`,
        'IDEMPOTENCY_CONFLICT',
        false,
      );
    }
    if (existing.conflict) {
      throw new SidecarError(
        `Request ${plan.id}: its nonce was used by a transaction this service did not sign — not delivered; investigate.`,
        'TRANSFER_NONCE_CONFLICT',
        false,
      );
    }
    const latest = existing.attempts[existing.attempts.length - 1];
    if (!existing.mined) await this.broadcast(existing, latest);
    logger.info(
      { id: plan.id, hash: existing.mined?.hash ?? latest.hash },
      'Outgoing transfer replayed from the journal — nothing new signed',
    );
    return { txHash: existing.mined?.hash ?? latest.hash, nonce: existing.nonce, replayed: true };
  }

  /** Gas estimate + `eth_call` from the sender at the priced block; a revert refuses. */
  private async simulate(plan: SendPlan, data: string, block: bigint): Promise<bigint> {
    const call = { from: plan.from, to: plan.contract, data };
    let estimate: bigint;
    let result: string;
    try {
      estimate = await this.deps.rpc.estimateGas(call, block);
      result = await this.deps.rpc.callFrom(call, block);
    } catch (err) {
      if (err instanceof RpcError && err.kind === 'rpc') {
        throw new SidecarError(
          `Simulation of the transfer reverted (${err.message}) — a blacklisted or empty sender, or a paused token; nothing signed (08 §3.7.6, §3.7.8).`,
          'TRANSFER_SIMULATION_REVERTED',
          false,
        );
      }
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
    return ceilDiv(estimate * GAS_LIMIT_HEADROOM_PERCENT, 100n);
  }

  /** Policy check on the exact object, then signature, then sender re-check. */
  private sign(
    tx: Transaction,
    plan: Pick<
      SendPlan,
      'signer' | 'depositIndex' | 'from' | 'contract' | 'recipient' | 'amountUnits' | 'refundType'
    >,
    capWei: bigint | null,
  ): JournalAttempt {
    assertSignableTokenTransfer(
      tx,
      {
        contract: plan.contract,
        recipient: plan.recipient,
        amountUnits: plan.amountUnits,
        wrongTokenRefund: plan.refundType === 'WRONG_TOKEN_REFUND',
        feeCapWeiPerGas: capWei ?? undefined,
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

  /** Same nonce, fees +12.5 % (never above the record's cap); false when no valid bump exists. */
  private async tryBump(record: JournalRecord, head: BlockHeader): Promise<boolean> {
    const previous = record.attempts[record.attempts.length - 1];
    const oldMax = BigInt(previous.maxFeePerGas);
    const oldTip = BigInt(previous.maxPriorityFeePerGas);
    const quote = await quoteFees(this.deps.rpc, head);
    let tip = maxBig(quote.priorityFeePerGas, ceilDiv(oldTip * BUMP_NUMERATOR, BUMP_DENOMINATOR));
    let maxFee = maxBig(
      2n * quote.baseFeePerGas + tip,
      ceilDiv(oldMax * BUMP_NUMERATOR, BUMP_DENOMINATOR),
    );
    const cap = record.feeCapWeiPerGas === null ? null : BigInt(record.feeCapWeiPerGas);
    if (cap !== null && maxFee > cap) maxFee = cap;
    if (tip > maxFee) tip = maxFee;
    if (maxFee * 10n < oldMax * 11n || tip * 10n < oldTip * 11n) {
      return false; // At the cap: it waits for the base fee to fall (08 §3.7.7).
    }
    const gasLimit = BigInt(record.gasLimit);
    const balance = await this.deps.rpc.getBalance(record.from, head.number);
    if (balance < gasLimit * maxFee) {
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
      to: record.contract ?? '',
      value: 0n,
      data: encodeTransfer(record.recipient, BigInt(record.amountUnits)),
      accessList: [],
    });
    const attempt = this.sign(
      tx,
      {
        signer: record.signer,
        depositIndex: record.depositIndex,
        from: record.from,
        contract: record.contract ?? '',
        recipient: record.recipient,
        amountUnits: BigInt(record.amountUnits),
        refundType: (record.refundType as RefundType | null) ?? null,
      },
      cap,
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

function contractRetOf(record: JournalRecord | null, receipt: TransactionReceipt): string {
  if (receipt.status !== 1) return 'REVERT';
  if (record === null || record.contract === null) return 'SUCCESS';
  const moved = receipt.logs.some(
    (log) =>
      sameAddress(log.address, record.contract) &&
      log.topics[0]?.toLowerCase() === TRANSFER_TOPIC &&
      log.topics[1]?.toLowerCase() === addressTopic(record.from) &&
      log.topics[2]?.toLowerCase() === addressTopic(record.recipient) &&
      BigInt(log.data === '0x' ? 0 : log.data) === BigInt(record.amountUnits),
  );
  // Success without the Transfer event we sent is not a payment (a deprecated
  // USDT forwards to another contract) — reported as a failure to look at.
  return moved ? 'SUCCESS' : 'NO_TRANSFER_LOG';
}
