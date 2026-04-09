import { logger } from '../logger.js';

/**
 * HD Wallet management stub — will be implemented in T70.
 * Manages BIP-44 address derivation from master seed (08 §3.2).
 * Derivation path: m/44'/195'/0'/0/{index}
 */
export class WalletManager {
  async initialize(): Promise<void> {
    logger.info('WalletManager initialized (skeleton — no wallet configured)');
  }

  async shutdown(): Promise<void> {
    logger.info('WalletManager shutting down');
  }
}
