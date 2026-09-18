import { logger } from '../logger.js';
import { SidecarError, SimulationRevertedError } from '../errors/SidecarError.js';
import { TronDelegationClient } from '../tron/TronDelegationClient.js';
import type {
  AccountResources,
  AccountState,
  ChainFeeParameters,
} from '../tron/TronResourceClient.js';
import {
  ESTIMATED_TRANSFER_TX_BYTES,
  burnSun,
  planDelegation,
  topUpSun,
  type DelegationPlan,
} from './DelegationPlanner.js';

/**
 * Resources for ONE deposit-sourced TRC-20 transfer (sweep, refund) —
 * 08 §3.3, owner decision 2026-09-16: HYBRID. The hot wallet stakes for the
 * baseline volume; whatever the stake cannot cover burns.
 *
 * <list type="number">
 *   <item><b>Activate.</b> A deposit that has only received TRC-20 is not an
 *     account: delegation to it and a transfer from it are both rejected at
 *     validation (measured on Nile 2026-09-16). One SUN from the sweeper
 *     creates it; the chain charges the SENDER the creation fee
 *     (1 TRX + 0.1 TRX bandwidth, measured).</item>
 *   <item><b>Plan.</b> Simulate the exact transfer and decide with
 *     <see cref="planDelegation"/> for ALL of its Energy (owner decision
 *     2026-09-17): delegate the whole shortfall if the stake can, otherwise
 *     burn the whole of it. Who the chain bills is deliberately not consulted —
 *     a contract owner's share comes out of a pool the contract's other callers
 *     drain in the same block, and a transfer sized around it fails on-chain
 *     when that pool runs dry.</item>
 *   <item><b>Delegate path.</b> Delegate the planned amount, wait until it is
 *     in a block and the deposit shows the Energy, broadcast, wait until the
 *     transfer is in a block, reclaim. If the Energy does not show, reclaim
 *     and burn instead — a broadcast short of Energy fails on-chain and no
 *     fallback would follow.</item>
 *   <item><b>Burn path.</b> Send the deposit the TRX it will burn, wait until
 *     it is in a block and in the balance, then broadcast.</item>
 *   <item><b>Probe failure.</b> If the plan cannot be computed at all, send
 *     the configured fixed fallback — the pre-decision behaviour, which never
 *     blocks the money path.</item>
 *   <item><b>The transfer itself would revert.</b> A simulation the node ran
 *     and that did not succeed is not a probe failure: nothing is sent and
 *     nothing is broadcast, and the call fails retryable (owner decision
 *     2026-09-17). The likeliest cause is an earlier attempt that moved the
 *     tokens without the backend recording it; the fixed fallback there only
 *     stranded 15 TRX in the deposit and put a doomed transfer on-chain.</item>
 * </list>
 *
 * <para>
 * EVERY STEP WAITS FOR ITS BLOCK (#323 validation, 2026-09-17). An account
 * read answers from the node's pending state, so "the Energy arrived" held
 * before the delegation was in any block: the 2026-09-16 Nile run put the
 * delegation, the transfer and the reclaim in ONE block, ordered only by how
 * they reached the block producer. Waits come in two lengths. Where giving up
 * only means "retry later" (activation, a TRX top-up — a late landing is simply
 * seen by the next attempt) the flow waits <c>retryableWaitAttempts</c> polls.
 * Where giving up early could strand a delegation or reclaim it ahead of the
 * transfer it funds, it waits <c>expiryWaitAttempts</c> polls — longer than the
 * 60 s expiration the node stamps on every transaction it builds, after which
 * a transaction no block holds can never land.
 * </para>
 *
 * <para>
 * THE TRANSFER IS ONLY BROADCAST WHILE THE BACKEND IS STILL WAITING. Past
 * <c>transferBroadcastDeadlineMs</c> the flow stops before the broadcast with a
 * retryable error. A backend that gave up on this call retries it; if this
 * call had broadcast anyway, the retry would find the tokens already gone and
 * the first transfer would never be recorded. The backend's transfer timeout
 * (<c>BlockchainSidecarOptions.TransferTimeoutSeconds</c>) is sized to this
 * deadline plus the reclaim wait that follows a broadcast.
 * </para>
 *
 * Undelegation after the broadcast is best-effort: the transfer is already
 * on-chain, and failing here would re-broadcast it.
 */
