import { logger } from '../logger.js';
import { SidecarError } from '../errors/SidecarError.js';
import { TronDelegationClient } from '../tron/TronDelegationClient.js';

/**
 * Orchestrates 08 §3.3 Energy delegation around a single outbound broadcast
 * (sweep, deposit-sourced refund). The deposit address holds only TRC-20
 * tokens — without Energy, its outgoing transfer would either burn its tiny
 * (or zero) TRX balance or fail outright. The flow is:
 *
 * <list type="number">
 *   <item>Sweeper (the hot wallet in MVP) issues <c>delegateresource</c> to
 *     the deposit with <c>lock=false</c>, granting Energy for one broadcast.</item>
 *   <item>The caller's <c>action</c> runs — the actual TRC-20 transfer
 *     consumes that delegated Energy.</item>
 *   <item><c>undelegateresource</c> reclaims the budget, regardless of
 *     whether <c>action</c> succeeded or threw.</item>
 * </list>
 *
 * <para>
 * Failure semantics:
 * <list type="bullet">
 *   <item>Delegation broadcast fails → fall back to a small TRX transfer
 *     (08 §3.3 "delegation başarısızsa deposit adresine minimum TRX transfer").
 *     The deposit then pays its own Energy out of TRX.</item>
 *   <item>Both delegation AND the fallback fail → <c>DELEGATION_AND_FALLBACK_FAILED</c>
 *     bubbles up; the upstream <c>OutgoingTransferDispatchJob</c> sees this
 *     as a transient error and reschedules the row per its retry policy.</item>
 *   <item><c>action</c> succeeds, undelegate fails → log <c>WARN</c> and
 *     continue. The sweep is already on-chain; surfacing this as a failure
 *     would re-broadcast the same TRC-20 transfer. Admin tooling (T96)
 *     surfaces stranded delegations from <see cref="getDelegationStateLog"/>.</item>
 * </list>
 * </para>
 */
export class EnergyDelegationService {
  private readonly client: TronDelegationClient;
  private readonly sweeperAddress: string;
  private readonly sweeperPrivateKey: string;
  private readonly delegationAmountSun: number;
  private readonly fallbackAmountSun: number;

  constructor(deps: EnergyDelegationServiceDeps) {
    this.client = deps.client;
    this.sweeperAddress = deps.sweeperAddress;
    this.sweeperPrivateKey = deps.sweeperPrivateKey;
    this.delegationAmountSun = deps.delegationAmountSun;
    this.fallbackAmountSun = deps.fallbackAmountSun;
  }

  /**
   * Run <paramref name="action"/> with Energy delegated to
   * <paramref name="depositAddress"/>. Always reclaims (best-effort) when a
   * delegation actually went through; falls back to TRX prefund if not.
   */
  async withDelegation<T>(
    depositAddress: string,
    action: () => Promise<T>,
    context: DelegationContext,
  ): Promise<DelegationOutcome<T>> {
    this.assertConfigured();

    const mode = await this.acquireBudget(depositAddress, context);

    let actionResult: T;
    try {
      actionResult = await action();
    } catch (err) {
      // The broadcast itself failed — try to reclaim before re-throwing so
      // we don't leak a budget into the deposit for the next retry attempt.
      if (mode === 'delegated') {
        await this.tryUndelegate(depositAddress, context, 'action-failed');
      }
      throw err;
    }

    if (mode === 'delegated') {
      await this.tryUndelegate(depositAddress, context, 'action-succeeded');
    }

    return {
      mode,
      delegationAmountSun: mode === 'delegated' ? this.delegationAmountSun : 0,
      fallbackAmountSun: mode === 'fallback' ? this.fallbackAmountSun : 0,
      action: actionResult,
    };
  }

