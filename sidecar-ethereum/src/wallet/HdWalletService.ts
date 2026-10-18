import { getAddress, type HDNodeWallet } from 'ethers';
import { SidecarError } from '../errors/SidecarError.js';
import { ETHEREUM_COIN_TYPE } from '../network.js';
import { assertValidIndex, deriveChild, masterNodeFromPhrase } from './hdRoot.js';

export interface DeriveResult {
  /** EIP-55 display form; the backend stores it lowercase (06 §1.4). */
  readonly address: string;
  readonly derivationPath: string;
  readonly index: number;
}

/**
 * Derived signing material (T152 signs sweeps and refunds with it). The
 * caller must use it once and drop the reference — 05 §3.5 "Private key
 * kullanımı"; nothing here caches a key.
 */
export interface DeriveSignerResult extends DeriveResult {
  readonly privateKey: string;
}

/** Absolute path of the Ethereum deposit at <c>index</c> (08 §3.7.2): m/44'/60'/0'/0/{index}. */
export function derivationPath(index: number): string {
  return `m/44'/${ETHEREUM_COIN_TYPE}'/0'/0/${index}`;
}

/**
 * Ethereum deposit addresses from the service's OWN seed (11 §F8 K7, 08 §3.7.2).
 *
 * <para>
 * The root is built once, at construction, by the shared production builder
 * (`hdRoot.ts`, byte-identical in the TRON service): a malformed phrase or a
 * non-master root stops the service at startup instead of at the first
 * deposit. Address and signing key come out of one derivation routine, so the
 * address the backend stores and the key that later signs for it cannot come
 * from two different paths (T143).
 * </para>
 */
export class EthereumHdWallet {
  private readonly root: HDNodeWallet;
  private readonly addressCache = new Map<number, DeriveResult>();

  constructor(mnemonic: string) {
    this.root = masterNodeFromPhrase(mnemonic);
  }

  derive(index: number): DeriveResult {
    assertValidIndex(index);
    const cached = this.addressCache.get(index);
    if (cached) return cached;
    const { address, path } = this.deriveKey(index);
    const result: DeriveResult = { address, derivationPath: path, index };
    this.addressCache.set(index, result);
    return result;
  }

  deriveSigner(index: number): DeriveSignerResult {
    assertValidIndex(index);
    const { address, path, privateKey } = this.deriveKey(index);
    return { address, derivationPath: path, index, privateKey };
  }

  private deriveKey(index: number): { address: string; path: string; privateKey: string } {
    const path = derivationPath(index);
    const child = deriveChild(this.root, path);
    const address = getAddress(child.address);
    if (!/^0x[0-9a-fA-F]{40}$/.test(address)) {
      throw new SidecarError(
        'Derived key produced an invalid Ethereum address.',
        'DERIVATION_FAILED',
        false,
      );
    }
    return { address, path, privateKey: child.privateKey };
  }
}
