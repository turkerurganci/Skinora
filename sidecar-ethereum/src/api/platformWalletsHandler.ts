import type { Request, Response } from 'express';
import { checksummedEvmAddress } from '../address/evmAddress.js';

/**
 * The service's own wallets, as the service sees them (T157 — admin wallet
 * screen, 07 §9.33).
 *
 * `POST /api/wallet/platform { network }` →
 * `200 { hotWalletAddress, fuelWalletAddress, coldWalletAddress }`, each EIP-55
 * or `null` when that wallet is not provisioned. The fuel wallet's address is
 * derived from its key and is nowhere in the configuration — the admin panel
 * must ask the service, otherwise an operator topping the wallet up would have
 * to copy it from a startup log line. The cold wallet is the canonical pinned
 * destination; the hot wallet lets the backend notice that its own
 * `ETHEREUM_HOT_WALLET_ADDRESS` and the signing key disagree.
 *
 * Read-only; no key leaves the process.
 */
export interface PlatformWallets {
  readonly hotWalletAddress: string | null;
  readonly fuelWalletAddress: string | null;
  readonly coldWalletAddress: string | null;
}

export function platformWalletsHandler(read: () => PlatformWallets) {
  return (_req: Request, res: Response): void => {
    const wallets = read();
    const show = (address: string | null): string | null =>
      address === null ? null : checksummedEvmAddress(address);
    res.status(200).json({
      hotWalletAddress: show(wallets.hotWalletAddress),
      fuelWalletAddress: show(wallets.fuelWalletAddress),
      coldWalletAddress: show(wallets.coldWalletAddress),
    });
  };
}