  private async acquireBudget(
    depositAddress: string,
    context: DelegationContext,
  ): Promise<DelegationMode> {
    try {
      await this.client.delegateEnergy({
        ownerAddress: this.sweeperAddress,
        ownerPrivateKey: this.sweeperPrivateKey,
        receiverAddress: depositAddress,
        amountSun: this.delegationAmountSun,
      });
      logger.info(
        {
          depositAddress,
          amountSun: this.delegationAmountSun,
          correlationId: context.correlationId,
          blockchainTransactionId: context.blockchainTransactionId,
        },
        'Energy delegation acquired',
      );
      return 'delegated';
    } catch (delegateErr) {
      logger.warn(
        {
          err: (delegateErr as Error).message,
          depositAddress,
          correlationId: context.correlationId,
          blockchainTransactionId: context.blockchainTransactionId,
        },
        'Energy delegation failed — attempting TRX fallback',
      );
      try {
        await this.client.sendTrx({
          fromAddress: this.sweeperAddress,
          fromPrivateKey: this.sweeperPrivateKey,
          toAddress: depositAddress,
          amountSun: this.fallbackAmountSun,
        });
        logger.info(
          {
            depositAddress,
            amountSun: this.fallbackAmountSun,
            correlationId: context.correlationId,
            blockchainTransactionId: context.blockchainTransactionId,
          },
          'TRX fallback prefund acquired',
        );
        return 'fallback';
      } catch (fallbackErr) {
        const message =
          `Energy delegation and TRX fallback both failed for ${depositAddress}. ` +
          `delegate=${(delegateErr as Error).message}; fallback=${(fallbackErr as Error).message}`;
        logger.error(
          {
            depositAddress,
            correlationId: context.correlationId,
            blockchainTransactionId: context.blockchainTransactionId,
          },
          message,
        );
        throw new SidecarError(message, 'DELEGATION_AND_FALLBACK_FAILED', true);
      }
    }
  }

  private async tryUndelegate(
    depositAddress: string,
    context: DelegationContext,
    phase: 'action-succeeded' | 'action-failed',
  ): Promise<void> {
    try {
      await this.client.undelegateEnergy({
        ownerAddress: this.sweeperAddress,
        ownerPrivateKey: this.sweeperPrivateKey,
        receiverAddress: depositAddress,
        amountSun: this.delegationAmountSun,
      });
    } catch (undelegateErr) {
      logger.warn(
        {
          err: (undelegateErr as Error).message,
          depositAddress,
          amountSun: this.delegationAmountSun,
          correlationId: context.correlationId,
          blockchainTransactionId: context.blockchainTransactionId,
          phase,
        },
        'Energy undelegate failed — admin investigation required (stranded delegation)',
      );
    }
  }

  private assertConfigured(): void {
    if (!this.sweeperAddress || !this.sweeperPrivateKey) {
      throw new SidecarError(
        'Sweeper credentials missing — delegation cannot run without HOT_WALLET_ADDRESS + HOT_WALLET_PRIVATE_KEY.',
        'SWEEPER_NOT_CONFIGURED',
        false,
      );
    }
    if (!Number.isFinite(this.delegationAmountSun) || this.delegationAmountSun <= 0) {
      throw new SidecarError(
        `Invalid SWEEP_ENERGY_DELEGATION_SUN value: ${this.delegationAmountSun}`,
        'INVALID_DELEGATION_AMOUNT',
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

export interface EnergyDelegationServiceDeps {
  client: TronDelegationClient;
  /** Sweeper account (hot wallet in MVP — 2026-05-17 scope decision). */
  sweeperAddress: string;
  sweeperPrivateKey: string;
  /** SUN units delegated per broadcast (08 §3.3 — admin-tunable via
   * <c>blockchain.sweep_energy_delegation_sun</c> SystemSetting; sidecar reads
   * from env at startup). */
  delegationAmountSun: number;
  /** SUN units transferred as fallback when delegation fails (08 §3.3). */
  fallbackAmountSun: number;
}

export interface DelegationContext {
  blockchainTransactionId: string;
  correlationId: string;
}

export type DelegationMode = 'delegated' | 'fallback';

export interface DelegationOutcome<T> {
  mode: DelegationMode;
  delegationAmountSun: number;
  fallbackAmountSun: number;
  action: T;
}
