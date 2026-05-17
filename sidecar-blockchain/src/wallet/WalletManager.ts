import { logger } from '../logger.js';
import { config } from '../config/index.js';
import {
  DeriveResult,
  DeriveSignerResult,
  HdWalletNotConfiguredError,
  HdWalletService,
} from './HdWalletService.js';

export class WalletManager {
  private readonly hd: HdWalletService;

  constructor(hd?: HdWalletService) {
    this.hd = hd ?? new HdWalletService(config.hdWalletMnemonic);
  }

  async initialize(): Promise<void> {
    if (!this.hd.isConfigured()) {
      logger.warn(
        'HD wallet mnemonic is not configured — /api/wallet/derive will respond 503. ' +
          'Set HD_WALLET_MNEMONIC before production startup (08 §3.2).',
      );
      return;
    }

    // Eager-fail on malformed mnemonic so the sidecar refuses to advertise
    // healthy state when the BIP-39 phrase fails checksum.
    try {
      this.hd.derive(0);
      logger.info("HD wallet initialized — derivation path m/44'/195'/0'/0/{index} ready");
    } catch (err) {
      logger.error(
        { err: (err as Error).message },
        'HD wallet initialization failed — derive endpoint will reject requests',
      );
      throw err;
    }
  }

  async shutdown(): Promise<void> {
    logger.info('WalletManager shutting down');
  }

  isHdConfigured(): boolean {
    return this.hd.isConfigured();
  }

  derive(index: number): DeriveResult {
    if (!this.hd.isConfigured()) {
      throw new HdWalletNotConfiguredError();
    }
    return this.hd.derive(index);
  }

  /**
   * T73: derive private-key material for an outbound transfer originating
   * from a deposit address. Caller must invoke once and discard.
   */
  deriveSigner(index: number): DeriveSignerResult {
    if (!this.hd.isConfigured()) {
      throw new HdWalletNotConfiguredError();
    }
    return this.hd.deriveSigner(index);
  }
}
