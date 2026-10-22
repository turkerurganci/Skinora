import { logger } from '../logger.js';
import { SidecarError } from '../errors/SidecarError.js';
import { sameAddress } from '../address/canonicalAddress.js';
import { TronResourceClient } from '../tron/TronResourceClient.js';
import { TransferService, TokenContractMap, TokenSymbol } from '../transfer/TransferService.js';
import { TrxPriceService, TrxPriceSource } from './TrxPriceService.js';
import {
  ACTIVATED_ACCOUNT_FREE_BANDWIDTH,
  ACTIVATION_TX_BYTES,
  ESTIMATED_TRANSFER_TX_BYTES,
  activationBurnSun,
  burnSun as transferBurnSun,
  callerEnergyShare,
  planDelegation,
  topUpSun,
  type DelegationPlan,
} from '../wallet/DelegationPlanner.js';
import type { DelegationSource } from '../wallet/EnergyDelegationService.js';

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
 *     directly and its whole pool applies (its own stake, or the payout share
 *     the stake account delegates to it permanently — DEPLOY_RUNBOOK §C.2);
 *     on a refund the deposit sends and the broadcast follows
 *     <c>planDelegation</c>: the stake covers the whole transfer, or nothing.
 *     What the stake can delegate is read from the account the broadcast
 *     delegates from, asked of the delegation flow itself.</item>
 *   <item>Bandwidth — the SENDER's own allowance (deposit addresses typically
 *     have none); the shortfall burns TRX at the chain's byte price.</item>
 *   <item>Activation — a refund from a deposit that has never received TRX
 *     first creates the account, and the chain burns the creation fee from the
 *     hot wallet that sends the 1 SUN (<c>activationBurnSun</c>: 1 TRX, plus
 *     0.1 TRX unless the hot wallet's STAKED Bandwidth covers the bytes). The
 *     buyer is charged it (owner decision 2026-10-02, 02 §4.6).</item>
 *   <item>Burned sun → USDT at the live TRX/USDT price, rounded UP to the
 *     2-decimal charge precision.</item>
 * </list>
 *
 * <para>
 * T169a (proje sahibi kararı 2026-10-21 — ⚠ PARA): a REFUND (deposit sender)
 * is charged the platform's whole outlay for it, not only what the chain
 * bills the deposit — exactly what <c>EnergyDelegationService</c> sends and
 * reports as <c>platformSpendSun</c> (T160c F1): the activation's 1 SUN and
 * its creation fee, the TRX top-up sized for the WHOLE transfer with its 10%
 * margin less what the deposit already holds (<c>topUpSun</c> — the part the
 * deposit does not burn stays there and is never collected), and the
 * top-up's own Bandwidth when the hot wallet has none left. The caller share
 * (point 2) no longer lowers a refund's charge: the TRX goes out for all of
 * the transfer whoever pays it on chain (Nile: the owner pays, the top-up
 * stays on the deposit). A payout keeps the chain-billed burn (payout rule
 * untouched — 02 §4.7).
 * </para>
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

/** The SUN the activation sends to create a deposit account (EnergyDelegationService.ensureAccount). */
const ACTIVATION_SUN = 1;

/**
 * T169a — bytes of the hot wallet's plain TRX send (the top-up). Same size as
 * the activation send it mirrors (`ACTIVATION_TX_BYTES`, 270 — the sweep line
 * prices its top-up the same way).
 */
const TRX_SEND_TX_BYTES = ACTIVATION_TX_BYTES;

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
  /** SUN the stake will delegate when <c>delegationPlan</c> is 'delegate'; null otherwise. */
  delegationSun: number | null;
  energyShortfall: number;
  bandwidthRequired: number;
  bandwidthAvailable: number;
  /**
   * SUN the hot wallet burns to activate a never-activated deposit before a
   * refund (refund path only; 0 otherwise). Included in <c>burnSun</c>.
   */
  activationBurnSun: number;
  /**
   * T169a — refund path: TRX the broadcast sends the deposit to burn for the
   * transfer (Energy for ALL of it unless delegated, plus its Bandwidth), with
   * the 10% margin, less what the deposit already holds (after activation: 1
   * SUN) — <c>topUpSun</c> of the delegation flow. 0 on a payout.
   */
  topUpSun: number;
  /**
   * T169a — refund path: the top-up transaction's own Bandwidth, burned when
   * the hot wallet's remaining Bandwidth cannot carry it (270 bytes ×
   * <c>getTransactionFee</c>); 0 when it can, when nothing is sent, and on a payout.
   */
  topUpFeeSun: number;
  /**
   * Everything the charge prices, SUN. Payout: the hot wallet's burn (Energy
   * shortfall + Bandwidth). Refund (T169a): the platform's whole outlay — 1 SUN
   * activation + <c>activationBurnSun</c> + <c>topUpSun</c> + <c>topUpFeeSun</c>,
   * the same total the broadcast reports as <c>platformSpendSun</c>.
   */
  burnSun: number;
  trxPriceUsdt: number;
  priceSource: TrxPriceSource;
}

