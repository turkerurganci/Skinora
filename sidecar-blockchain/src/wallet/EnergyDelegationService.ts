import { logger } from '../logger.js';
import { SidecarError } from '../errors/SidecarError.js';
import { TronDelegationClient } from '../tron/TronDelegationClient.js';
import type {
  AccountResources,
  AccountState,
  ChainFeeParameters,
  ContractEnergyPolicy,
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
 *   <item><b>Plan.</b> Simulate the exact transfer, read who pays its Energy,
 *     and decide with <see cref="planDelegation"/>: delegate the WHOLE
 *     shortfall if the stake can, otherwise burn the whole of it.</item>
 *   <item><b>Delegate path.</b> Delegate exactly the planned amount, then
 *     confirm the deposit actually received the Energy before broadcasting.
 *     If it did not (the ratio moved, the block has not landed), reclaim and
 *     burn instead — a broadcast short of Energy fails on-chain and no
 *     fallback would follow.</item>
 *   <item><b>Burn path.</b> Send the deposit the TRX it will burn (computed
 *     from the simulation and current chain prices), confirm it arrived, then
 *     broadcast.</item>
 *   <item><b>Probe failure.</b> If the plan cannot be computed at all, send
 *     the configured fixed fallback — the pre-decision behaviour, which never
 *     blocks the money path.</item>
 * </list>
 *
 * Undelegation after the broadcast is best-effort: the transfer is already
 * on-chain, and failing here would re-broadcast it.
 */
export class EnergyDelegationService {
  private readonly client: TronDelegationClient;
  private readonly resources: DelegationResourceProbe;
  private readonly sweeperAddress: string;
  private readonly sweeperPrivateKey: string;
  private readonly fallbackAmountSun: number;
  private readonly pollIntervalMs: number;
  private readonly pollAttempts: number;
  private readonly sleep: (ms: number) => Promise<void>;

  constructor(deps: EnergyDelegationServiceDeps) {
    this.client = deps.client;
    this.resources = deps.resources;
    this.sweeperAddress = deps.sweeperAddress;
    this.sweeperPrivateKey = deps.sweeperPrivateKey;
    this.fallbackAmountSun = deps.fallbackAmountSun;
    this.pollIntervalMs = deps.pollIntervalMs ?? 3_000;
    this.pollAttempts = deps.pollAttempts ?? 20;
    this.sleep = deps.sleep ?? ((ms) => new Promise((resolve) => setTimeout(resolve, ms)));
  }

  async withDelegation<T>(
    transfer: DelegatedTransfer,
    action: () => Promise<T>,
    context: DelegationContext,
  ): Promise<DelegationOutcome<T>> {
    this.assertConfigured();
    const deposit = transfer.depositAddress;

    const activationSun = await this.ensureAccount(deposit, context);
    const budget = await this.acquireBudget(transfer, context);

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
      await this.tryUndelegate(deposit, budget.delegationAmountSun, context, 'action-succeeded');
    }

    return { ...budget, activationSun, action: actionResult };
  }

  /** Create the deposit account if it does not exist yet. Returns SUN sent (0 or 1). */
  private async ensureAccount(deposit: string, context: DelegationContext): Promise<number> {
    const state = await this.resources.getAccountState(deposit);
    if (state.exists) return 0;

    await this.client.sendTrx({
      fromAddress: this.sweeperAddress,
      fromPrivateKey: this.sweeperPrivateKey,
      toAddress: deposit,
      amountSun: 1,
    });
    const created = await this.waitFor(
      async () => (await this.resources.getAccountState(deposit)).exists,
    );
    if (!created) {
      throw new SidecarError(
        `Deposit ${deposit} activation was broadcast but the account is not visible yet.`,
        'DEPOSIT_ACTIVATION_NOT_CONFIRMED',
        true,
      );
    }
    logger.info(
      { depositAddress: deposit, ...contextFields(context) },
      'Deposit account activated (1 SUN; creation fee charged to the sweeper)',
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
      probes = await this.readProbes(transfer, context);
    } catch (err) {
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
        await this.topUpBandwidthIfNeeded(deposit, probes, context);
        return { mode: 'delegated', delegationAmountSun: plan.delegationSun, fallbackAmountSun: 0 };
      }
      // Delegation did not deliver: burn the whole shortfall instead.
      return this.burn(deposit, plan.energyShortfall, probes, context);
    }

    if (plan.kind === 'burn') {
      return this.burn(deposit, plan.energyShortfall, probes, context);
    }

    // no-energy-needed — the contract owner (or existing Energy) pays; only
    // Bandwidth can still be short.
    const sent = await this.topUpBandwidthIfNeeded(deposit, probes, context);
    return { mode: 'no-energy', delegationAmountSun: 0, fallbackAmountSun: sent };
  }

  private async readProbes(
    transfer: DelegatedTransfer,
    context: DelegationContext,
  ): Promise<PlanProbes> {
    const [energyRequired, policy, depositResources, hotResources, delegatableSun, fees, state] =
      await Promise.all([
        this.resources.estimateTransferEnergy(
          transfer.contractAddress,
          transfer.depositAddress,
          transfer.toAddress,
          transfer.amountUnits,
        ),
        this.resources.getContractEnergyPolicy(transfer.contractAddress).catch((err: Error) => {
          // Conservative: the caller pays everything. Mainnet Tether sets 100.
          logger.warn(
            {
              err: err.message,
              contractAddress: transfer.contractAddress,
              ...contextFields(context),
            },
            'Contract energy policy unreadable — planning as if the caller pays 100%',
          );
          return { callerPercent: 100, originEnergyLimit: 0 } satisfies ContractEnergyPolicy;
        }),
        this.resources.getAccountResources(transfer.depositAddress),
        this.resources.getAccountResources(this.sweeperAddress),
        this.resources.getDelegatableEnergySun(this.sweeperAddress),
        this.resources.getChainFeeParameters(),
        this.resources.getAccountState(transfer.depositAddress),
      ]);

    const plan = planDelegation({
      energyRequired,
      callerPercent: policy.callerPercent,
      senderEnergyAvailable: depositResources.energyAvailable,
      energyPerTrx: hotResources.energyPerTrx,
      delegatableSun,
    });
    return { plan, depositResources, fees, balanceSun: state.balanceSun };
  }

  /** Delegate and confirm the Energy arrived; reclaim and report false if it did not. */
  private async tryDelegate(
    deposit: string,
    plan: Extract<DelegationPlan, { kind: 'delegate' }>,
    context: DelegationContext,
  ): Promise<boolean> {
    try {
      await this.client.delegateEnergy({
        ownerAddress: this.sweeperAddress,
        ownerPrivateKey: this.sweeperPrivateKey,
        receiverAddress: deposit,
        amountSun: plan.delegationSun,
      });
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

    const arrived = await this.waitFor(
      async () =>
        (await this.resources.getAccountResources(deposit)).energyAvailable >= plan.callerEnergy,
    );
    if (arrived) {
      logger.info(
        {
          depositAddress: deposit,
          amountSun: plan.delegationSun,
          callerEnergy: plan.callerEnergy,
          ...contextFields(context),
        },
        'Energy delegation confirmed',
      );
      return true;
    }

    logger.warn(
      {
        depositAddress: deposit,
        amountSun: plan.delegationSun,
        callerEnergy: plan.callerEnergy,
        ...contextFields(context),
      },
      'Delegated Energy did not reach the required amount — reclaiming and burning instead',
    );
    await this.tryUndelegate(deposit, plan.delegationSun, context, 'delegation-short');
    return false;
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
    try {
      await this.client.sendTrx({
        fromAddress: this.sweeperAddress,
        fromPrivateKey: this.sweeperPrivateKey,
        toAddress: deposit,
        amountSun,
      });
    } catch (err) {
      throw new SidecarError(
        `TRX ${purpose} to ${deposit} failed: ${(err as Error).message}`,
        'DELEGATION_AND_FALLBACK_FAILED',
        true,
      );
    }
    // Broadcasting the transfer before the TRX lands would make it fail on
    // Energy with nothing left to fall back to.
    const arrived = await this.waitFor(
      async () =>
        (await this.resources.getAccountState(deposit)).balanceSun >= balanceBeforeSun + amountSun,
    );
    if (!arrived) {
      throw new SidecarError(
        `TRX ${purpose} to ${deposit} was broadcast but has not arrived yet.`,
        'TRX_TOP_UP_NOT_CONFIRMED',
        true,
      );
    }
    logger.info(
      { depositAddress: deposit, amountSun, purpose, ...contextFields(context) },
      'TRX sent to deposit and confirmed',
    );
  }

  private async tryUndelegate(
    deposit: string,
    amountSun: number,
    context: DelegationContext,
    phase: 'action-succeeded' | 'action-failed' | 'delegation-short',
  ): Promise<void> {
    try {
      await this.client.undelegateEnergy({
        ownerAddress: this.sweeperAddress,
        ownerPrivateKey: this.sweeperPrivateKey,
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

  private async waitFor(condition: () => Promise<boolean>): Promise<boolean> {
    for (let attempt = 0; attempt < this.pollAttempts; attempt++) {
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
  getContractEnergyPolicy(contractAddress: string): Promise<ContractEnergyPolicy>;
  getAccountResources(address: string): Promise<AccountResources>;
  getAccountState(address: string): Promise<AccountState>;
  getDelegatableEnergySun(ownerAddress: string): Promise<number>;
  getChainFeeParameters(): Promise<ChainFeeParameters>;
}

export interface EnergyDelegationServiceDeps {
  client: TronDelegationClient;
  resources: DelegationResourceProbe;
  /** Sweeper account (hot wallet in MVP — 2026-05-17 scope decision). */
  sweeperAddress: string;
  sweeperPrivateKey: string;
  /** SUN sent when the plan itself cannot be computed (08 §3.3 fallback). */
  fallbackAmountSun: number;
  /** Confirmation polling (defaults: 3 s × 20). */
  pollIntervalMs?: number;
  pollAttempts?: number;
  sleep?: (ms: number) => Promise<void>;
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
 * TRX sent for exactly this transfer · <c>no-energy</c> — the caller owed no
 * Energy (contract owner pays) · <c>fallback</c> — the plan could not be
 * computed and the fixed amount was sent.
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
