import { logger } from '../logger.js';
import { SidecarError } from '../errors/SidecarError.js';
import { TronResourceClient } from '../tron/TronResourceClient.js';
import { TransferService, TokenContractMap, TokenSymbol } from '../transfer/TransferService.js';
import { TrxPriceService, TrxPriceSource } from './TrxPriceService.js';
import {
  ACTIVATED_ACCOUNT_FREE_BANDWIDTH,
  ESTIMATED_TRANSFER_TX_BYTES,
  planDelegation,
  type DelegationPlan,
} from '../wallet/DelegationPlanner.js';

/**
 * Pre-send fee estimate for an outbound TRC-20 transfer
 * (Prova-GasFeeChargedIsFixedGuess — owner decision 2026-09-02: charge the
 * computed cost, not a constant). The model mirrors what the broadcast will
 * actually do:
 *
 * <list type="number">
 *   <item>Energy the transfer needs — `triggerconstantcontract` simulation of
 *     the exact call (captures the recipient-holds-balance ~64k vs ~130k split).</item>
 *   <item>Energy the platform already owns — the HOT WALLET's spendable
 *     Energy, because it is the resource provider on both paths: payouts
 *     spend it directly, refunds receive it via `delegateresource`
 *     (EnergyDelegationService). Covered Energy costs nothing reusable-stake
 *     aside; only the shortfall burns TRX at the chain's Energy unit price.</item>
 *   <item>Bandwidth — the SENDER's own allowance (deposit addresses typically
 *     have none); the shortfall burns TRX at the chain's byte price.</item>
 *   <item>Burned sun → USDT at the live TRX/USDT price, rounded UP to the
 *     2-decimal charge precision.</item>
 * </list>
 *
 * The estimate can still drift from the realized fee (recipient balance or
 * chain prices may change between estimate and broadcast); that residual
 * variance stays on the platform by design — the user is charged the value
 * computed here, snapshotted onto <c>BlockchainTransaction.GasFee</c>.
 */

// Lives with the planner so the delegation flow can use it without importing this
// module (which imports TransferService, which imports the delegation flow).
export { ESTIMATED_TRANSFER_TX_BYTES } from '../wallet/DelegationPlanner.js';

export interface FeeEstimateRequest {
  /** Sender. Omitted → hot wallet (payout path). Refunds pass the deposit address. */
  fromAddress?: string;
  toAddress: string;
  /** Human units, e.g. "10.20". */
  amount: string;
  token: TokenSymbol;
  correlationId?: string;
}

export interface FeeEstimateResult {
  /** Charge amount in USDT, rounded up to 2 decimals. */
  feeUsdt: string;
  /** Total Energy the call consumes, before working out who pays it. */
  energyRequired: number;
  /** Share of that Energy the SENDER pays (`consume_user_resource_percent`). */
  energyPayableByCaller: number;
  /** Percent the contract assigns to the caller; 0 = owner subsidises fully. */
  contractCallerPercent: number;
  /** Energy the platform can actually bring to this transfer. */
  energyAvailable: number;
  /**
   * Refund path only (null on payouts): the plan the broadcast will follow,
   * from the same <c>planDelegation</c> the delegation flow uses — so a refund
   * is charged for exactly the path it will take.
   */
  delegationPlan: DelegationPlan['kind'] | null;
  /** SUN the sweeper will delegate when <c>delegationPlan</c> is 'delegate'; null otherwise. */
  delegationSun: number | null;
  energyShortfall: number;
  bandwidthRequired: number;
  bandwidthAvailable: number;
  burnSun: number;
  trxPriceUsdt: number;
  priceSource: TrxPriceSource;
}

export interface FeeEstimationServiceDeps {
  resourceClient: TronResourceClient;
  priceService: TrxPriceService;
  tokenContracts: TokenContractMap;
  hotWalletAddress: string;
  tokenDecimals?: number;
}

export class FeeEstimationService {
  private readonly resources: TronResourceClient;
  private readonly price: TrxPriceService;
  private readonly tokens: TokenContractMap;
  private readonly hotWalletAddress: string;
  private readonly decimalsPower: bigint;

  constructor(deps: FeeEstimationServiceDeps) {
    this.resources = deps.resourceClient;
    this.price = deps.priceService;
    this.tokens = deps.tokenContracts;
    this.hotWalletAddress = deps.hotWalletAddress;
    this.decimalsPower = 10n ** BigInt(deps.tokenDecimals ?? 6);
  }

