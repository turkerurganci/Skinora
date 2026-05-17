import TronWeb from 'tronweb';
import { logger } from '../logger.js';
import { SidecarError } from '../errors/SidecarError.js';
import { transfersTotal } from '../metrics.js';

/**
 * Thin TronWeb adapter for TRON Stake 2.0 Energy delegation (08 §3.3).
 *
 * <para>
 * Three primitives live here:
 * <list type="bullet">
 *   <item><c>delegateEnergy</c> — sweeper account → deposit address Energy
 *     delegation via <c>delegateresource</c>, used before deposit-sourced
 *     sweep / refund broadcasts so the deposit pays no TRX out of its own
 *     balance.</item>
 *   <item><c>undelegateEnergy</c> — delegation reclaim via
 *     <c>undelegateresource</c>, used after the broadcast succeeds. With
 *     <c>lock=false</c> (the only mode we use), reclaim is instant.</item>
 *   <item><c>sendTrx</c> — fallback TRX transfer (08 §3.3 "delegation
 *     başarısızsa deposit adresine minimum TRX transfer"). Used when
 *     delegation itself fails so the deposit can still pay its own gas.</item>
 * </list>
 * </para>
 *
 * <para>
 * Each call instantiates a fresh <see cref="TronWeb"/> binding scoped to the
 * sweeper's private key — mirror of <see cref="TronTransferClient"/> signing
 * isolation (05 §3.3). Constructor is signer-free because the same client
 * serves multiple flows (sweep, refund, future payout-from-deposit).
 * </para>
 */
export class TronDelegationClient {
  private readonly fullNodeUrl: string;
  private readonly apiKey: string;
  private readonly tronWebFactory: DelegationTronWebFactory;

  constructor(
    fullNodeUrl: string,
    apiKey: string,
    tronWebFactory: DelegationTronWebFactory = defaultDelegationTronWebFactory,
  ) {
    this.fullNodeUrl = fullNodeUrl;
    this.apiKey = apiKey;
    this.tronWebFactory = tronWebFactory;
  }

  /**
   * Delegate Energy from <paramref name="ownerAddress"/> to
   * <paramref name="receiverAddress"/> for the duration of a single sweep /
   * refund broadcast. Always issued with <c>lock=false</c> so the caller can
   * reclaim immediately after the broadcast lands.
   */
  async delegateEnergy(request: DelegationRequest): Promise<DelegationResult> {
    const tronWeb = this.bind(request.ownerPrivateKey) as TronWebDelegationShape;
    try {
      const built = await tronWeb.transactionBuilder.delegateResource(
        request.amountSun,
        request.receiverAddress,
        'ENERGY',
        request.ownerAddress,
        false,
      );
      if (!built?.txID) {
        transfersTotal.inc({ type: 'delegate', status: 'build_failed' });
        throw new SidecarError(
          'delegateResource returned no txID — build failed.',
          'DELEGATE_BUILD_FAILED',
          true,
        );
      }
      const signed = await tronWeb.trx.sign(built, request.ownerPrivateKey);
      const broadcast = await tronWeb.trx.sendRawTransaction(signed);
      if (!broadcast.result || !broadcast.txid) {
        transfersTotal.inc({ type: 'delegate', status: 'broadcast_rejected' });
        throw new SidecarError(
          `delegateResource broadcast rejected: ${broadcast.message ?? broadcast.code ?? 'unknown'}`,
          'DELEGATE_BROADCAST_REJECTED',
          true,
        );
      }
      transfersTotal.inc({ type: 'delegate', status: 'broadcast_ok' });
      logger.info(
        {
          txHash: broadcast.txid,
          owner: request.ownerAddress,
          receiver: request.receiverAddress,
          amountSun: request.amountSun,
        },
        'Energy delegation broadcast',
      );
      return { txHash: broadcast.txid };
    } catch (err) {
      if (err instanceof SidecarError) {
        throw err;
      }
      transfersTotal.inc({ type: 'delegate', status: 'broadcast_failed' });
      logger.error(
        { err: (err as Error).message, receiver: request.receiverAddress },
        'Energy delegation failed',
      );
      throw new SidecarError(
        `Energy delegation failed: ${(err as Error).message}`,
        'DELEGATE_BROADCAST_FAILED',
        true,
      );
    }
  }

  /**
   * Reclaim previously delegated Energy. Best-effort: callers are expected to
   * surface failures but never to fail the upstream sweep / refund because of
   * an undelegate problem — the broadcast already succeeded by then.
   */
  async undelegateEnergy(request: DelegationRequest): Promise<DelegationResult> {
    const tronWeb = this.bind(request.ownerPrivateKey) as TronWebDelegationShape;
    try {
      const built = await tronWeb.transactionBuilder.undelegateResource(
        request.amountSun,
        request.receiverAddress,
        'ENERGY',
        request.ownerAddress,
      );
      if (!built?.txID) {
        transfersTotal.inc({ type: 'undelegate', status: 'build_failed' });
        throw new SidecarError(
          'undelegateResource returned no txID — build failed.',
          'UNDELEGATE_BUILD_FAILED',
          true,
        );
      }
      const signed = await tronWeb.trx.sign(built, request.ownerPrivateKey);
      const broadcast = await tronWeb.trx.sendRawTransaction(signed);
      if (!broadcast.result || !broadcast.txid) {
        transfersTotal.inc({ type: 'undelegate', status: 'broadcast_rejected' });
        throw new SidecarError(
          `undelegateResource broadcast rejected: ${broadcast.message ?? broadcast.code ?? 'unknown'}`,
          'UNDELEGATE_BROADCAST_REJECTED',
          true,
        );
      }
      transfersTotal.inc({ type: 'undelegate', status: 'broadcast_ok' });
      logger.info(
        {
          txHash: broadcast.txid,
          owner: request.ownerAddress,
          receiver: request.receiverAddress,
          amountSun: request.amountSun,
        },
        'Energy undelegation broadcast',
      );
      return { txHash: broadcast.txid };
    } catch (err) {
      if (err instanceof SidecarError) {
        throw err;
      }
      transfersTotal.inc({ type: 'undelegate', status: 'broadcast_failed' });
      logger.error(
        { err: (err as Error).message, receiver: request.receiverAddress },
        'Energy undelegation failed',
      );
      throw new SidecarError(
        `Energy undelegation failed: ${(err as Error).message}`,
        'UNDELEGATE_BROADCAST_FAILED',
        true,
      );
    }
  }

