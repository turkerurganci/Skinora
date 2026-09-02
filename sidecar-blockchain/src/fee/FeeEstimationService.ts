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
  energyRequired: number;
  energyAvailable: number;
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

    const [energyRequired, hotWalletResources, senderResources, feeParams, priceQuote] =
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
        request.fromAddress && request.fromAddress !== this.hotWalletAddress
          ? this.resources.getAccountResources(request.fromAddress)
          : null,
        this.resources.getChainFeeParameters(),
        this.price.getPrice(request.correlationId),
      ]);

    const bandwidthSource = senderResources ?? hotWalletResources;
    const energyAvailable = hotWalletResources.energyAvailable;
    const energyShortfall = Math.max(0, energyRequired - energyAvailable);
    const bandwidthRequired = ESTIMATED_TRANSFER_TX_BYTES;
    const bandwidthAvailable = bandwidthSource.bandwidthAvailable;
    const bandwidthShortfall = Math.max(0, bandwidthRequired - bandwidthAvailable);

    const burnSun =
      energyShortfall * feeParams.energyFeeSun + bandwidthShortfall * feeParams.bandwidthFeeSun;

    // Sun → TRX → USDT, rounded UP to the 2-decimal charge precision so the
    // charge never undershoots its own basis by a sub-cent artifact.
    const feeUsdt = (Math.ceil((burnSun / 1_000_000) * priceQuote.priceUsdt * 100) / 100).toFixed(
      2,
    );

    const result: FeeEstimateResult = {
      feeUsdt,
      energyRequired,
      energyAvailable,
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
}
