import { logger } from '../logger.js';
import {
  DeriveResult,
  DeriveSignerResult,
  HdWalletNotConfiguredError,
  HdWalletService,
} from './HdWalletService.js';

export class WalletManager {
  private readonly hd: HdWalletService;

  /**
   * T163 — the seed is handed in by the entry point, read from its Docker
   * secret (config/signingSecrets.ts); this class never reads the environment.
   */
  constructor(hd: HdWalletService) {
    this.hd = hd;
  }

  async initialize(): Promise<void> {
    if (!this.hd.isConfigured()) {
      logger.warn(
        'HD wallet mnemonic is not configured — /api/wallet/derive will respond 503. ' +
          'Provide the seed as the Docker secret secrets/tron_hd_mnemonic.txt (08 §3.2, DEPLOY_RUNBOOK §L).',
      );
      return;
    }

    // Eager-fail on malformed mnemonic so the sidecar refuses to advertise
    // healthy state when the BIP-39 phrase fails checksum.
    try {
      // Index 0 is a public address; it lets an operator compare the seed in
      // use with the offline backup's summary (DEPLOY_RUNBOOK §L) without
      // anything secret leaving the process.
      const { address } = this.hd.derive(0);
      logger.info(
        { depositIndex0: address },
        "HD wallet initialized — derivation path m/44'/195'/0'/0/{index} ready",
      );
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
