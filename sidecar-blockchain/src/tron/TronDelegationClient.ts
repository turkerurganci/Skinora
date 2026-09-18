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
 *   <item><c>sendTrx</c> — plain TRX transfer to a deposit (08 §3.3): the
 *     1 SUN that activates it, the TRX its transfer will burn, or the fixed
 *     fallback when no plan could be computed.</item>
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
        undefined,
        permissionOptions(request.ownerPermissionId),
      );
      if (!built?.txID) {
        transfersTotal.inc({ type: 'delegate', status: 'build_failed' });
        throw new SidecarError(
          'delegateResource returned no txID — build failed.',
          'DELEGATE_BUILD_FAILED',
          true,
        );
      }
      const signed = await this.sign(tronWeb, built, request);
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
        { err: describeError(err), receiver: request.receiverAddress },
        'Energy delegation failed',
      );
      throw new SidecarError(
        `Energy delegation failed: ${describeError(err)}`,
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
        permissionOptions(request.ownerPermissionId),
      );
      if (!built?.txID) {
        transfersTotal.inc({ type: 'undelegate', status: 'build_failed' });
        throw new SidecarError(
          'undelegateResource returned no txID — build failed.',
          'UNDELEGATE_BUILD_FAILED',
          true,
        );
      }
      const signed = await this.sign(tronWeb, built, request);
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
        { err: describeError(err), receiver: request.receiverAddress },
        'Energy undelegation failed',
      );
      throw new SidecarError(
        `Energy undelegation failed: ${describeError(err)}`,
        'UNDELEGATE_BROADCAST_FAILED',
        true,
      );
    }
  }

  /**
   * Plain TRX transfer to a deposit (08 §3.3): activation (1 SUN), the burn
   * top-up when the stake cannot cover a transfer, or the fixed fallback when
   * no plan could be computed. The receiver then burns TRX to cover its own
   * TRC-20 transfer gas.
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
        { err: describeError(err), to: request.toAddress },
        'TRX fallback transfer failed',
      );
      throw new SidecarError(
        `TRX fallback transfer failed: ${describeError(err)}`,
        'FALLBACK_TRX_BROADCAST_FAILED',
        true,
      );
    }
  }

  /**
   * Sign as the transaction's owner, or <i>on behalf of</i> it.
   *
   * <para>
   * When the staked TRX lives in its own account (owner key offline) the hot
   * wallet's key is listed in that account's active permission, and
   * <c>trx.sign</c> refuses the transaction outright — measured on Nile
   * 2026-09-18: <c>"Private key does not match address in transaction"</c>.
   * <c>trx.multiSign</c> is the path that produces a signature the node
   * accepts against a permission the signer does not own; a signature offered
   * without the permission id is rejected by the chain with
   * <c>SIGERROR … is not contained of permission</c>, so a misconfigured id
   * fails loudly rather than signing something unintended.
   * </para>
   */
  private async sign(
    tronWeb: TronWebDelegationShape,
    built: unknown,
    request: DelegationRequest,
  ): Promise<unknown> {
    if (request.ownerPermissionId === undefined) {
      return tronWeb.trx.sign(built, request.ownerPrivateKey);
    }
    return tronWeb.trx.multiSign(built, request.ownerPrivateKey, request.ownerPermissionId);
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
  /** Account that holds the staked TRX — the dedicated stake account, or the
   * hot wallet itself while no stake account is configured. */
  ownerAddress: string;
  /** The signing key. It belongs to <c>ownerAddress</c> only when
   * <c>ownerPermissionId</c> is unset; otherwise it is the hot wallet's key,
   * signing on the stake account's behalf. */
  ownerPrivateKey: string;
  /** Active-permission id the signature is offered against. Unset means the
   * key owns the account and signs for itself (the pre-split arrangement). */
  ownerPermissionId?: number;
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

/**
 * What actually went wrong, in a form an operator can read.
 *
 * <para>
 * TronWeb does not always reject with an <c>Error</c>: offering a key that
 * does not own the transaction it is signing rejects with a bare string, and
 * reading <c>.message</c> off it yields <c>undefined</c> — measured on Nile
 * 2026-09-18, where a delegation misconfigured to skip the permission id
 * failed with the text "Energy delegation failed: undefined" and told the
 * operator nothing. The most likely causes of that rejection (a wrong
 * permission id, a stake account that never granted one) are exactly the ones
 * whose message matters.
 * </para>
 */
function describeError(err: unknown): string {
  if (err instanceof Error && err.message) return err.message;
  if (typeof err === 'string' && err) return err;
  try {
    const text = JSON.stringify(err);
    if (text && text !== '{}') return text;
  } catch {
    // A value that cannot be serialised still deserves a name below.
  }
  return String(err);
}
/** <c>{ permissionId }</c>, or nothing at all — TronWeb treats an empty object
 * as "no permission" but an explicit <c>undefined</c> keeps the builder's own
 * argument-shuffling (it accepts callbacks in these positions) unambiguous. */
function permissionOptions(permissionId: number | undefined): PermissionOptions | undefined {
  return permissionId === undefined ? undefined : { permissionId };
}

interface PermissionOptions {
  permissionId: number;
}

interface TronWebDelegationShape {
  transactionBuilder: {
    delegateResource(
      balance: number,
      receiverAddress: string,
      resource: 'ENERGY' | 'BANDWIDTH',
      ownerAddress: string,
      lock: boolean,
      lockPeriod?: number,
      options?: PermissionOptions,
    ): Promise<{ txID?: string } | undefined>;
    undelegateResource(
      balance: number,
      receiverAddress: string,
      resource: 'ENERGY' | 'BANDWIDTH',
      ownerAddress: string,
      options?: PermissionOptions,
    ): Promise<{ txID?: string } | undefined>;
    sendTrx(
      toAddress: string,
      amount: number,
      fromAddress: string,
    ): Promise<{ txID?: string } | undefined>;
  };
  trx: {
    sign(transaction: unknown, privateKey: string): Promise<unknown>;
    /** Signs against a permission the key does not own (see <c>sign</c>). */
    multiSign(transaction: unknown, privateKey: string, permissionId: number): Promise<unknown>;
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