export class EnergyDelegationService {
  private readonly client: TronDelegationClient;
  private readonly resources: DelegationResourceProbe;
  private readonly sweeperAddress: string;
  private readonly sweeperPrivateKey: string;
  /** Account the Energy is delegated FROM — see <c>delegationOwner</c>. */
  private readonly stakeAddress: string;
  private readonly stakePermissionId: number | undefined;
  private readonly fallbackAmountSun: number;
  private readonly pollIntervalMs: number;
  private readonly retryableWaitAttempts: number;
  private readonly expiryWaitAttempts: number;
  private readonly stateWaitAttempts: number;
  private readonly transferBroadcastDeadlineMs: number;
  private readonly sleep: (ms: number) => Promise<void>;
  private readonly now: () => number;

  constructor(deps: EnergyDelegationServiceDeps) {
    this.client = deps.client;
    this.resources = deps.resources;
    this.sweeperAddress = deps.sweeperAddress;
    this.sweeperPrivateKey = deps.sweeperPrivateKey;
    // Unset stake account = the pre-split arrangement, where the hot wallet
    // both holds the stake and signs for itself. Configuring one moves only
    // the stake; the signing key does not change and never becomes an owner.
    this.stakeAddress = deps.stakeAddress || '';
    this.stakePermissionId = this.stakeAddress ? (deps.stakePermissionId ?? 2) : undefined;
    this.fallbackAmountSun = deps.fallbackAmountSun;
    this.pollIntervalMs = deps.pollIntervalMs ?? 3_000;
    this.retryableWaitAttempts = deps.retryableWaitAttempts ?? 10;
    this.expiryWaitAttempts = deps.expiryWaitAttempts ?? 25;
    this.stateWaitAttempts = deps.stateWaitAttempts ?? 3;
    this.transferBroadcastDeadlineMs = deps.transferBroadcastDeadlineMs ?? 150_000;
    this.sleep = deps.sleep ?? ((ms) => new Promise((resolve) => setTimeout(resolve, ms)));
    this.now = deps.now ?? Date.now;
  }

  /**
   * Whose stake backs the delegation (owner decision 2026-09-17).
   *
   * <para>
   * The staked TRX is the largest value behind the hot key — a day's sales of
   * deposits sit in their own addresses, so what the hot key guards is the
   * stake plus commission. Moving the stake into its own account, whose owner
   * key stays offline, means a stolen hot key can delegate and reclaim Energy
   * but cannot unstake, transfer TRX, or rewrite the permission: all three are
   * rejected with <c>SIGERROR "Permission denied"</c> (measured on Nile,
   * 2026-09-17). The TRX this service SENDS — activation, a burn top-up, the
   * fixed fallback — still leaves the hot wallet, because that same permission
   * denies transfers; only the delegation moves.
   * </para>
   */
  private get delegationOwner(): string {
    return this.stakeAddress || this.sweeperAddress;
  }

  async withDelegation<T extends { txHash: string }>(
    transfer: DelegatedTransfer,
    action: () => Promise<T>,
    context: DelegationContext,
  ): Promise<DelegationOutcome<T>> {
    this.assertConfigured();
    const startedAt = this.now();
    const deposit = transfer.depositAddress;

    const activationSun = await this.ensureAccount(deposit, context);
    const budget = await this.acquireBudget(transfer, context);

    const elapsedMs = this.now() - startedAt;
    if (elapsedMs > this.transferBroadcastDeadlineMs) {
      if (budget.mode === 'delegated') {
        await this.tryUndelegate(deposit, budget.delegationAmountSun, context, 'deadline-elapsed');
      }
      throw new SidecarError(
        `Resources for ${deposit} took ${elapsedMs} ms; not broadcasting the transfer past ` +
          `${this.transferBroadcastDeadlineMs} ms, where the backend may already have given up on this call.`,
        'TRANSFER_WINDOW_ELAPSED',
        true,
      );
    }

    let actionResult: T;
    try {
      actionResult = await action();
    } catch (err) {
      // Reclaim before re-throwing so a retry does not find a stale budget.
      if (budget.mode === 'delegated') {
        await this.tryUndelegate(deposit, budget.delegationAmountSun, context, 'action-failed');
      }
      throw err;
    }

    if (budget.mode === 'delegated') {
      await this.awaitTransferBlock(deposit, actionResult.txHash, context);
      await this.tryUndelegate(deposit, budget.delegationAmountSun, context, 'action-succeeded');
    }

    return { ...budget, activationSun, action: actionResult };
  }

