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

/**
 * Largest non-hardened BIP-32 child index (2^31 - 1). Indexes at or above
 * 2^31 fall into the hardened range, which BIP-44 reserves for the purpose,
 * coin and account levels; the address level must stay below it.
 */
export const MAX_DERIVATION_INDEX = 0x7fffffff;

/**
 * Absolute BIP-44 path of the TRON deposit address at <c>index</c>
 * (08 §3.2). This exact string is handed to the derivation, so the path the
 * sidecar reports to the backend is the path it actually used (T143).
 */
export function derivationPath(index: number): string {
  return `m/44'/${COIN_TYPE}'/${ACCOUNT}'/${CHANGE}/${index}`;
}

/**
 * Throws unless <c>node</c> is a BIP-32 master node (depth 0, path "m").
 *
 * Guards the pre-T143 defect: ethers v6 <c>HDNodeWallet.fromMnemonic(m)</c>
 * called WITHOUT a path does not return the master node — it applies its
 * default Ethereum path and returns the node at m/44'/60'/0'/0/0 (depth 5).
 * This service used that node as its "root" and appended the TRON path to it,
 * so every deposit address really lived at
 * m/44'/60'/0'/0/0/44'/195'/0'/0/{i}: invisible to any standard TRON wallet
 * restoring the same seed, while derivationPath() claimed m/44'/195'/0'/0/{i}.
 */
export function assertMasterNode(node: HDNodeWallet): void {
  if (node.depth !== 0 || node.path !== 'm') {
    throw new SidecarError(
      `HD root must be the BIP-32 master node (depth 0, path "m"); got depth ${node.depth}, path ${String(node.path)}.`,
      'HD_ROOT_NOT_MASTER',
      false,
    );
  }
}

/**
 * BIP-32 master node for a BIP-39 phrase (empty passphrase). The explicit
 * "m" path is load-bearing — see <see cref="assertMasterNode"/>. Throws the
 * ethers parse error for a malformed phrase; callers map it to their own code.
 */
export function masterNodeFromPhrase(phrase: string): HDNodeWallet {
  const root = HDNodeWallet.fromMnemonic(Mnemonic.fromPhrase(phrase), 'm');
  assertMasterNode(root);
  return root;
}

function assertValidIndex(index: number): void {
  if (!Number.isInteger(index) || index < 0 || index > MAX_DERIVATION_INDEX) {
    throw new InvalidDerivationIndexError(
      `Index must be an integer between 0 and ${MAX_DERIVATION_INDEX} (received: ${String(index)}).`,
    );
  }
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
   * that later signs for it cannot come from two different paths. The path is
   * absolute ("m/..."), which also makes ethers refuse a non-master root on
   * its own; the node's recorded path is checked against the requested one.
   */
  private deriveKey(index: number): { address: string; path: string; keyHex: string } {
    const path = derivationPath(index);
    const child = this.getRoot().derivePath(path);
    if (child.path !== path) {
      throw new SidecarError(
        `Derived node path ${String(child.path)} does not match the requested path ${path}.`,
        'DERIVATION_FAILED',
        false,
      );
    }

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

  private getRoot(): HDNodeWallet {
    if (this.root) return this.root;
    let root: HDNodeWallet;
    try {
      root = HDNodeWallet.fromMnemonic(Mnemonic.fromPhrase(this.mnemonic), 'm');
    } catch (err) {
      logger.error({ err: (err as Error).message }, 'Failed to parse HD master mnemonic');
      throw new SidecarError(
        'Master mnemonic is malformed (BIP-39 word list mismatch or checksum failure).',
        'INVALID_MASTER_MNEMONIC',
        false,
      );
    }
    // Outside the try: a non-master root is a code defect, not a malformed
    // phrase, and must not be reported as INVALID_MASTER_MNEMONIC.
    assertMasterNode(root);
    this.root = root;
    return root;
  }
}
