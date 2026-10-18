import { computeAddress } from 'ethers';
import { canonicalAddress, sameAddress } from '../address/canonicalAddress.js';
import { ConfigError, type EthereumServiceConfig } from '../config/index.js';

/**
 * Startup check of the service's two platform signing keys (08 §3.7.2,
 * 05 §3.5): the **hot wallet** (payout, cold transfer — T152) and the **fuel
 * wallet** (ETH gas top-ups to deposits — T153). Neither signs anything yet;
 * the check makes the deployment honest before they do.
 *
 * <list type="bullet">
 *   <item>The fuel key is a different key from the hot key
 *     (`(öneriyle kaydedildi — ⚠)`, 11 §F8 "Yakıt cüzdanının kendi anahtarı"):
 *     whoever steals the fuel key can only move small ETH amounts to the
 *     service's own deposits (T153). The same key — or two keys controlling the
 *     same address — refuses startup (`WALLET_KEYS_NOT_DISTINCT`).</item>
 *   <item>The fuel wallet is neither the configured hot nor the cold wallet.</item>
 *   <item>A hot key that does not control `ETHEREUM_HOT_WALLET_ADDRESS` (the
 *     address the backend sweeps to) refuses startup
 *     (`HOT_WALLET_ADDRESS_MISMATCH`) — the TRON service's pinned-destination
 *     rule (05 §3.3 "Platform adresleri") applied to the key.</item>
 * </list>
 *
 * Returns the two addresses (EIP-55) for the startup line; never the keys.
 */
export interface WalletKeyAddresses {
  readonly hotWalletAddress: string | null;
  readonly fuelWalletAddress: string | null;
}

export function checkWalletKeys(
  config: Pick<
    EthereumServiceConfig,
    'hotWalletKey' | 'fuelWalletKey' | 'hotWalletAddress' | 'coldWalletAddress'
  >,
): WalletKeyAddresses {
  const hot = config.hotWalletKey ? computeAddress(config.hotWalletKey) : null;
  const fuel = config.fuelWalletKey ? computeAddress(config.fuelWalletKey) : null;

  if (hot && fuel && sameAddress(hot, fuel)) {
    throw new ConfigError(
      'WALLET_KEYS_NOT_DISTINCT',
      'Fuel and hot wallet keys control ONE address — they must be separate keys (08 §3.7.2).',
    );
  }
  if (
    fuel &&
    (sameAddress(fuel, config.hotWalletAddress) || sameAddress(fuel, config.coldWalletAddress))
  ) {
    throw new ConfigError(
      'WALLET_KEYS_NOT_DISTINCT',
      'The fuel wallet key controls the configured hot or cold wallet address — it must be its own wallet (08 §3.7.2).',
    );
  }
  if (hot && config.hotWalletAddress && canonicalAddress(hot) !== config.hotWalletAddress) {
    throw new ConfigError(
      'HOT_WALLET_ADDRESS_MISMATCH',
      `The hot wallet key controls ${hot}, not ETHEREUM_HOT_WALLET_ADDRESS — refusing to start (05 §3.3 "Platform adresleri").`,
    );
  }
  return { hotWalletAddress: hot, fuelWalletAddress: fuel };
}