  /** Create the deposit account if it does not exist yet. Returns SUN sent (0 or 1). */
  private async ensureAccount(deposit: string, context: DelegationContext): Promise<number> {
    const state = await this.resources.getAccountState(deposit);
    if (state.exists) return 0;

    const { txHash } = await this.client.sendTrx({
      fromAddress: this.sweeperAddress,
      fromPrivateKey: this.sweeperPrivateKey,
      toAddress: deposit,
      amountSun: 1,
    });
    const landed = await this.landed(
      txHash,
      this.retryableWaitAttempts,
      async () => (await this.resources.getAccountState(deposit)).exists,
    );
    if (landed !== 'confirmed') {
      throw new SidecarError(
        `Deposit ${deposit} activation ${txHash} ${describeWait(landed)}.`,
        'DEPOSIT_ACTIVATION_NOT_CONFIRMED',
        true,
      );
    }
    logger.info(
      { depositAddress: deposit, txHash, ...contextFields(context) },
      'Deposit account activated in a block (1 SUN; creation fee charged to the sweeper)',
    );
    return 1;
  }

  private async acquireBudget(
    transfer: DelegatedTransfer,
    context: DelegationContext,
  ): Promise<Budget> {
    const deposit = transfer.depositAddress;

    let probes: PlanProbes;
    try {
      probes = await this.readProbes(transfer);
    } catch (err) {
      if (err instanceof SimulationRevertedError) {
        logger.warn(
          { reason: err.reason, depositAddress: deposit, ...contextFields(context) },
          'Deposit transfer would fail on-chain — nothing sent, nothing broadcast',
        );
        throw new SidecarError(
          `The transfer from ${deposit} simulates as failed (${err.reason}); nothing was sent or broadcast. ` +
            "If an earlier attempt of this transfer timed out, check the deposit's outgoing transfers before re-issuing it.",
          'DEPOSIT_TRANSFER_WOULD_REVERT',
          true,
        );
      }
      logger.warn(
        { err: (err as Error).message, depositAddress: deposit, ...contextFields(context) },
        'Delegation plan unavailable — sending the fixed TRX fallback',
      );
      await this.sendAndConfirmTrx(deposit, this.fallbackAmountSun, 0, context, 'FALLBACK');
      return {
        mode: 'fallback',
        delegationAmountSun: 0,
        fallbackAmountSun: this.fallbackAmountSun,
      };
    }

    const { plan } = probes;
    logger.info(
      { depositAddress: deposit, plan, ...contextFields(context) },
      'Deposit transfer resource plan',
    );

    if (plan.kind === 'delegate') {
      const delegated = await this.tryDelegate(deposit, plan, context);
      if (delegated) {
        let sent: number;
        try {
          sent = await this.topUpBandwidthIfNeeded(deposit, probes, context);
        } catch (err) {
          // Nothing is broadcast after this; a delegation left in place would
          // stay with the deposit until an admin reclaims it.
          await this.tryUndelegate(deposit, plan.delegationSun, context, 'bandwidth-failed');
          throw err;
        }
        return {
          mode: 'delegated',
          delegationAmountSun: plan.delegationSun,
          fallbackAmountSun: sent,
        };
      }
      // Delegation did not deliver: burn the whole shortfall instead.
      return this.burn(deposit, plan.energyShortfall, probes, context);
    }

    if (plan.kind === 'burn') {
      return this.burn(deposit, plan.energyShortfall, probes, context);
    }

    // no-energy-needed — the deposit already holds the Energy for the whole
    // transfer; only Bandwidth can still be short.
    const sent = await this.topUpBandwidthIfNeeded(deposit, probes, context);
    return { mode: 'no-energy', delegationAmountSun: 0, fallbackAmountSun: sent };
  }

