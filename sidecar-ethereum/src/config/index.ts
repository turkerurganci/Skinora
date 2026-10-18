import { canonicalAddress } from '../address/canonicalAddress.js';
import { checksummedEvmAddress } from '../address/evmAddress.js';
import { SidecarError } from '../errors/SidecarError.js';
import { ETHEREUM_CHAINS, MAINNET_TOKEN_CONTRACTS, type EthereumChainName } from '../network.js';
import { readSecretFile } from './secretFile.js';

/**
 * Configuration of the Ethereum service (T150 — 05 §3.3a, §3.5; 08 §3.7).
 *
 * <para>
 * <b>Fail closed at startup.</b> {@link loadConfig} throws a {@link ConfigError}
 * on anything the service must not run without: the internal key (05 §3.4 —
 * "iç anahtarsız açılmaz", 08 §3.7.2), its own seed (K7), a primary RPC
 * provider, and well-formed values for everything that is set. index.ts turns
 * the throw into a non-zero exit, so a misconfigured signer never listens.
 * </para>
 *
 * <para>
 * <b>Secrets are files, not variables.</b> The seed and the two signing keys
 * are Docker secrets (`*_FILE` paths, default `/run/secrets/...`); the
 * internal key, the webhook secret and the RPC URLs (which carry the
 * provider's API key) are environment values like the TRON service's.
 * </para>
 */

export class ConfigError extends SidecarError {
  constructor(code: string, message: string) {
    super(message, code, false);
    this.name = 'ConfigError';
  }
}

export interface RpcProviderSettings {
  /** Label used in logs and metrics — never the URL, which carries the API key. */
  readonly name: string;
  readonly url: string;
  /** Widest `eth_getLogs` block range the provider accepts (08 §3.7.4: Alchemy Free 10, Chainstack Developer 100). */
  readonly maxBlockRange: number;
  /** Most addresses (or topic values) in one search position (08 §3.7.4: 1,000, geth's LogQueryLimit). */
  readonly maxAddressesPerQuery: number;
}

export interface EthereumServiceConfig {
  readonly port: number;
  readonly nodeEnv: string;
  readonly internalKey: string;
  /** HMAC secret for webhooks to the backend (05 §3.3a). Read now, used from T151 on; '' = not set. */
  readonly webhookHmacKey: string;
  readonly backendUrl: string;
  readonly chain: EthereumChainName;
  readonly chainId: number;
  /** BIP-39 phrase of the Ethereum seed (K7). Never logged. */
  readonly hdMnemonic: string;
  /** Hot wallet signing key (payout, cold transfer — T152), or null while not provisioned. */
  readonly hotWalletKey: string | null;
  /** Fuel wallet signing key (gas top-ups — T153), or null while not provisioned. */
  readonly fuelWalletKey: string | null;
  /** Canonical (lowercase) — same value the backend reads (ETHEREUM_HOT_WALLET_ADDRESS). */
  readonly hotWalletAddress: string | null;
  readonly coldWalletAddress: string | null;
  /** Canonical token contracts; '' = not configured (a testnet without T159's USDT copy yet). */
  readonly tokenContracts: { readonly USDT: string; readonly USDC: string };
  readonly rpc: {
    readonly providers: readonly RpcProviderSettings[];
    readonly timeoutMs: number;
    readonly failoverCooldownMs: number;
  };
  readonly finality: {
    /** How often the finalized head is read (08 §3.7.4: 30 s). */
    readonly pollIntervalMs: number;
    /** Finalized block older than this → unhealthy and alarm (08 §3.7.8: 30 min). */
    readonly stallThresholdSeconds: number;
  };
  readonly shutdownTimeoutMs: number;
}

export const DEFAULT_SECRET_PATHS = {
  hdMnemonic: '/run/secrets/ethereum_hd_mnemonic',
  hotWalletKey: '/run/secrets/ethereum_hot_wallet_private_key',
  fuelWalletKey: '/run/secrets/ethereum_fuel_wallet_private_key',
} as const;

/** Internal key length floor — the runbook's `openssl rand -hex 32` is 64 characters. */
export const MIN_INTERNAL_KEY_LENGTH = 32;

