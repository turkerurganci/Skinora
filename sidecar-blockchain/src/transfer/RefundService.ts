import { logger } from '../logger.js';
import { sameAddress } from '../address/canonicalAddress.js';
import { SidecarError } from '../errors/SidecarError.js';
import { WalletManager } from '../wallet/WalletManager.js';
import { TronTransferClient, SendTransferResult } from '../tron/TronTransferClient.js';
import { TokenContractMap, TokenSymbol, TransferService } from './TransferService.js';
import { TransferGuard } from './TransferGuard.js';
import { DepositRefundLimit } from './DepositRefundLimit.js';
import {
  EnergyDelegationService,
  DelegationMode,
  DelegationOutcome,
} from '../wallet/EnergyDelegationService.js';

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
  /** Resource path used (08 §3.3 audit field): <c>delegated</c> (stake covered
   * the Energy), <c>burn</c> (TRX sent for this transfer), <c>no-energy</c>
   * (the deposit already held the Energy) or <c>fallback</c> (plan unavailable,
   * fixed TRX). */
  delegationMode: DelegationMode;
  delegationAmountSun: number;
  fallbackAmountSun: number;
  /**
   * T160c düzeltme turu (F1) — SUN this call cost the hot wallet: the TRX it
   * sent the deposit (activation, burn / Bandwidth top-up, fallback) plus the
   * fee each of those sends burned. The deposit's own transfer burns out of
   * that TRX (its receipt fee is not on top); what it does not burn stays on
   * the deposit, never collected. The backend's accounting reads this.
   */
  platformSpendSun: number;
}

export interface RefundServiceDeps {
  walletManager: WalletManager;
  client: TronTransferClient;
  tokenContracts: TokenContractMap;
  tokenDecimals?: number;
  /** Amount limits (05 §3.3). Required for the same reason as on
   * <c>TransferService</c>: no signer without the ability to refuse. */
  guard: TransferGuard;
  /** T172b — the signer's 24-hour deposit refund cap. Required for the same
   * reason as the guard: every refund leaves a deposit, and the signer must be
   * able to refuse what a compromised backend asks for. */
  depositRefundLimit: DepositRefundLimit;
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
  private readonly guard: TransferGuard;
  private readonly depositRefundLimit: DepositRefundLimit;

  constructor(deps: RefundServiceDeps) {
    this.wallet = deps.walletManager;
    this.client = deps.client;
    this.tokens = deps.tokenContracts;
    this.decimalsPower = 10n ** BigInt(deps.tokenDecimals ?? 6);
    this.energyDelegation = deps.energyDelegation;
    this.guard = deps.guard;
    this.depositRefundLimit = deps.depositRefundLimit;
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
    if (!sameAddress(signer.address, request.depositAddress)) {
      throw new SidecarError(
        `Derived address ${signer.address} does not match expected deposit address ${request.depositAddress}.`,
        'DEPOSIT_ADDRESS_MISMATCH',
        false,
      );
    }
    const contract = this.resolveContract(request.token);
    const amountUnits = TransferService.toRawUnits(request.amount, this.decimalsPower);

    // Buyer addresses cannot be pinned either. A refund is bounded by its own
    // deposit, so only the single-transfer ceiling applies here — the daily
    // ceiling measures hot-wallet outflow, and this leaves a deposit address.
    this.guard.assertSingleTransferLimit(BigInt(amountUnits));

    // T172b — and the deposit refund cap: counted (durably) BEFORE any TRX is
    // spent on the deposit or anything is signed; over the cap the refund
    // waits (TransferDeferredError → 503 deferred), nothing spent.
    const reservation = this.depositRefundLimit.reserve(
      request.blockchainTransactionId,
      BigInt(amountUnits),
      request.toBuyerAddress,
    );
    // Whether the token transfer may have reached the network. Until the
    // action starts, and when the build itself failed, it certainly did not.
    let broadcastAttempted = false;
    let broadcastOk = false;

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

    let outcome: DelegationOutcome<SendTransferResult>;
    try {
      outcome = await this.energyDelegation.withDelegation(
        {
          depositAddress: request.depositAddress,
          contractAddress: contract,
          toAddress: request.toBuyerAddress,
          amountUnits,
        },
        async () => {
          broadcastAttempted = true;
          const sent = await this.client.sendTransfer({
            fromAddress: signer.address,
            privateKey: signer.privateKey,
            contractAddress: contract,
            toAddress: request.toBuyerAddress,
            amountUnits,
          });
          broadcastOk = true;
          return sent;
        },
        {
          blockchainTransactionId: request.blockchainTransactionId,
          correlationId: request.correlationId,
        },
      );
    } catch (err) {
      if (!broadcastOk && (!broadcastAttempted || RefundService.neverBroadcast(err))) {
        this.depositRefundLimit.release(reservation);
      }
      throw err;
    }
    this.depositRefundLimit.settle(reservation, outcome.action.txHash);

    return {
      txHash: outcome.action.txHash,
      delegationMode: outcome.mode,
      delegationAmountSun: outcome.delegationAmountSun,
      fallbackAmountSun: outcome.fallbackAmountSun,
      platformSpendSun: outcome.platformSpendSun,
    };
  }

  /**
   * T172b — failures that prove the token transfer never left this process:
   * nothing was signed (no key) or the node would not even build it. A
   * rejected or failed broadcast may still have reached a node, so it keeps
   * counting (fail closed).
   */
  private static neverBroadcast(err: unknown): boolean {
    return (
      err instanceof SidecarError &&
      (err.code === 'TRANSFER_BUILD_FAILED' || err.code === 'TRANSFER_NO_PRIVATE_KEY')
    );
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