  private async readProbes(transfer: DelegatedTransfer): Promise<PlanProbes> {
    const simulation = this.resources.estimateTransferEnergy(
      transfer.contractAddress,
      transfer.depositAddress,
      transfer.toAddress,
      transfer.amountUnits,
    );
    const reads = Promise.all([
      this.resources.getAccountResources(transfer.depositAddress),
      // Only for the network-wide Energy/TRX ratio, which every account read
      // carries — deliberately NOT the delegation owner, so that reading the
      // ratio and reading the stake stay two separate questions.
      this.resources.getAccountResources(this.sweeperAddress),
      // How much this account may still delegate out. It must be the account
      // that HOLDS the stake, not the one that signs for it.
      this.resources.getDelegatableEnergySun(this.delegationOwner),
      this.resources.getChainFeeParameters(),
      this.resources.getAccountState(transfer.depositAddress),
    ]);
    // Everything settles before a failure is raised, and the simulation's own
    // failure wins: "this transfer would revert" must not be lost to an
    // unrelated read failing first, which would send the fixed fallback.
    const [simulated, read] = await Promise.allSettled([simulation, reads]);
    if (simulated.status === 'rejected') throw simulated.reason;
    if (read.status === 'rejected') throw read.reason;
    const energyRequired = simulated.value;
    const [depositResources, sweeperResources, delegatableSun, fees, state] = read.value;

    const plan = planDelegation({
      energyRequired,
      senderEnergyAvailable: depositResources.energyAvailable,
      energyPerTrx: sweeperResources.energyPerTrx,
      delegatableSun,
    });
    return { plan, depositResources, fees, balanceSun: state.balanceSun };
  }

  /** Delegate and confirm the Energy is usable in a block; reclaim and report false if not. */
  private async tryDelegate(
    deposit: string,
    plan: Extract<DelegationPlan, { kind: 'delegate' }>,
    context: DelegationContext,
  ): Promise<boolean> {
    let txHash: string;
    try {
      ({ txHash } = await this.client.delegateEnergy({
        ownerAddress: this.delegationOwner,
        ownerPrivateKey: this.sweeperPrivateKey,
        ownerPermissionId: this.stakePermissionId,
        receiverAddress: deposit,
        amountSun: plan.delegationSun,
      }));
    } catch (err) {
      logger.warn(
        {
          err: (err as Error).message,
          depositAddress: deposit,
          amountSun: plan.delegationSun,
          ...contextFields(context),
        },
        'Energy delegation broadcast failed — burning instead',
      );
      return false;
    }

    const landed = await this.landed(
      txHash,
      this.expiryWaitAttempts,
      async () =>
        (await this.resources.getAccountResources(deposit)).energyAvailable >= plan.energyRequired,
    );
    const fields = {
      depositAddress: deposit,
      txHash,
      amountSun: plan.delegationSun,
      energyRequired: plan.energyRequired,
      ...contextFields(context),
    };
    if (landed === 'confirmed') {
      logger.info(fields, 'Energy delegation confirmed in a block');
      return true;
    }

    logger.warn(
      { ...fields, outcome: landed },
      'Delegated Energy is not usable — reclaiming and burning instead',
    );
    await this.tryUndelegate(deposit, plan.delegationSun, context, 'delegation-short');
    return false;
  }

  /**
   * A reclaim that reaches a block ahead of the transfer it funds leaves that
   * transfer without Energy, and it fails on-chain. Wait for the transfer's
   * block; a transfer no block holds within the expiry wait can no longer land,
   * so reclaiming after it is safe either way.
   */
  private async awaitTransferBlock(
    deposit: string,
    txHash: string,
    context: DelegationContext,
  ): Promise<void> {
    const block = await this.waitForBlock(txHash, this.expiryWaitAttempts);
    if (block === null) {
      logger.warn(
        { depositAddress: deposit, txHash, ...contextFields(context) },
        'Transfer is in no block after its expiration window — reclaiming the delegation anyway',
      );
    }
  }

  private async burn(
    deposit: string,
    energyShortfall: number,
    probes: PlanProbes,
    context: DelegationContext,
  ): Promise<Budget> {
    const required = burnSun({
      energyToBurn: energyShortfall,
      energyFeeSun: probes.fees.energyFeeSun,
      bandwidthAvailable: probes.depositResources.bandwidthAvailable,
      txBytes: ESTIMATED_TRANSFER_TX_BYTES,
      bandwidthFeeSun: probes.fees.bandwidthFeeSun,
    });
    const sent = topUpSun(required, probes.balanceSun);
    if (sent > 0) {
      await this.sendAndConfirmTrx(deposit, sent, probes.balanceSun, context, 'BURN_TOP_UP');
    }
    return { mode: 'burn', delegationAmountSun: 0, fallbackAmountSun: sent };
  }

