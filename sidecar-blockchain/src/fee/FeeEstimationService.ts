import { logger } from '../logger.js';
import { SidecarError } from '../errors/SidecarError.js';
import { TronResourceClient } from '../tron/TronResourceClient.js';
import { TransferService, TokenContractMap, TokenSymbol } from '../transfer/TransferService.js';
import { TrxPriceService, TrxPriceSource } from './TrxPriceService.js';
import {
  ACTIVATED_ACCOUNT_FREE_BANDWIDTH,
  ESTIMATED_TRANSFER_TX_BYTES,
  callerEnergyShare,
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
 *   <item>Energy the chain bills the SENDER — the contract owner pays its share
 *     out of its own remaining Energy, so the split is read from the owner's
 *     account, not assumed from the contract's percent
 *     (<c>callerEnergyShare</c>).</item>
 *   <item>Energy the platform brings — on a payout the hot wallet sends
 *     directly and its whole pool applies; on a refund the deposit sends and
 *     the broadcast follows <c>planDelegation</c>: the stake covers the whole
 *     transfer, or nothing.</item>
 *   <item>Bandwidth — the SENDER's own allowance (deposit addresses typically
 *     have none); the shortfall burns TRX at the chain's byte price.</item>
 *   <item>Burned sun → USDT at the live TRX/USDT price, rounded UP to the
 *     2-decimal charge precision.</item>
 * </list>
 *
 * The estimate can still drift from the realized fee (recipient balance, the
 * owner's remaining Energy or chain prices may change between estimate and
 * broadcast); that residual variance stays on the platform by design — the
 * user is charged the value computed here, snapshotted onto
 * <c>BlockchainTransaction.GasFee</c>.
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
  /**
   * Energy the chain bills the SENDER: the whole call minus what the contract
   * owner pays out of its remaining Energy (<c>callerEnergyShare</c>).
   */
  energyPayableByCaller: number;
  /** The contract's nominal caller percent; 0 = its owner pays whatever its Energy covers. */
  contractCallerPercent: number;
  /** The contract owner's remaining Energy when estimated; 0 when unreadable. */
  contractOwnerEnergyAvailable: number;
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

interface ContractShare {
  callerPercent: number;
  originEnergyLimit: number;
  ownerEnergyAvailable: number;
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
      contractShare,
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
      this.readContractShare(contractAddress, request.correlationId),
    ]);

    // (1) WHO PAYS. A contract can have its owner absorb its callers' execution
    //     cost — but only out of the owner's REMAINING Energy. The Nile test
    //     USDT's owner has plenty, which is why rehearsal transfers measure
    //     `fee: 0`; mainnet Tether's owner has none, so although the contract
    //     says 30% its callers pay 100% (542/542 calls, 2026-09-17). Charging
    //     the nominal percent billed mainnet users 30% of the real cost;
    //     charging 100% would bill a cost nobody incurs wherever an owner pays.
    const energyPayableByCaller = callerEnergyShare({
      energyRequired,
      callerPercent: contractShare.callerPercent,
      originEnergyLimit: contractShare.originEnergyLimit,
      ownerEnergyAvailable: contractShare.ownerEnergyAvailable,
    });

    // (2) WHAT WE CAN BRING. On the payout path the hot wallet sends
    //     directly, so its whole pool applies and whatever it lacks burns. On
    //     the refund path the sender is a deposit address and the broadcast
    //     follows `planDelegation`, which plans the WHOLE transfer: the stake
    //     covers all of it or none of it. The path is decided exactly as the
    //     broadcast decides it; what the user pays on the burn path is only
    //     what the chain bills the deposit (TRX sent for the owner's share
    //     stays in the deposit unburned).
    let energyAvailable: number;
    let energyShortfall: number;
    let delegationPlan: DelegationPlan['kind'] | null = null;
    let delegationSun: number | null = null;
    if (isDelegatedPath) {
      const depositEnergy = senderResources!.energyAvailable;
      const plan = planDelegation({
        energyRequired,
        senderEnergyAvailable: depositEnergy,
        energyPerTrx: hotWalletResources.energyPerTrx,
        delegatableSun: delegatableSun ?? 0,
      });
      delegationPlan = plan.kind;
      delegationSun = plan.kind === 'delegate' ? plan.delegationSun : null;
      energyAvailable =
        plan.kind === 'delegate' ? energyRequired : Math.min(energyRequired, depositEnergy);
      energyShortfall =
        plan.kind === 'burn' ? Math.max(0, energyPayableByCaller - depositEnergy) : 0;
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
      contractCallerPercent: contractShare.callerPercent,
      contractOwnerEnergyAvailable: contractShare.ownerEnergyAvailable,
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
   * The contract's split and its owner's remaining Energy, degrading to "the
   * owner pays nothing" (the caller pays everything) when a probe fails.
   *
   * That fallback is the conservative direction on the axis that matters: it
   * can only make the estimate LARGER than reality, never smaller, so a probe
   * outage cannot silently shift the platform's cost onto a user who was
   * charged too little. On mainnet USDT it is also today's real split.
   */
  private async readContractShare(
    contractAddress: string,
    correlationId?: string,
  ): Promise<ContractShare> {
    let policy;
    try {
      policy = await this.resources.getContractEnergyPolicy(contractAddress);
    } catch (err) {
      logger.warn(
        { err: (err as Error).message, contractAddress, correlationId },
        'Contract energy policy unreadable — assuming the caller pays 100%',
      );
      return { callerPercent: 100, originEnergyLimit: 0, ownerEnergyAvailable: 0 };
    }

    const share = {
      callerPercent: policy.callerPercent,
      originEnergyLimit: policy.originEnergyLimit,
    };
    if (!policy.originAddress) {
      logger.warn(
        { contractAddress, correlationId },
        'Contract owner not reported — assuming the owner pays nothing',
      );
      return { ...share, ownerEnergyAvailable: 0 };
    }
    try {
      const owner = await this.resources.getAccountResources(policy.originAddress);
      return { ...share, ownerEnergyAvailable: owner.energyAvailable };
    } catch (err) {
      logger.warn(
        {
          err: (err as Error).message,
          contractAddress,
          originAddress: policy.originAddress,
          correlationId,
        },
        'Contract owner resources unreadable — assuming the owner pays nothing',
      );
      return { ...share, ownerEnergyAvailable: 0 };
    }
  }
}
