import { HDNodeWallet, Mnemonic } from 'ethers';
import TronWeb from 'tronweb';
import { logger } from '../logger.js';
import { SidecarError } from '../errors/SidecarError.js';

export class HdWalletNotConfiguredError extends SidecarError {
  constructor() {
    super('Master mnemonic is not configured.', 'HD_WALLET_NOT_CONFIGURED', false);
    this.name = 'HdWalletNotConfiguredError';
  }
}

export class InvalidDerivationIndexError extends SidecarError {
  constructor(reason: string) {
    super(reason, 'INVALID_DERIVATION_INDEX', false);
    this.name = 'InvalidDerivationIndexError';
  }
}

export interface DeriveResult {
  readonly address: string;
  readonly derivationPath: string;
  readonly index: number;
}

/**
 * Derived signing material. The caller MUST treat <c>privateKey</c> as
 * short-lived and drop the reference immediately after signing — JS strings
 * are immutable, so the best we can do is starve the GC of long-lived refs
 * (05 §3.3 "Private key kullanımı"). See <c>HdWalletService.deriveSigner</c>.
 */
export interface DeriveSignerResult extends DeriveResult {
  readonly privateKey: string;
}

const COIN_TYPE = 195;
const ACCOUNT = 0;
const CHANGE = 0;

export function derivationPath(index: number): string {
  return `m/44'/${COIN_TYPE}'/${ACCOUNT}'/${CHANGE}/${index}`;
}

export class HdWalletService {
  private root: HDNodeWallet | null = null;
  private readonly mnemonic: string;

  /**
   * Per-index derived-address cache (08 §3.2 — WP10 HD address cache). BIP-32
   * derivation is deterministic, so once an index → address is computed it
   * never changes for the configured mnemonic; caching it avoids re-running
   * the keccak/secp256k1 work on every monitor/derive request. Only the
   * **public address** is cached — <see cref="deriveSigner"/> recomputes the
   * private key on demand and never persists it (05 §3.3 signing isolation).
   */
  private readonly addressCache = new Map<number, DeriveResult>();

  constructor(mnemonic: string) {
    this.mnemonic = mnemonic.trim();
  }

  isConfigured(): boolean {
    return this.mnemonic.length > 0;
  }

  derive(index: number): DeriveResult {
    if (!Number.isInteger(index) || index < 0) {
      throw new InvalidDerivationIndexError(
        `Index must be a non-negative integer (received: ${String(index)}).`,
      );
    }
    if (!this.isConfigured()) {
      throw new HdWalletNotConfiguredError();
    }

    const cached = this.addressCache.get(index);
    if (cached) {
      return cached;
    }

    const root = this.getRoot();
    const child = root.derivePath(this.relativePath(index));

    const privateKeyHex = child.privateKey.slice(2);
    const address = TronWeb.address.fromPrivateKey(privateKeyHex);
    if (typeof address !== 'string' || !address.startsWith('T')) {
      throw new SidecarError(
        'TronWeb returned an invalid Tron address for the derived key.',
        'DERIVATION_FAILED',
        false,
      );
    }
    const result: DeriveResult = { address, derivationPath: derivationPath(index), index };
    this.addressCache.set(index, result);
    return result;
  }

  /**
   * Same derivation as <see cref="derive"/> but also returns the private key
   * hex for signing. T73 outbound transfers from deposit addresses
   * (refund / sweep) need this — the caller is responsible for invoking the
   * material exactly once and not stashing it in long-lived state. See
   * <see cref="TronTransferClient.sendTransfer"/> which scopes the key to a
   * single TronWeb instance per call (05 §3.3 signing isolation).
   */
  deriveSigner(index: number): DeriveSignerResult {
    if (!Number.isInteger(index) || index < 0) {
      throw new InvalidDerivationIndexError(
        `Index must be a non-negative integer (received: ${String(index)}).`,
      );
    }
    if (!this.isConfigured()) {
      throw new HdWalletNotConfiguredError();
    }

    const root = this.getRoot();
    const child = root.derivePath(this.relativePath(index));
    const privateKeyHex = child.privateKey.slice(2);

    const address = TronWeb.address.fromPrivateKey(privateKeyHex);
    if (typeof address !== 'string' || !address.startsWith('T')) {
      throw new SidecarError(
        'TronWeb returned an invalid Tron address for the derived key.',
        'DERIVATION_FAILED',
        false,
      );
    }
    return { address, derivationPath: derivationPath(index), index, privateKey: privateKeyHex };
  }

  private relativePath(index: number): string {
    return `44'/${COIN_TYPE}'/${ACCOUNT}'/${CHANGE}/${index}`;
  }

  private getRoot(): HDNodeWallet {
    if (this.root) return this.root;
    try {
      const mnemonic = Mnemonic.fromPhrase(this.mnemonic);
      this.root = HDNodeWallet.fromMnemonic(mnemonic);
      return this.root;
    } catch (err) {
      logger.error({ err: (err as Error).message }, 'Failed to parse HD master mnemonic');
      throw new SidecarError(
        'Master mnemonic is malformed (BIP-39 word list mismatch or checksum failure).',
        'INVALID_MASTER_MNEMONIC',
        false,
      );
    }
  }
}