  /** Bandwidth only (delegated or no-energy paths). Returns SUN sent. */
  private async topUpBandwidthIfNeeded(
    deposit: string,
    probes: PlanProbes,
    context: DelegationContext,
  ): Promise<number> {
    const required = burnSun({
      energyToBurn: 0,
      energyFeeSun: probes.fees.energyFeeSun,
      bandwidthAvailable: probes.depositResources.bandwidthAvailable,
      txBytes: ESTIMATED_TRANSFER_TX_BYTES,
      bandwidthFeeSun: probes.fees.bandwidthFeeSun,
    });
    const sent = topUpSun(required, probes.balanceSun);
    if (sent > 0) {
      await this.sendAndConfirmTrx(deposit, sent, probes.balanceSun, context, 'BANDWIDTH_TOP_UP');
    }
    return sent;
  }

  private async sendAndConfirmTrx(
    deposit: string,
    amountSun: number,
    balanceBeforeSun: number,
    context: DelegationContext,
    purpose: 'FALLBACK' | 'BURN_TOP_UP' | 'BANDWIDTH_TOP_UP',
  ): Promise<void> {
    let txHash: string;
    try {
      ({ txHash } = await this.client.sendTrx({
        fromAddress: this.sweeperAddress,
        fromPrivateKey: this.sweeperPrivateKey,
        toAddress: deposit,
        amountSun,
      }));
    } catch (err) {
      throw new SidecarError(
        `TRX ${purpose} to ${deposit} failed: ${(err as Error).message}`,
        'DELEGATION_AND_FALLBACK_FAILED',
        true,
      );
    }
    // A transfer broadcast before the TRX is in a block can reach the block
    // producer first and fail on Energy with nothing left to fall back to.
    const landed = await this.landed(
      txHash,
      this.retryableWaitAttempts,
      async () =>
        (await this.resources.getAccountState(deposit)).balanceSun >= balanceBeforeSun + amountSun,
    );
    if (landed !== 'confirmed') {
      throw new SidecarError(
        `TRX ${purpose} to ${deposit} (${txHash}) ${describeWait(landed)}.`,
        'TRX_TOP_UP_NOT_CONFIRMED',
        true,
      );
    }
    logger.info(
      { depositAddress: deposit, txHash, amountSun, purpose, ...contextFields(context) },
      'TRX sent to deposit and confirmed in a block',
    );
  }

  private async tryUndelegate(
    deposit: string,
    amountSun: number,
    context: DelegationContext,
    phase:
      | 'action-succeeded'
      | 'action-failed'
      | 'delegation-short'
      | 'bandwidth-failed'
      | 'deadline-elapsed',
  ): Promise<void> {
    try {
      await this.client.undelegateEnergy({
        ownerAddress: this.delegationOwner,
        ownerPrivateKey: this.sweeperPrivateKey,
        ownerPermissionId: this.stakePermissionId,
        receiverAddress: deposit,
        amountSun,
      });
    } catch (undelegateErr) {
      logger.warn(
        {
          err: (undelegateErr as Error).message,
          depositAddress: deposit,
          amountSun,
          phase,
          ...contextFields(context),
        },
        'Energy undelegate failed — admin investigation required (stranded delegation)',
      );
    }
  }

  /**
   * <paramref name="txHash"/> in a block, then the account read showing it.
   * TronGrid load-balances reads, so the node answering the second question
   * can be a block behind the one that answered the first.
   */
  private async landed(
    txHash: string,
    blockWaitAttempts: number,
    reflected: () => Promise<boolean>,
  ): Promise<WaitOutcome> {
    if ((await this.waitForBlock(txHash, blockWaitAttempts)) === null) return 'not-in-block';
    return (await this.waitFor(reflected, this.stateWaitAttempts)) ? 'confirmed' : 'not-reflected';
  }

  private async waitForBlock(txHash: string, attempts: number): Promise<number | null> {
    for (let attempt = 0; attempt < attempts; attempt++) {
      try {
        const block = await this.resources.getTransactionBlockNumber(txHash);
        if (block !== null) return block;
      } catch {
        // A transient probe error is not an answer; keep polling.
      }
      await this.sleep(this.pollIntervalMs);
    }
    return null;
  }

  private async waitFor(condition: () => Promise<boolean>, attempts: number): Promise<boolean> {
    for (let attempt = 0; attempt < attempts; attempt++) {
      try {
        if (await condition()) return true;
      } catch {
        // A transient probe error is not an answer; keep polling.
      }
      await this.sleep(this.pollIntervalMs);
    }
    return false;
  }

