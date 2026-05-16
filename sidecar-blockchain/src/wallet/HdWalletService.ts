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

const COIN_TYPE = 195;
const ACCOUNT = 0;
const CHANGE = 0;

export function derivationPath(index: number): string {
  return `m/44'/${COIN_TYPE}'/${ACCOUNT}'/${CHANGE}/${index}`;
}

export class HdWalletService {
  private root: HDNodeWallet | null = null;
  private readonly mnemonic: string;

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

    const root = this.getRoot();
    const child = root.derivePath(this.relativePath(index));

    const privateKeyHex = child.privateKey.slice(2);
    try {
      const address = TronWeb.address.fromPrivateKey(privateKeyHex);
      if (typeof address !== 'string' || !address.startsWith('T')) {
        throw new SidecarError(
          'TronWeb returned an invalid Tron address for the derived key.',
          'DERIVATION_FAILED',
          false,
        );
      }
      return { address, derivationPath: derivationPath(index), index };
    } finally {
      // Private key string is short-lived. We can't truly zero a JS string
      // (immutable), but clearing the local reference helps GC and avoids
      // accidental retention in any closure created in this scope.
    }
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
