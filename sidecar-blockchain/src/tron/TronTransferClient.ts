import TronWeb from 'tronweb';
import { config } from '../config/index.js';
import { logger } from '../logger.js';
import { SidecarError } from '../errors/SidecarError.js';
import { transfersTotal } from '../metrics.js';

/**
 * Resource budget for an outbound TRC-20 transfer (08 §3.3). TronWeb's
 * <c>triggerSmartContract</c> consumes both fields verbatim; the defaults
 * carry the headroom established by 08 §3.3 (≈65k Energy, 15 TRX fallback).
 */
export interface TransferOptions {
  /** Max fee burned (sun = TRX * 1e6) if the chain charges Bandwidth/Energy. */
  feeLimitSun?: number;
  /** Optional integer call-value (default 0 — TRC-20 transfers carry no TRX). */
  callValue?: number;
}

export interface SendTransferRequest {
  fromAddress: string;
  privateKey: string;
  contractAddress: string;
  toAddress: string;
  /** Raw token units (decimals already applied — e.g. 100.5 USDT → "100500000"). */
  amountUnits: string;
  options?: TransferOptions;
}

export interface SendTransferResult {
  txHash: string;
}

export interface TransactionStatusResult {
  txHash: string;
  /** Solid (irreversible) block height the tx landed in; absent until finality. */
  blockNumber?: number;
  /** TronGrid's contract execution result — typically "SUCCESS" or "REVERT". */
  contractRet?: string;
  /** Number of solid blocks since inclusion; -1 while the tx is still pending. */
  confirmations: number;
}

/**
 * Thin TronWeb adapter for outbound TRC-20 transfers (08 §3.1 + §3.3).
 *
 * <para>
 * Each call instantiates a fresh <see cref="TronWeb"/> binding so the private
 * key is scoped to the broadcast and discarded with the local variable —
 * caller passes the signing material once and the TronWeb wrapper goes out
 * of scope before the next call (05 §3.3 signing isolation). The instance
 * is created with a placeholder mnemonic; the real signer is the
 * <c>privateKey</c> supplied per request.
 * </para>
 *
 * <para>
 * Transaction status is delegated to <see cref="TronGridClient"/> —
 * <see cref="getTransactionStatus"/> here only adapts the response shape so
 * the dispatcher can decide CONFIRMED / FAILED / pending without learning
 * the solidity-node payload.
 * </para>
 */
export class TronTransferClient {
  private readonly fullNodeUrl: string;
  private readonly solidityUrl: string;
  private readonly apiKey: string;
  private readonly tronWebFactory: TronWebFactory;

  constructor(
    fullNodeUrl: string,
    solidityUrl: string,
    apiKey: string,
    tronWebFactory: TronWebFactory = defaultTronWebFactory,
  ) {
    this.fullNodeUrl = fullNodeUrl;
    this.solidityUrl = solidityUrl;
    this.apiKey = apiKey;
    this.tronWebFactory = tronWebFactory;
  }

  /**
   * Build a TRC-20 transfer transaction via `triggersmartcontract`, sign it
   * locally, and broadcast through `broadcasttransaction`. Returns the txid
   * the chain assigned — the dispatcher persists this on the
   * `BlockchainTransaction` row and follows up with `getTransactionStatus`
   * once the solidity node has caught up.
   */
  async sendTransfer(request: SendTransferRequest): Promise<SendTransferResult> {
    if (!request.privateKey) {
      throw new SidecarError(
        'Transfer privateKey missing — signing aborted.',
        'TRANSFER_NO_PRIVATE_KEY',
        false,
      );
    }
    const tronWeb = this.tronWebFactory({
      fullHost: this.fullNodeUrl,
      apiKey: this.apiKey,
      privateKey: request.privateKey,
    }) as TronWebShape;

    // Fee cap (08 §3.3). Per-request override wins; otherwise the operator-tunable
    // `transferFeeLimitSun` config (WP10 — `TRANSFER_FEE_LIMIT_SUN`, default 100 TRX).
    const feeLimit = request.options?.feeLimitSun ?? config.transferFeeLimitSun;
    const callValue = request.options?.callValue ?? 0;

    try {
      const builder = tronWeb.transactionBuilder;
      const built = await builder.triggerSmartContract(
        request.contractAddress,
        'transfer(address,uint256)',
        { feeLimit, callValue },
        [
          { type: 'address', value: request.toAddress },
          { type: 'uint256', value: request.amountUnits },
        ],
        request.fromAddress,
      );

      if (!built.result?.result || !built.transaction) {
        transfersTotal.inc({ type: 'transfer', status: 'build_failed' });
        throw new SidecarError(
          `triggerSmartContract failed: ${built.result?.message ?? 'unknown error'}`,
          'TRANSFER_BUILD_FAILED',
          true,
        );
      }

      const trx = tronWeb.trx;
      const signed = await trx.sign(built.transaction, request.privateKey);
      const broadcast = await trx.sendRawTransaction(signed);

      if (!broadcast.result || !broadcast.txid) {
        transfersTotal.inc({ type: 'transfer', status: 'broadcast_rejected' });
        throw new SidecarError(
          `broadcasttransaction rejected: ${broadcast.message ?? broadcast.code ?? 'unknown'}`,
          'TRANSFER_BROADCAST_REJECTED',
          true,
        );
      }

      transfersTotal.inc({ type: 'transfer', status: 'broadcast_ok' });
      logger.info(
        {
          txHash: broadcast.txid,
          from: request.fromAddress,
          to: request.toAddress,
          contract: request.contractAddress,
          amountUnits: request.amountUnits,
        },
        'TRC-20 transfer broadcast',
      );
      return { txHash: broadcast.txid };
    } catch (err) {
      if (err instanceof SidecarError) {
        throw err;
      }
      transfersTotal.inc({ type: 'transfer', status: 'broadcast_failed' });
      logger.error({ err: (err as Error).message }, 'TRC-20 transfer broadcast failed');
      throw new SidecarError(
        `Transfer broadcast failed: ${(err as Error).message}`,
        'TRANSFER_BROADCAST_FAILED',
        true,
      );
    }
  }

