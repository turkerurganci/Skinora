import { logger } from '../logger.js';
import { SidecarError } from '../errors/SidecarError.js';
import { WalletManager } from '../wallet/WalletManager.js';
import {
  TronTransferClient,
  SendTransferResult,
  TransactionStatusResult,
} from '../tron/TronTransferClient.js';

export type TokenSymbol = 'USDT' | 'USDC';

export interface PayoutRequest {
  /** Logical id from the .NET backend `BlockchainTransaction.Id`. */
  blockchainTransactionId: string;
  toAddress: string;
  /** Decimal string with up to 6 fraction digits — e.g. "100.5". */
  amount: string;
  token: TokenSymbol;
  correlationId: string;
}

export interface SweepRequest {
  blockchainTransactionId: string;
  /** Derivation index from `PaymentAddress.HdWalletIndex`. */
  depositIndex: number;
  /** Expected deposit address (sanity check against derive result). */
  depositAddress: string;
  toHotWalletAddress: string;
  amount: string;
  token: TokenSymbol;
  correlationId: string;
}

export interface TokenContractMap {
  USDT: string;
  USDC: string;
}

export interface TransferServiceDeps {
  walletManager: WalletManager;
  client: TronTransferClient;
  tokenContracts: TokenContractMap;
  hotWalletAddress: string;
  hotWalletPrivateKey: string;
  /** Token decimals — 6 for USDT and USDC per 08 §3.3 (mainnet + testnet). */
  tokenDecimals?: number;
}

/**
 * Outbound transfer primitives owned by the blockchain sidecar (08 §3.1, §3.3).
 *
 * <para>
 * Two flows live here:
 * <list type="bullet">
 *   <item><c>payout</c> — hot wallet → seller (SELLER_PAYOUT row).
 *     Signing key: <c>HOT_WALLET_PRIVATE_KEY</c> (Docker secret).</item>
 *   <item><c>sweep</c> — deposit address → hot wallet
 *     (operational consolidation, 05 §3.3 sweep mechanism).
 *     Signing key: derived from <c>HD_WALLET_MNEMONIC</c> at index N,
 *     produced fresh per call and dropped immediately after broadcast.</item>
 * </list>
 * </para>
 *
 * <para>
 * Retry / dispatcher cadence is owned by the .NET backend's
 * <c>OutgoingTransferDispatchJob</c> (08 §3.3 — "retry 3 deneme: 1dk, 5dk,
 * 15dk"). This class only performs single broadcasts and bubbles transient
 * vs. permanent errors via <see cref="SidecarError.retryable"/>.
 * </para>
 */
export class TransferService {
  private readonly wallet: WalletManager;
  private readonly client: TronTransferClient;
  private readonly tokens: TokenContractMap;
  private readonly hotWalletAddress: string;
  private readonly hotWalletPrivateKey: string;
  private readonly decimalsPower: bigint;

  constructor(deps: TransferServiceDeps) {
    this.wallet = deps.walletManager;
    this.client = deps.client;
    this.tokens = deps.tokenContracts;
    this.hotWalletAddress = deps.hotWalletAddress;
    this.hotWalletPrivateKey = deps.hotWalletPrivateKey;
    this.decimalsPower = 10n ** BigInt(deps.tokenDecimals ?? 6);
  }

  async payout(request: PayoutRequest): Promise<SendTransferResult> {
    if (!this.hotWalletAddress || !this.hotWalletPrivateKey) {
      throw new SidecarError(
        'Hot wallet credentials are not configured (HOT_WALLET_ADDRESS + HOT_WALLET_PRIVATE_KEY).',
        'HOT_WALLET_NOT_CONFIGURED',
        false,
      );
    }
    const contract = this.resolveContract(request.token);
    const amountUnits = TransferService.toRawUnits(request.amount, this.decimalsPower);

    logger.info(
      {
        blockchainTransactionId: request.blockchainTransactionId,
        correlationId: request.correlationId,
        token: request.token,
        amount: request.amount,
        toAddress: request.toAddress,
      },
      'Broadcasting SELLER_PAYOUT',
    );

    return this.client.sendTransfer({
      fromAddress: this.hotWalletAddress,
      privateKey: this.hotWalletPrivateKey,
      contractAddress: contract,
      toAddress: request.toAddress,
      amountUnits,
    });
  }

  async sweep(request: SweepRequest): Promise<SendTransferResult> {
    if (!this.hotWalletAddress) {
      throw new SidecarError(
        'Hot wallet address is not configured.',
        'HOT_WALLET_NOT_CONFIGURED',
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
        toHotWalletAddress: request.toHotWalletAddress,
        token: request.token,
        amount: request.amount,
      },
      'Broadcasting SWEEP (deposit -> hot wallet)',
    );

    return this.client.sendTransfer({
      fromAddress: signer.address,
      privateKey: signer.privateKey,
      contractAddress: contract,
      toAddress: request.toHotWalletAddress,
      amountUnits,
    });
  }

  async getStatus(txHash: string): Promise<TransactionStatusResult> {
    return this.client.getTransactionStatus(txHash);
  }

  resolveContract(token: TokenSymbol): string {
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

  /**
   * Convert a decimal amount string (e.g. "100.5") to raw uint256 units the
   * smart contract expects ("100500000" for 6 decimals). Avoids
   * <c>parseFloat</c> entirely — bigint arithmetic keeps precision intact
   * even at very large amounts (09 §14.3 financial math invariants).
   */
  static toRawUnits(amount: string, decimalsPower: bigint): string {
    if (!/^\d+(?:\.\d+)?$/.test(amount)) {
      throw new SidecarError(
        `Invalid amount string "${amount}" — expected positive decimal.`,
        'INVALID_TRANSFER_AMOUNT',
        false,
      );
    }
    const [whole, fraction = ''] = amount.split('.');
    const decimals = decimalsPower.toString().length - 1;
    if (fraction.length > decimals) {
      throw new SidecarError(
        `Amount "${amount}" exceeds ${decimals} fractional digits.`,
        'INVALID_TRANSFER_AMOUNT',
        false,
      );
    }
    const padded = fraction.padEnd(decimals, '0');
    const wholeBig = BigInt(whole) * decimalsPower;
    const fractionBig = BigInt(padded || '0');
    return (wholeBig + fractionBig).toString();
  }
}
