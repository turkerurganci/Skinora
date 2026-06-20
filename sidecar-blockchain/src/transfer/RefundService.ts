import { logger } from '../logger.js';
import { SidecarError } from '../errors/SidecarError.js';
import { WalletManager } from '../wallet/WalletManager.js';
import { TronTransferClient, SendTransferResult } from '../tron/TronTransferClient.js';
import { TokenContractMap, TokenSymbol, TransferService } from './TransferService.js';
import { EnergyDelegationService, DelegationOutcome } from '../wallet/EnergyDelegationService.js';

export interface RefundRequest {
  blockchainTransactionId: string;
  /** Derivation index from `PaymentAddress.HdWalletIndex` — refund source. */
  depositIndex: number;
  depositAddress: string;
  /** Buyer-controlled address (`BlockchainTransaction.ToAddress` row). */
  toBuyerAddress: string;
  /** Already net of gas fee — backend's RefundDecisionService computes it. */
  amount: string;
  token: TokenSymbol;
  correlationId: string;
}

export interface RefundResult extends SendTransferResult {
  /** Delegation path used: <c>delegated</c> (delegateresource) or
   * <c>fallback</c> (TRX prefund). 08 §3.3 audit field. */
  delegationMode: 'delegated' | 'fallback';
  delegationAmountSun: number;
  fallbackAmountSun: number;
}

export interface RefundServiceDeps {
  walletManager: WalletManager;
  client: TronTransferClient;
  tokenContracts: TokenContractMap;
  tokenDecimals?: number;
  /** Energy delegation orchestrator (T74). Refund originates from a deposit
   * address with no TRX, so delegation is mandatory in production. Tests can
   * omit this only when explicitly exercising the <c>DELEGATION_NOT_WIRED</c>
   * error path. */
  energyDelegation?: EnergyDelegationService;
}

/**
 * Outbound refund flow — deposit address → buyer's source address (02 §4.6,
 * 08 §3.3). Gas fee deduction is owned by the backend
 * (<c>RefundDecisionService</c> in T53/T72); the sidecar only broadcasts the
 * pre-computed net amount.
 *
 * <para>
 * Refund types covered (06 §3.8): BUYER_REFUND, EXCESS_REFUND,
 * WRONG_TOKEN_REFUND, INCORRECT_AMOUNT_REFUND, LATE_PAYMENT_REFUND. All five
 * share the same primitive — only the row type differs upstream.
 * </para>
 */
export class RefundService {
  private readonly wallet: WalletManager;
  private readonly client: TronTransferClient;
  private readonly tokens: TokenContractMap;
  private readonly decimalsPower: bigint;
  private readonly energyDelegation?: EnergyDelegationService;

  constructor(deps: RefundServiceDeps) {
    this.wallet = deps.walletManager;
    this.client = deps.client;
    this.tokens = deps.tokenContracts;
    this.decimalsPower = 10n ** BigInt(deps.tokenDecimals ?? 6);
    this.energyDelegation = deps.energyDelegation;
  }

  async refund(request: RefundRequest): Promise<RefundResult> {
    if (!this.energyDelegation) {
      throw new SidecarError(
        'Energy delegation service is not wired — refund from deposit requires delegateresource/undelegateresource (08 §3.3).',
        'DELEGATION_NOT_WIRED',
        false,
      );
    }
    const signer = this.wallet.deriveSigner(request.depositIndex);
    if (signer.address !== request.depositAddress) {
      throw new SidecarError(
        `Derived address ${signer.address} does not match expected deposit address ${request.depositAddress}.`,
        'DEPOSIT_ADDRESS_MISMATCH',
        false,
      );
    }
    const contract = this.resolveContract(request.token);
    const amountUnits = TransferService.toRawUnits(request.amount, this.decimalsPower);

    logger.info(
      {
        blockchainTransactionId: request.blockchainTransactionId,
        correlationId: request.correlationId,
        depositAddress: request.depositAddress,
        toBuyerAddress: request.toBuyerAddress,
        token: request.token,
        amount: request.amount,
      },
      'Broadcasting BUYER_REFUND-family transfer (deposit -> buyer) with Energy delegation',
    );

    const outcome: DelegationOutcome<SendTransferResult> =
      await this.energyDelegation.withDelegation(
        request.depositAddress,
        () =>
          this.client.sendTransfer({
            fromAddress: signer.address,
            privateKey: signer.privateKey,
            contractAddress: contract,
            toAddress: request.toBuyerAddress,
            amountUnits,
          }),
        {
          blockchainTransactionId: request.blockchainTransactionId,
          correlationId: request.correlationId,
        },
      );

    return {
      txHash: outcome.action.txHash,
      delegationMode: outcome.mode,
      delegationAmountSun: outcome.delegationAmountSun,
      fallbackAmountSun: outcome.fallbackAmountSun,
    };
  }

  private resolveContract(token: TokenSymbol): string {
    const address = this.tokens[token];
    if (!address) {
      throw new SidecarError(
        `Token contract address for ${token} is not configured.`,
        'TOKEN_CONTRACT_NOT_CONFIGURED',
        false,
      );
    }
    return address;
  }
}
