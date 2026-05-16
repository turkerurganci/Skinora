import { logger } from '../logger.js';
import { SidecarError } from '../errors/SidecarError.js';
import { WalletManager } from '../wallet/WalletManager.js';
import { TronTransferClient, SendTransferResult } from '../tron/TronTransferClient.js';
import { TokenContractMap, TokenSymbol, TransferService } from './TransferService.js';

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

export interface RefundServiceDeps {
  walletManager: WalletManager;
  client: TronTransferClient;
  tokenContracts: TokenContractMap;
  tokenDecimals?: number;
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

  constructor(deps: RefundServiceDeps) {
    this.wallet = deps.walletManager;
    this.client = deps.client;
    this.tokens = deps.tokenContracts;
    this.decimalsPower = 10n ** BigInt(deps.tokenDecimals ?? 6);
  }

  async refund(request: RefundRequest): Promise<SendTransferResult> {
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
      'Broadcasting BUYER_REFUND-family transfer (deposit -> buyer)',
    );

    return this.client.sendTransfer({
      fromAddress: signer.address,
      privateKey: signer.privateKey,
      contractAddress: contract,
      toAddress: request.toBuyerAddress,
      amountUnits,
    });
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