  async estimate(request: FeeEstimateRequest): Promise<FeeEstimateResult> {
    const contractAddress = this.tokens[request.token];
    if (!contractAddress) {
      throw new SidecarError(
        `Token contract for ${request.token} is not configured.`,
        'TOKEN_CONTRACT_NOT_CONFIGURED',
        false,
      );
    }
    const sender = request.fromAddress || this.hotWalletAddress;
    if (!sender) {
      throw new SidecarError(
        'No sender: fromAddress omitted and hot wallet address is not configured.',
        'HOT_WALLET_NOT_CONFIGURED',
        false,
      );
    }

    const amountUnits = TransferService.toRawUnits(request.amount, this.decimalsPower);

    const isDelegatedPath =
      Boolean(request.fromAddress) && request.fromAddress !== this.hotWalletAddress;

    const [
      energyRequired,
      hotWalletResources,
      senderResources,
      senderState,
      delegatableSun,
      feeParams,
      priceQuote,
      policy,
    ] = await Promise.all([
      this.resources.estimateTransferEnergy(
        contractAddress,
        sender,
        request.toAddress,
        amountUnits,
      ),
      this.resources.getAccountResources(this.hotWalletAddress || sender),
      // Bandwidth belongs to the sender itself; skip the duplicate fetch
      // when the sender IS the hot wallet.
      isDelegatedPath ? this.resources.getAccountResources(request.fromAddress!) : null,
      isDelegatedPath ? this.resources.getAccountState(request.fromAddress!) : null,
      isDelegatedPath ? this.resources.getDelegatableEnergySun(this.hotWalletAddress) : null,
      this.resources.getChainFeeParameters(),
      this.price.getPrice(request.correlationId),
      this.readContractPolicy(contractAddress, request.correlationId),
    ]);

    // (1) WHO PAYS. A contract can absorb its callers' execution cost
    //     (`consume_user_resource_percent = 0`). The Nile test USDT is
    //     deployed that way, which is the real reason every rehearsal
    //     transfer measured `fee: 0` — not delegation, which delivered
    //     nothing while the hot wallet held no stake. Charging the sender for
    //     the owner's share would bill a cost nobody incurs.
    const energyPayableByCaller = Math.ceil((energyRequired * policy.callerPercent) / 100);

    // (2) WHAT WE CAN BRING. On the payout path the hot wallet sends
    //     directly, so its whole pool applies and whatever it lacks burns. On
    //     the refund path the sender is a deposit address; the broadcast
    //     follows `planDelegation` — the stake covers the WHOLE shortfall or
    //     none of it (partial delegation leaves a deposit with no TRX short of
    //     Energy, and the transfer fails). The estimate asks the same function
    //     so the refund is charged for the path it will actually take.
    let energyAvailable: number;
    let energyShortfall: number;
    let delegationPlan: DelegationPlan['kind'] | null = null;
    let delegationSun: number | null = null;
    if (isDelegatedPath) {
      const plan = planDelegation({
        energyRequired,
        callerPercent: policy.callerPercent,
        senderEnergyAvailable: senderResources!.energyAvailable,
        energyPerTrx: hotWalletResources.energyPerTrx,
        delegatableSun: delegatableSun ?? 0,
      });
      delegationPlan = plan.kind;
      delegationSun = plan.kind === 'delegate' ? plan.delegationSun : null;
      energyShortfall = plan.kind === 'burn' ? plan.energyShortfall : 0;
      energyAvailable =
        plan.kind === 'delegate'
          ? plan.callerEnergy
          : Math.min(plan.callerEnergy, senderResources!.energyAvailable);
    } else {
      energyAvailable = hotWalletResources.energyAvailable;
      energyShortfall = Math.max(0, energyPayableByCaller - energyAvailable);
    }

    const bandwidthRequired = ESTIMATED_TRANSFER_TX_BYTES;
    // A deposit that has never received TRX is not an account yet and reports
    // no Bandwidth; the flow activates it before the transfer, after which it
    // has the free daily allowance (measured on Nile 2026-09-16).
    const bandwidthAvailable = !isDelegatedPath
      ? hotWalletResources.bandwidthAvailable
      : senderState!.exists
        ? senderResources!.bandwidthAvailable
        : ACTIVATED_ACCOUNT_FREE_BANDWIDTH;
    // Bandwidth is all-or-nothing on TRON: an account short of the full byte
    // count burns TRX for the WHOLE transaction, not just the missing bytes.
    const bandwidthBurnBytes = bandwidthAvailable >= bandwidthRequired ? 0 : bandwidthRequired;

    const burnSun =
      energyShortfall * feeParams.energyFeeSun + bandwidthBurnBytes * feeParams.bandwidthFeeSun;

    // Sun → TRX → USDT, rounded UP to the 2-decimal charge precision so the
    // charge never undershoots its own basis by a sub-cent artifact.
    const feeUsdt = (Math.ceil((burnSun / 1_000_000) * priceQuote.priceUsdt * 100) / 100).toFixed(
      2,
    );

    const result: FeeEstimateResult = {
      feeUsdt,
      energyRequired,
      energyPayableByCaller,
      contractCallerPercent: policy.callerPercent,
      energyAvailable,
      delegationPlan,
      delegationSun,
      energyShortfall,
      bandwidthRequired,
      bandwidthAvailable,
      burnSun,
      trxPriceUsdt: priceQuote.priceUsdt,
      priceSource: priceQuote.source,
    };

    logger.info(
      {
        ...result,
        from: sender,
        to: request.toAddress,
        token: request.token,
        correlationId: request.correlationId,
      },
      'Fee estimate computed',
    );
    return result;
  }

  /**
   * Contract energy policy, degrading to "the caller pays everything" when the
   * probe fails.
   *
   * That fallback is the conservative direction on the axis that matters: it
   * can only make the estimate LARGER than reality, never smaller, so a probe
   * outage cannot silently shift the platform's cost onto a user who was
   * charged too little. Mainnet Tether sets 100 anyway, so the fallback is
   * also the mainnet-correct value.
   */
  private async readContractPolicy(contractAddress: string, correlationId?: string) {
    try {
      return await this.resources.getContractEnergyPolicy(contractAddress);
    } catch (err) {
      logger.warn(
        { err: (err as Error).message, contractAddress, correlationId },
        'Contract energy policy unreadable — assuming the caller pays 100%',
      );
      return { callerPercent: 100, originEnergyLimit: 0 };
    }
  }
}