  /**
   * Combine `getTransactionInfoById` + `getNowBlock` (solidity node) into a
   * single confirmation snapshot. T76 reconciliation also uses this shape
   * to verify that a dispatched txid actually landed on chain.
   */
  async getTransactionStatus(
    txHash: string,
    fetchFn: typeof fetch = fetch,
  ): Promise<TransactionStatusResult> {
    const infoUrl = `${this.solidityUrl}/walletsolidity/gettransactioninfobyid`;
    const blockUrl = `${this.solidityUrl}/walletsolidity/getnowblock`;
    const headers: Record<string, string> = {
      accept: 'application/json',
      'content-type': 'application/json',
    };
    if (this.apiKey) headers['TRON-PRO-API-KEY'] = this.apiKey;

    const [infoResponse, blockResponse] = await Promise.all([
      fetchFn(infoUrl, {
        method: 'POST',
        headers,
        body: JSON.stringify({ value: txHash }),
      }),
      fetchFn(blockUrl, {
        method: 'POST',
        headers,
        body: JSON.stringify({}),
      }),
    ]);

    if (!infoResponse.ok) {
      throw new SidecarError(
        `gettransactioninfobyid returned HTTP ${infoResponse.status}`,
        'TRANSFER_STATUS_HTTP_ERROR',
        true,
      );
    }
    if (!blockResponse.ok) {
      throw new SidecarError(
        `getnowblock returned HTTP ${blockResponse.status}`,
        'TRANSFER_STATUS_HTTP_ERROR',
        true,
      );
    }

    const info = (await infoResponse.json()) as {
      id?: string;
      blockNumber?: number;
      receipt?: { result?: string };
    };
    const block = (await blockResponse.json()) as {
      block_header?: { raw_data?: { number?: number } };
    };

    const txBlock =
      typeof info.blockNumber === 'number' && Number.isFinite(info.blockNumber)
        ? info.blockNumber
        : undefined;
    const solidBlock = block.block_header?.raw_data?.number;

    let confirmations = -1;
    if (txBlock !== undefined && typeof solidBlock === 'number') {
      confirmations = Math.max(0, solidBlock - txBlock);
    }

    return {
      txHash,
      blockNumber: txBlock,
      contractRet: info.receipt?.result,
      confirmations,
    };
  }
}

/**
 * Wrapper signature so unit tests can inject a stub TronWeb factory without
 * pulling the real SDK. The factory accepts the same shape the SDK expects
 * (full host, solidity node, optional API key, optional private key).
 */
export type TronWebFactory = (config: TronWebConstructorConfig) => unknown;

export interface TronWebConstructorConfig {
  fullHost: string;
  apiKey?: string;
  privateKey?: string;
}

interface TronWebShape {
  transactionBuilder: TransactionBuilderShape;
  trx: TrxShape;
}

interface TransactionBuilderShape {
  triggerSmartContract(
    contractAddress: string,
    functionSelector: string,
    options: { feeLimit: number; callValue: number },
    parameters: Array<{ type: string; value: string }>,
    fromAddress: string,
  ): Promise<{
    result?: { result?: boolean; message?: string };
    transaction?: unknown;
  }>;
}

interface TrxShape {
  sign(transaction: unknown, privateKey: string): Promise<unknown>;
  sendRawTransaction(signed: unknown): Promise<{
    result?: boolean;
    txid?: string;
    code?: string;
    message?: string;
  }>;
}

const defaultTronWebFactory: TronWebFactory = (config) => {
  const init: { fullHost: string; headers?: Record<string, string>; privateKey?: string } = {
    fullHost: config.fullHost,
  };
  if (config.apiKey) {
    init.headers = { 'TRON-PRO-API-KEY': config.apiKey };
  }
  if (config.privateKey) {
    init.privateKey = config.privateKey;
  }
  return new TronWeb(init);
};
