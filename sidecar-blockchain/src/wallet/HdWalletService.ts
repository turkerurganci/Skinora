import type { HDNodeWallet } from 'ethers';
import TronWeb from 'tronweb';
import { logger } from '../logger.js';
import { SidecarError } from '../errors/SidecarError.js';
import {
  HdWalletNotConfiguredError,
  assertValidIndex,
  deriveChild,
  masterNodeFromPhrase,
} from './hdRoot.js';

// The shared root/child primitives (T150) — re-exported so existing importers
// keep one import site.
export {
  HdWalletNotConfiguredError,
  InvalidDerivationIndexError,
  MAX_DERIVATION_INDEX,
  assertMasterNode,
  masterNodeFromPhrase,
} from './hdRoot.js';

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

/**
 * Absolute BIP-44 path of the TRON deposit address at <c>index</c>
 * (08 §3.2). This exact string is handed to the derivation, so the path the
 * sidecar reports to the backend is the path it actually used (T143).
 */
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
    assertValidIndex(index);
    if (!this.isConfigured()) {
      throw new HdWalletNotConfiguredError();
    }

    const cached = this.addressCache.get(index);
    if (cached) {
      return cached;
    }

    const { address, path } = this.deriveKey(index);
    const result: DeriveResult = { address, derivationPath: path, index };
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
    assertValidIndex(index);
    if (!this.isConfigured()) {
      throw new HdWalletNotConfiguredError();
    }

    const { address, path, keyHex: privateKey } = this.deriveKey(index);
    return { address, derivationPath: path, index, privateKey };
  }

  /**
   * Single derivation routine behind both <see cref="derive"/> and
   * <see cref="deriveSigner"/>, so the address the backend stores and the key
   * that later signs for it cannot come from two different paths.
   */
  private deriveKey(index: number): { address: string; path: string; keyHex: string } {
    const path = derivationPath(index);
    const child = deriveChild(this.getRoot(), path);

    const keyHex = child.privateKey.slice(2);
    const address = TronWeb.address.fromPrivateKey(keyHex);
    if (typeof address !== 'string' || !address.startsWith('T')) {
      throw new SidecarError(
        'TronWeb returned an invalid Tron address for the derived key.',
        'DERIVATION_FAILED',
        false,
      );
    }
    return { address, path, keyHex };
  }

  /**
   * The master node, built once by the shared production builder
   * (`masterNodeFromPhrase`, T150 — the T143 helper and this method used to
   * build the root separately, so the Ethereum vectors pinned on the helper
   * did not run through the code that derives deposits).
   */
  private getRoot(): HDNodeWallet {
    if (this.root) return this.root;
    try {
      this.root = masterNodeFromPhrase(this.mnemonic);
    } catch (err) {
      if (err instanceof SidecarError && err.code === 'INVALID_MASTER_MNEMONIC') {
        logger.error({ code: err.code }, 'Failed to parse HD master mnemonic');
      }
      throw err;
    }
    return this.root;
  }
}