  /**
   * Plain TRX transfer used as 08 §3.3 fallback when the delegation path
   * cannot deliver Energy (e.g. sweeper Energy budget exhausted, network
   * regression in the staking module). The receiver then burns TRX to cover
   * its own TRC-20 transfer gas.
   */
  async sendTrx(request: TrxTransferRequest): Promise<DelegationResult> {
    const tronWeb = this.bind(request.fromPrivateKey) as TronWebDelegationShape;
    try {
      const built = await tronWeb.transactionBuilder.sendTrx(
        request.toAddress,
        request.amountSun,
        request.fromAddress,
      );
      if (!built?.txID) {
        transfersTotal.inc({ type: 'fallback_trx', status: 'build_failed' });
        throw new SidecarError(
          'sendTrx returned no txID — build failed.',
          'FALLBACK_TRX_BUILD_FAILED',
          true,
        );
      }
      const signed = await tronWeb.trx.sign(built, request.fromPrivateKey);
      const broadcast = await tronWeb.trx.sendRawTransaction(signed);
      if (!broadcast.result || !broadcast.txid) {
        transfersTotal.inc({ type: 'fallback_trx', status: 'broadcast_rejected' });
        throw new SidecarError(
          `sendTrx broadcast rejected: ${broadcast.message ?? broadcast.code ?? 'unknown'}`,
          'FALLBACK_TRX_BROADCAST_REJECTED',
          true,
        );
      }
      transfersTotal.inc({ type: 'fallback_trx', status: 'broadcast_ok' });
      logger.info(
        {
          txHash: broadcast.txid,
          from: request.fromAddress,
          to: request.toAddress,
          amountSun: request.amountSun,
        },
        'TRX fallback transfer broadcast',
      );
      return { txHash: broadcast.txid };
    } catch (err) {
      if (err instanceof SidecarError) {
        throw err;
      }
      transfersTotal.inc({ type: 'fallback_trx', status: 'broadcast_failed' });
      logger.error(
        { err: (err as Error).message, to: request.toAddress },
        'TRX fallback transfer failed',
      );
      throw new SidecarError(
        `TRX fallback transfer failed: ${(err as Error).message}`,
        'FALLBACK_TRX_BROADCAST_FAILED',
        true,
      );
    }
  }

  private bind(privateKey: string): unknown {
    if (!privateKey) {
      throw new SidecarError(
        'Delegation privateKey missing — signing aborted.',
        'DELEGATE_NO_PRIVATE_KEY',
        false,
      );
    }
    return this.tronWebFactory({
      fullHost: this.fullNodeUrl,
      apiKey: this.apiKey,
      privateKey,
    });
  }
}

export interface DelegationRequest {
  /** Owner account that holds the staked TRX (typically the hot wallet). */
  ownerAddress: string;
  /** Owner's signing key — scoped to a single broadcast. */
  ownerPrivateKey: string;
  /** Deposit address receiving the temporary Energy budget. */
  receiverAddress: string;
  /** SUN units (1 TRX = 1_000_000 SUN). Stake 2.0 takes the TRX amount the
   * delegation is backed by; Energy generation is derived by the chain. */
  amountSun: number;
}

export interface TrxTransferRequest {
  fromAddress: string;
  fromPrivateKey: string;
  toAddress: string;
  amountSun: number;
}

export interface DelegationResult {
  txHash: string;
}

export type DelegationTronWebFactory = (config: {
  fullHost: string;
  apiKey?: string;
  privateKey?: string;
}) => unknown;

interface TronWebDelegationShape {
  transactionBuilder: {
    delegateResource(
      balance: number,
      receiverAddress: string,
      resource: 'ENERGY' | 'BANDWIDTH',
      ownerAddress: string,
      lock: boolean,
      lockPeriod?: number,
    ): Promise<{ txID?: string } | undefined>;
    undelegateResource(
      balance: number,
      receiverAddress: string,
      resource: 'ENERGY' | 'BANDWIDTH',
      ownerAddress: string,
    ): Promise<{ txID?: string } | undefined>;
    sendTrx(
      toAddress: string,
      amount: number,
      fromAddress: string,
    ): Promise<{ txID?: string } | undefined>;
  };
  trx: {
    sign(transaction: unknown, privateKey: string): Promise<unknown>;
    sendRawTransaction(signed: unknown): Promise<{
      result?: boolean;
      txid?: string;
      code?: string;
      message?: string;
    }>;
  };
}

const defaultDelegationTronWebFactory: DelegationTronWebFactory = (config) => {
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