  private assertConfigured(): void {
    if (!this.sweeperAddress || !this.sweeperPrivateKey) {
      throw new SidecarError(
        'Sweeper credentials missing — delegation cannot run without HOT_WALLET_ADDRESS + HOT_WALLET_PRIVATE_KEY.',
        'SWEEPER_NOT_CONFIGURED',
        false,
      );
    }
    if (!Number.isFinite(this.fallbackAmountSun) || this.fallbackAmountSun <= 0) {
      throw new SidecarError(
        `Invalid SWEEP_TRX_FALLBACK_SUN value: ${this.fallbackAmountSun}`,
        'INVALID_FALLBACK_AMOUNT',
        false,
      );
    }
  }
}

type WaitOutcome = 'confirmed' | 'not-in-block' | 'not-reflected';

function describeWait(outcome: Exclude<WaitOutcome, 'confirmed'>): string {
  return outcome === 'not-in-block'
    ? 'is not in a block yet'
    : 'is in a block, but the account read does not show it yet';
}

function contextFields(context: DelegationContext) {
  return {
    correlationId: context.correlationId,
    blockchainTransactionId: context.blockchainTransactionId,
  };
}

/** The subset of <c>TronResourceClient</c> the flow needs — injectable for tests. */
export interface DelegationResourceProbe {
  estimateTransferEnergy(
    contractAddress: string,
    fromAddress: string,
    toAddress: string,
    amountUnits: string,
  ): Promise<number>;
  getAccountResources(address: string): Promise<AccountResources>;
  getAccountState(address: string): Promise<AccountState>;
  getDelegatableEnergySun(ownerAddress: string): Promise<number>;
  getChainFeeParameters(): Promise<ChainFeeParameters>;
  getTransactionBlockNumber(txHash: string): Promise<number | null>;
}

export interface EnergyDelegationServiceDeps {
  client: TronDelegationClient;
  resources: DelegationResourceProbe;
  /** Hot wallet: the signing key, and the account every TRX this service sends
   * leaves from (activation, burn top-up, fixed fallback). */
  sweeperAddress: string;
  sweeperPrivateKey: string;
  /** Dedicated stake account holding the frozen TRX, signed for by the hot
   * wallet's key through an active permission. Empty = the hot wallet holds
   * its own stake, the arrangement before the 2026-09-17 split. */
  stakeAddress?: string;
  /** Active-permission id on the stake account; ignored when there is none.
   * Default 2 — the first id the chain assigns to an added active permission. */
  stakePermissionId?: number;
  /** SUN sent when the plan itself cannot be computed (08 §3.3 fallback). */
  fallbackAmountSun: number;
  /** Poll spacing — one TRON block (default 3 s). */
  pollIntervalMs?: number;
  /** Block wait where a timeout only means "retry later": activation, TRX top-ups (default 10 polls ≈ 30 s). */
  retryableWaitAttempts?: number;
  /**
   * Block wait that must outlast the node's 60 s transaction expiration: the
   * delegation, and the transfer before its reclaim (default 25 polls ≈ 75 s).
   */
  expiryWaitAttempts?: number;
  /** Polls, after the block, for the account read to show it (default 3). */
  stateWaitAttempts?: number;
  /** No transfer broadcast after this long since the call started (default 150 s). */
  transferBroadcastDeadlineMs?: number;
  sleep?: (ms: number) => Promise<void>;
  now?: () => number;
}

export interface DelegatedTransfer {
  depositAddress: string;
  contractAddress: string;
  toAddress: string;
  amountUnits: string;
}

export interface DelegationContext {
  blockchainTransactionId: string;
  correlationId: string;
}

/**
 * <c>delegated</c> — stake covered the Energy · <c>burn</c> — the deposit burned
 * TRX sent for exactly this transfer · <c>no-energy</c> — the deposit already
 * held the Energy for the whole transfer · <c>fallback</c> — the plan could not
 * be computed and the fixed amount was sent.
 */
export type DelegationMode = 'delegated' | 'burn' | 'no-energy' | 'fallback';

interface Budget {
  mode: DelegationMode;
  delegationAmountSun: number;
  /** TRX sent to the deposit (burn top-up, bandwidth top-up or fixed fallback). */
  fallbackAmountSun: number;
}

interface PlanProbes {
  plan: DelegationPlan;
  depositResources: AccountResources;
  fees: ChainFeeParameters;
  balanceSun: number;
}

export interface DelegationOutcome<T> extends Budget {
  /** 1 when this call created the deposit account, 0 otherwise. */
  activationSun: number;
  action: T;
}