type Env = Readonly<Record<string, string | undefined>>;
type MountedFileReader = (path: string) => string | null;

function value(env: Env, name: string): string {
  return (env[name] ?? '').trim();
}

function positiveInt(env: Env, name: string, fallback: number): number {
  const raw = value(env, name);
  if (raw === '') return fallback;
  if (!/^\d+$/.test(raw) || Number(raw) <= 0) {
    throw new ConfigError(
      'INVALID_CONFIG',
      `${name} must be a positive whole number, got "${raw}".`,
    );
  }
  return Number(raw);
}

/** PORT: 0..65535 (0 = any free port, used by the startup tests). */
function listenPort(env: Env): number {
  const raw = value(env, 'PORT');
  if (raw === '') return 5300;
  if (!/^\d+$/.test(raw) || Number(raw) > 65535) {
    throw new ConfigError('INVALID_CONFIG', `PORT must be a port number, got "${raw}".`);
  }
  return Number(raw);
}

function optionalAddress(env: Env, name: string): string | null {
  const raw = value(env, name);
  if (raw === '') return null;
  if (checksummedEvmAddress(raw) === null) {
    throw new ConfigError(
      'INVALID_CONFIG',
      `${name} is not an Ethereum address (0x + 40 hex; mixed case must carry a valid EIP-55 checksum).`,
    );
  }
  return canonicalAddress(raw);
}

function providerFrom(env: Env, prefix: 'PRIMARY' | 'BACKUP', defaults: { maxBlockRange: number }) {
  const url = value(env, `ETHEREUM_RPC_${prefix}_URL`);
  if (url === '') return null;
  let parsed: URL;
  try {
    parsed = new URL(url);
  } catch {
    // The URL itself is not echoed: provider URLs carry the API key.
    throw new ConfigError('INVALID_CONFIG', `ETHEREUM_RPC_${prefix}_URL is not a URL.`);
  }
  if (parsed.protocol !== 'https:' && parsed.protocol !== 'http:') {
    throw new ConfigError('INVALID_CONFIG', `ETHEREUM_RPC_${prefix}_URL must be http(s).`);
  }
  const settings: RpcProviderSettings = {
    name: value(env, `ETHEREUM_RPC_${prefix}_NAME`) || prefix.toLowerCase(),
    url,
    maxBlockRange: positiveInt(
      env,
      `ETHEREUM_RPC_${prefix}_MAX_BLOCK_RANGE`,
      defaults.maxBlockRange,
    ),
    maxAddressesPerQuery: positiveInt(env, `ETHEREUM_RPC_${prefix}_MAX_ADDRESSES`, 1000),
  };
  return settings;
}

function signingKey(raw: string | null, name: string): string | null {
  if (raw === null) return null;
  const hex = raw.startsWith('0x') || raw.startsWith('0X') ? raw.slice(2) : raw;
  if (!/^[0-9a-fA-F]{64}$/.test(hex)) {
    // The value is never echoed — only that it is malformed.
    throw new ConfigError('INVALID_SIGNING_KEY', `${name} is not a 32-byte hex private key.`);
  }
  return '0x' + hex.toLowerCase();
}