/**
 * T149 — what sweeping ONE fresh deposit to the hot wallet costs the platform
 * in TRX, priced now; the backend turns it into the buyer's TRON network fee
 * line at acceptance (11 §F8 K8, 08 §3.1a). All amounts SUN.
 */
export interface SweepCostEstimateResult {
  /** activationSun + burnTopUpSun + bandwidthSun — the line's basis. */
  sweepCostSun: number;
  /** The hot wallet activating the never-funded deposit (1 TRX + 0.1 TRX unless staked Bandwidth covers it). */
  activationSun: number;
  /** TRX sent to the deposit to burn for the WHOLE transfer, with the flow's 10% margin; never recovered. */
  burnTopUpSun: number;
  /** The hot wallet's Bandwidth for that top-up transaction, assumed burned. */
  bandwidthSun: number;
  /** Energy of a transfer into an account that already holds the token (the hot wallet). */
  energyRequired: number;
  trxPriceUsdt: number;
  priceSource: TrxPriceSource;
  /** sweepCostSun × price, unrounded — informational; the backend applies its buffer and rounds once. */
  costUsdt: string;
}

export interface FeeEstimationServiceDeps {
  resourceClient: TronResourceClient;
  priceService: TrxPriceService;
  tokenContracts: TokenContractMap;
  hotWalletAddress: string;
  /**
   * The delegation flow — the one place that knows which account holds the
   * stake. Required, so the refund estimate cannot silently fall back to
   * reading the hot wallet after the stake has moved (#325 validation, B1).
   */
  delegationSource: DelegationSource;
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
  private readonly delegationSource: DelegationSource;
  private readonly decimalsPower: bigint;

  constructor(deps: FeeEstimationServiceDeps) {
    this.resources = deps.resourceClient;
    this.price = deps.priceService;
    this.tokens = deps.tokenContracts;
    this.hotWalletAddress = deps.hotWalletAddress;
    this.delegationSource = deps.delegationSource;
    this.decimalsPower = 10n ** BigInt(deps.tokenDecimals ?? 6);
  }

