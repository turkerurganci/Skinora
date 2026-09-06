import { logger } from '../logger.js';
import { SidecarError } from '../errors/SidecarError.js';
import { TronResourceClient } from '../tron/TronResourceClient.js';
import { TransferService, TokenContractMap, TokenSymbol } from '../transfer/TransferService.js';
import { TrxPriceService, TrxPriceSource } from './TrxPriceService.js';

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

/** Typical size of a signed TRC-20 `triggersmartcontract` transaction. */
export const ESTIMATED_TRANSFER_TX_BYTES = 350;

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
  /** Delegation ceiling applied on the refund path, null when not applicable. */
  delegationCapEnergy: number | null;
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
  /**
   * TRX (in SUN) the sweeper delegates to a deposit address for one outbound
   * transfer — `config.sweepEnergyDelegationSun`. The refund path can only
   * bring THIS much stake to the transfer, not the hot wallet's whole pool,
   * so it is the ceiling on the energy credit there.
   */
  delegationAmountSun?: number;
}

export class FeeEstimationService {
  private readonly resources: TronResourceClient;
  private readonly price: TrxPriceService;
  private readonly tokens: TokenContractMap;
  private readonly hotWalletAddress: string;
  private readonly decimalsPower: bigint;
  private readonly delegationAmountSun: number;

  constructor(deps: FeeEstimationServiceDeps) {
    this.resources = deps.resourceClient;
    this.price = deps.priceService;
    this.tokens = deps.tokenContracts;
    this.hotWalletAddress = deps.hotWalletAddress;
    this.decimalsPower = 10n ** BigInt(deps.tokenDecimals ?? 6);
    this.delegationAmountSun = deps.delegationAmountSun ?? 0;
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

    const [energyRequired, hotWalletResources, senderResources, feeParams, priceQuote, policy] =
      await Promise.all([
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
    //     directly, so its whole pool applies. On the refund path the sender
    //     is a deposit address that owns nothing; it receives a FIXED
    //     delegation (`sweepEnergyDelegationSun`), so the credit is capped by
    //     what that stake actually produces — not by the pool it came from.
    //     Without this cap a staked mainnet hot wallet makes the estimate say
    //     0.00 while the transfer burns nearly everything, and the platform
    //     silently eats the difference.
    const energyPerTrx = hotWalletResources.energyPerTrx;
    const delegationCapEnergy =
      isDelegatedPath && energyPerTrx !== null
        ? Math.floor((this.delegationAmountSun / 1_000_000) * energyPerTrx)
        : null;
    const energyAvailable =
      delegationCapEnergy === null
        ? hotWalletResources.energyAvailable
        : Math.min(hotWalletResources.energyAvailable, delegationCapEnergy);

    const energyShortfall = Math.max(0, energyPayableByCaller - energyAvailable);
    const bandwidthSource = senderResources ?? hotWalletResources;
    const bandwidthRequired = ESTIMATED_TRANSFER_TX_BYTES;
    const bandwidthAvailable = bandwidthSource.bandwidthAvailable;
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
      delegationCapEnergy,
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
