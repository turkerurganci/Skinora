import { logger } from '../logger.js';
import { SidecarError } from '../errors/SidecarError.js';
import { WalletManager } from '../wallet/WalletManager.js';
import {
  TronTransferClient,
  SendTransferResult,
  TransactionStatusResult,
} from '../tron/TronTransferClient.js';
import {
  EnergyDelegationService,
  DelegationMode,
  DelegationOutcome,
} from '../wallet/EnergyDelegationService.js';
import { TransferGuard } from './TransferGuard.js';

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

export interface ColdWalletTransferRequest {
  /** Logical id from the .NET backend's <c>ColdWalletTransfer</c> pre-allocation
   * — used in logs and correlation only; nothing is persisted on the sidecar
   * (the backend owns the ledger row, T77 — 05 §3.3, 06 §3.22). */
  coldTransferId: string;
  /** Destination cold wallet address the backend recorded on its ledger row.
   * It is checked against this sidecar's own <c>COLD_WALLET_ADDRESS</c> and
   * a mismatch is refused — the caller cannot choose where consolidation
   * lands (owner decision 2026-09-16). */
  toColdAddress: string;
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
  /** Destination pinning + amount limits (05 §3.3). Required: the signer must
   * never be constructible without the guards that let it refuse. */
  guard: TransferGuard;
  /** Energy delegation orchestrator (T74). Optional so unit tests that focus
   * on payout (hot-wallet-sourced, no delegation needed) can omit it. Sweep
   * requires it — calling <c>sweep()</c> without one throws
   * <c>DELEGATION_NOT_WIRED</c>. */
  energyDelegation?: EnergyDelegationService;
}

export interface SweepResult extends SendTransferResult {
  /** Resource path used (08 §3.3 audit field): <c>delegated</c> (stake covered
   * the Energy), <c>burn</c> (TRX sent for this transfer), <c>no-energy</c>
   * (the deposit already held the Energy) or <c>fallback</c> (plan unavailable,
   * fixed TRX). */
  delegationMode: DelegationMode;
  delegationAmountSun: number;
  fallbackAmountSun: number;
}

/**
 * Outbound transfer primitives owned by the blockchain sidecar (08 §3.1, §3.3).
 *
 * <para>
 * Two flows live here:
 * <list type="bullet">
 *   <item><c>payout</c> — hot wallet → seller (SELLER_PAYOUT row).
 *     Signing key: <c>HOT_WALLET_PRIVATE_KEY</c> (Docker secret).</item>
 *   <item><c>coldWalletTransfer</c> — hot wallet → cold wallet (admin-driven
 *     operational consolidation, T77 — 05 §3.3 hot wallet limit alert flow).
 *     Same signing key as payout; mechanically a TRC-20 transfer from the
 *     hot wallet to an admin-supplied cold address. Distinct from
 *     <c>payout</c> only at the logging / metrics layer so the admin
 *     dashboards can separate operational ledger movement from
 *     customer-facing payouts.</item>
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
  private readonly energyDelegation?: EnergyDelegationService;
  private readonly guard: TransferGuard;

  constructor(deps: TransferServiceDeps) {
    this.wallet = deps.walletManager;
    this.client = deps.client;
    this.tokens = deps.tokenContracts;
    this.hotWalletAddress = deps.hotWalletAddress;
    this.hotWalletPrivateKey = deps.hotWalletPrivateKey;
    this.decimalsPower = 10n ** BigInt(deps.tokenDecimals ?? 6);
    this.energyDelegation = deps.energyDelegation;
    this.guard = deps.guard;
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

    // The seller's address cannot be pinned — it is the seller's own. Both
    // limits therefore guard this path: the single-transfer ceiling catches a
    // miscomputed amount, the 24-hour ceiling caps what a compromised caller
    // can drain in many small steps (05 §3.3 "Transfer limitleri").
    this.guard.assertSingleTransferLimit(BigInt(amountUnits));
    await this.guard.assertHotWalletDailyLimit(BigInt(amountUnits));

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

    const result = await this.client.sendTransfer({
      fromAddress: this.hotWalletAddress,
      privateKey: this.hotWalletPrivateKey,
      contractAddress: contract,
      toAddress: request.toAddress,
      amountUnits,
    });
    // Chain history lags a broadcast by a block or two; record it so a burst
    // inside one window cannot each read a stale total and all pass.
    this.guard.recordHotWalletOutflow(result.txHash, BigInt(amountUnits));
    return result;
  }

  async coldWalletTransfer(request: ColdWalletTransferRequest): Promise<SendTransferResult> {
    if (!this.hotWalletAddress || !this.hotWalletPrivateKey) {
      throw new SidecarError(
        'Hot wallet credentials are not configured (HOT_WALLET_ADDRESS + HOT_WALLET_PRIVATE_KEY).',
        'HOT_WALLET_NOT_CONFIGURED',
        false,
      );
    }
    // Pinned destination: consolidation may only credit this sidecar's own
    // cold wallet, so neither an admin-editable setting nor a tampered backend
    // can point it elsewhere. Exempt from the amount limits for the same
    // reason — the money cannot leave the platform this way.
    this.guard.assertColdDestination(request.toColdAddress);
    const contract = this.resolveContract(request.token);
    const amountUnits = TransferService.toRawUnits(request.amount, this.decimalsPower);

    logger.info(
      {
        coldTransferId: request.coldTransferId,
        correlationId: request.correlationId,
        token: request.token,
        amount: request.amount,
        toColdAddress: request.toColdAddress,
      },
      'Broadcasting COLD_WALLET_TRANSFER (hot -> cold)',
    );

    return this.client.sendTransfer({
      fromAddress: this.hotWalletAddress,
      privateKey: this.hotWalletPrivateKey,
      contractAddress: contract,
      toAddress: request.toColdAddress,
      amountUnits,
    });
  }

  async sweep(request: SweepRequest): Promise<SweepResult> {
    if (!this.hotWalletAddress) {
      throw new SidecarError(
        'Hot wallet address is not configured.',
        'HOT_WALLET_NOT_CONFIGURED',
        false,
      );
    }
    if (!this.energyDelegation) {
      throw new SidecarError(
        'Energy delegation service is not wired — sweep requires delegateresource/undelegateresource (08 §3.3).',
        'DELEGATION_NOT_WIRED',
        false,
      );
    }
    // Pinned destination: a sweep may only credit this sidecar's own hot
    // wallet (05 §3.3). The request still carries the address the backend
    // recorded on the ledger row, so drift fails here instead of redirecting.
    this.guard.assertSweepDestination(request.toHotWalletAddress);
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
      'Broadcasting SWEEP (deposit -> hot wallet) with Energy delegation',
    );

    const outcome: DelegationOutcome<SendTransferResult> =
      await this.energyDelegation.withDelegation(
        {
          depositAddress: request.depositAddress,
          contractAddress: contract,
          toAddress: request.toHotWalletAddress,
          amountUnits,
        },
        () =>
          this.client.sendTransfer({
            fromAddress: signer.address,
            privateKey: signer.privateKey,
            contractAddress: contract,
            toAddress: request.toHotWalletAddress,
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