  /**
   * The account a refund estimate reads the delegatable stake from — the
   * delegation flow's own answer. Read by the startup line (index.ts), where
   * it must name the same account the flow delegates from.
   */
  get delegationOwner(): string {
    return this.delegationSource.delegationOwner;
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
      Boolean(request.fromAddress) && !sameAddress(request.fromAddress, this.hotWalletAddress);

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
      // The account that HOLDS the stake, exactly as the broadcast reads it —
      // the dedicated stake account once one is configured, not the hot wallet.
      // T149 — with the dormant stake switched off the broadcast reads none
      // and burns, so the estimate reads none either.
      isDelegatedPath && this.delegationSource.stakingEnabled
        ? this.resources.getDelegatableEnergySun(this.delegationSource.delegationOwner)
        : null,
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
    //     broadcast decides it. T169a: on the burn path the refund is charged
    //     the TRX the broadcast sends for the WHOLE transfer (step 4), not
    //     only the chain-billed share — the TRX sent for the owner's share
    //     stays in the deposit unburned and is never collected.
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
      // T169a — the burn path's top-up is sized for the WHOLE transfer (the
      // broadcast's own shortfall, owner share ignored), and that TRX is what
      // the refund is charged.
      energyShortfall = plan.kind === 'burn' ? plan.energyShortfall : 0;
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

    // (3) ACTIVATION. A deposit that has only received TRC-20 is not an
    //     account; the broadcast creates it with 1 SUN from the hot wallet
    //     (EnergyDelegationService.ensureAccount) and the chain burns the
    //     creation fee from the hot wallet. That burn exists only because this
    //     refund is being sent, so it is part of what the buyer pays.
    const activationSun = isDelegatedPath
      ? activationBurnSun({
          depositExists: senderState!.exists,
          senderStakedBandwidth: hotWalletResources.stakedBandwidthAvailable,
          createNewAccountFeeSun: feeParams.createNewAccountFeeSun,
          createAccountBandwidthFeeSun: feeParams.createAccountBandwidthFeeSun,
          createNewAccountBandwidthRate: feeParams.createNewAccountBandwidthRate,
        })
      : 0;
    if (activationSun === null) {
      throw new SidecarError(
        'getchainparameters response is missing the account-creation fees a never-activated deposit needs.',
        'FEE_ESTIMATE_CHAIN_PARAMS_FAILED',
        true,
      );
    }

    // (4) WHAT IS CHARGED. A payout: the hot wallet's own burn. A refund
    //     (T169a — proje sahibi kararı 2026-10-21, ⚠ PARA): the platform's
    //     whole outlay, mirroring EnergyDelegationService step by step — the
    //     1 SUN activation and its creation fee, the top-up it sends
    //     (`topUpSun` of the burn it plans, less the deposit's balance) and that
    //     top-up's own Bandwidth. TRX the deposit does not burn stays on it and
    //     is never collected, so it is spent all the same (T160c F1).
    let topUp = 0;
    let topUpFee = 0;
    let burnSun: number;
    if (isDelegatedPath) {
      const depositExists = senderState!.exists;
      const requiredBurn = transferBurnSun({
        energyToBurn: energyShortfall,
        energyFeeSun: feeParams.energyFeeSun,
        bandwidthAvailable,
        txBytes: bandwidthRequired,
        bandwidthFeeSun: feeParams.bandwidthFeeSun,
      });
      // After activation the deposit holds the 1 SUN that created it.
      const balanceSun = depositExists ? senderState!.balanceSun : ACTIVATION_SUN;
      topUp = topUpSun(requiredBurn, balanceSun);
      topUpFee =
        topUp > 0 && hotWalletResources.bandwidthAvailable < TRX_SEND_TX_BYTES
          ? TRX_SEND_TX_BYTES * feeParams.bandwidthFeeSun
          : 0;
      burnSun = (depositExists ? 0 : ACTIVATION_SUN) + activationSun + topUp + topUpFee;
    } else {
      burnSun =
        energyShortfall * feeParams.energyFeeSun +
        bandwidthBurnBytes * feeParams.bandwidthFeeSun +
        activationSun;
    }

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
      activationBurnSun: activationSun,
      topUpSun: topUp,
      topUpFeeSun: topUpFee,
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
   * T149 — the sweep the buyer's TRON network fee line pays for, priced at
   * acceptance, before the deposit exists (11 §F8 K8, 08 §3.1a). It mirrors
   * the burn path (the deposit flow, dormant stake OFF)
   * (<c>EnergyDelegationService</c>):
   *
   * <list type="number">
   *   <item><b>Activation</b> — a fresh deposit has only received TRC-20, so
   *     the hot wallet creates it with 1 SUN and the chain burns the creation
   *     fee from the hot wallet (<c>activationBurnSun</c>).</item>
   *   <item><b>Burn top-up</b> — the flow sizes the TRX it sends for the WHOLE
   *     transfer, owner share ignored (owner decision 2026-09-17), with its
   *     10% margin (<c>topUpSun</c>), less the 1 SUN activation left in the
   *     deposit. None of it comes back: what is not burned stays in the
   *     deposit, so all of it is the platform's cost.</item>
   *   <item><b>Bandwidth</b> — the top-up is a TRX transfer from the hot
   *     wallet; its bytes are priced as burned (the hot wallet's free daily
   *     allowance is shared by every transfer it sends and cannot be counted
   *     on eight days ahead). The deposit's own transfer runs on the free
   *     allowance the activation gives it.</item>
   * </list>
   *
   * The transfer's Energy is simulated as a hot wallet → hot wallet transfer
   * of 1 raw unit: a sweep sends into the hot wallet, which already holds the
   * token, and a self-transfer between holders measured exactly the same
   * Energy as one between two holders on mainnet (64,285, 2026-10-18 —
   * T149 report). A hot wallet holding no token reverts the simulation; the
   * caller then writes its fallback line.
   */
  async estimateSweepCost(
    token: TokenSymbol,
    correlationId?: string,
  ): Promise<SweepCostEstimateResult> {
    const contractAddress = this.tokens[token];
    if (!contractAddress) {
      throw new SidecarError(
        `Token contract for ${token} is not configured.`,
        'TOKEN_CONTRACT_NOT_CONFIGURED',
        false,
      );
    }
    if (!this.hotWalletAddress) {
      throw new SidecarError(
        'Hot wallet address is not configured — the sweep has no destination to price.',
        'HOT_WALLET_NOT_CONFIGURED',
        false,
      );
    }

    const [energyRequired, hotWalletResources, feeParams, priceQuote] = await Promise.all([
      this.resources.estimateTransferEnergy(
        contractAddress,
        this.hotWalletAddress,
        this.hotWalletAddress,
        '1',
      ),
      this.resources.getAccountResources(this.hotWalletAddress),
      this.resources.getChainFeeParameters(),
      this.price.getPrice(correlationId),
    ]);

    const activationSun = activationBurnSun({
      depositExists: false,
      senderStakedBandwidth: hotWalletResources.stakedBandwidthAvailable,
      createNewAccountFeeSun: feeParams.createNewAccountFeeSun,
      createAccountBandwidthFeeSun: feeParams.createAccountBandwidthFeeSun,
      createNewAccountBandwidthRate: feeParams.createNewAccountBandwidthRate,
    });
    if (activationSun === null) {
      throw new SidecarError(
        'getchainparameters response is missing the account-creation fees a fresh deposit needs.',
        'FEE_ESTIMATE_CHAIN_PARAMS_FAILED',
        true,
      );
    }

    // What the burn path sends: the whole transfer's Energy at the chain's
    // price (+ deposit Bandwidth, which the activated allowance covers), with
    // the 10% margin, less the 1 SUN the activation left in the deposit.
    const burnTopUpSun = topUpSun(
      transferBurnSun({
        energyToBurn: energyRequired,
        energyFeeSun: feeParams.energyFeeSun,
        bandwidthAvailable: ACTIVATED_ACCOUNT_FREE_BANDWIDTH,
        txBytes: ESTIMATED_TRANSFER_TX_BYTES,
        bandwidthFeeSun: feeParams.bandwidthFeeSun,
      }),
      1,
    );
    const bandwidthSun = ACTIVATION_TX_BYTES * feeParams.bandwidthFeeSun;
    const sweepCostSun = activationSun + burnTopUpSun + bandwidthSun;

    const result: SweepCostEstimateResult = {
      sweepCostSun,
      activationSun,
      burnTopUpSun,
      bandwidthSun,
      energyRequired,
      trxPriceUsdt: priceQuote.priceUsdt,
      priceSource: priceQuote.source,
      costUsdt: ((sweepCostSun / 1_000_000) * priceQuote.priceUsdt).toFixed(6),
    };
    logger.info({ ...result, token, correlationId }, 'Sweep fee estimate computed');
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