export function loadConfig(
  env: Env,
  readMounted: MountedFileReader = readSecretFile,
): EthereumServiceConfig {
  const internalKey = value(env, 'INTERNAL_KEY');
  if (internalKey === '') {
    throw new ConfigError(
      'INTERNAL_KEY_MISSING',
      'INTERNAL_KEY is not set — the Ethereum service does not start without its internal key (05 §3.4, 08 §3.7.2).',
    );
  }
  if (internalKey.length < MIN_INTERNAL_KEY_LENGTH) {
    throw new ConfigError(
      'INTERNAL_KEY_TOO_SHORT',
      `INTERNAL_KEY must be at least ${MIN_INTERNAL_KEY_LENGTH} characters (openssl rand -hex 32).`,
    );
  }

  const chainRaw = value(env, 'ETHEREUM_NETWORK') || 'sepolia';
  if (!(chainRaw in ETHEREUM_CHAINS)) {
    throw new ConfigError(
      'INVALID_CONFIG',
      `ETHEREUM_NETWORK must be one of ${Object.keys(ETHEREUM_CHAINS).join(', ')}, got "${chainRaw}".`,
    );
  }
  const chain = chainRaw as EthereumChainName;

  const mnemonicPath = value(env, 'ETHEREUM_HD_MNEMONIC_FILE') || DEFAULT_SECRET_PATHS.hdMnemonic;
  const hdMnemonic = readMounted(mnemonicPath);
  if (hdMnemonic === null) {
    throw new ConfigError(
      'HD_WALLET_NOT_CONFIGURED',
      `The Ethereum seed secret is missing or empty (${mnemonicPath}) — the service does not start without its own seed (11 §F8 K7).`,
    );
  }

  const hotWalletKey = signingKey(
    readMounted(
      value(env, 'ETHEREUM_HOT_WALLET_PRIVATE_KEY_FILE') || DEFAULT_SECRET_PATHS.hotWalletKey,
    ),
    'The hot wallet key secret',
  );
  const fuelWalletKey = signingKey(
    readMounted(
      value(env, 'ETHEREUM_FUEL_WALLET_PRIVATE_KEY_FILE') || DEFAULT_SECRET_PATHS.fuelWalletKey,
    ),
    'The fuel wallet key secret',
  );

  const primary = providerFrom(env, 'PRIMARY', { maxBlockRange: 10 });
  if (primary === null) {
    throw new ConfigError(
      'RPC_NOT_CONFIGURED',
      'ETHEREUM_RPC_PRIMARY_URL is not set — the service needs at least its primary RPC provider (08 §3.7.1).',
    );
  }
  const backup = providerFrom(env, 'BACKUP', { maxBlockRange: 100 });
  if (backup !== null && backup.name === primary.name) {
    throw new ConfigError(
      'INVALID_CONFIG',
      'ETHEREUM_RPC_PRIMARY_NAME and ETHEREUM_RPC_BACKUP_NAME must differ (they label the metrics).',
    );
  }

  const usdt = optionalAddress(env, 'ETHEREUM_USDT_CONTRACT');
  const usdc = optionalAddress(env, 'ETHEREUM_USDC_CONTRACT');
  const onMainnet = chain === 'mainnet';
  const tokenContracts = {
    USDT: usdt ?? (onMainnet ? canonicalAddress(MAINNET_TOKEN_CONTRACTS.USDT) : ''),
    USDC: usdc ?? (onMainnet ? canonicalAddress(MAINNET_TOKEN_CONTRACTS.USDC) : ''),
  };

  return {
    port: listenPort(env),
    nodeEnv: value(env, 'NODE_ENV') || 'development',
    internalKey,
    webhookHmacKey: value(env, 'WEBHOOK_SECRET'),
    backendUrl: value(env, 'BACKEND_URL') || 'http://skinora-backend:5000',
    chain,
    chainId: ETHEREUM_CHAINS[chain].chainId,
    hdMnemonic,
    hotWalletKey,
    fuelWalletKey,
    hotWalletAddress: optionalAddress(env, 'ETHEREUM_HOT_WALLET_ADDRESS'),
    coldWalletAddress: optionalAddress(env, 'ETHEREUM_COLD_WALLET_ADDRESS'),
    tokenContracts,
    rpc: {
      providers: backup ? [primary, backup] : [primary],
      timeoutMs: positiveInt(env, 'ETHEREUM_RPC_TIMEOUT_MS', 8000),
      failoverCooldownMs: positiveInt(env, 'ETHEREUM_RPC_FAILOVER_COOLDOWN_MS', 30_000),
    },
    finality: {
      pollIntervalMs: positiveInt(env, 'ETHEREUM_FINALIZED_POLL_INTERVAL_MS', 30_000),
      stallThresholdSeconds: positiveInt(env, 'ETHEREUM_FINALIZED_STALL_SECONDS', 1800),
    },
    shutdownTimeoutMs: 10_000,
  };
}

/** Host of a provider URL — what logs may show (the path and query carry the API key). */
export function providerHost(url: string): string {
  try {
    return new URL(url).host;
  } catch {
    return '(invalid url)';
  }
}
