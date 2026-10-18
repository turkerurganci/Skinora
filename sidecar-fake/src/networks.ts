import { config } from './config.js';
import {
  fakeEthereumAddress,
  fakeEthereumTxHash,
  fakeTronAddress,
  fakeTxHash,
  toChecksumAddress,
} from './ids.js';

/**
 * T155 — the two payment networks the fake stands in for (11 §F8 K1). Every
 * difference between them that the backend can observe lives in one profile
 * here, so a route never asks "which chain am I?" in more than one place:
 * address and hash formats, the HD path, the token contracts a payment arrives
 * from, which webhook secret signs, how a final transfer is reported.
 *
 * The backend reaches each network's service at its own address (T148 —
 * `BlockchainSidecar__BaseUrl` for TRON, `EthereumSidecar__BaseUrl` for
 * Ethereum); the fake answers each on its own port with a router bound to one
 * profile (app.ts), so a call that reaches the wrong network's surface is
 * visible — and refused — exactly as the real services refuse it.
 */
export type Network = 'TRON' | 'ETHEREUM';

export const NETWORKS: readonly Network[] = ['TRON', 'ETHEREUM'];

export type StablecoinSymbol = 'USDT' | 'USDC';

export function isNetwork(value: unknown): value is Network {
  return value === 'TRON' || value === 'ETHEREUM';
}

/** What a final outgoing transfer looks like on `GET /api/transfer/status/:txHash`. */
export type TransferStatusBody = Record<string, unknown>;

export interface NetworkProfile {
  readonly network: Network;
  /** Shared secret this network's service signs webhooks with (05 §3.4, T148). */
  webhookSecret(): string;
  /** Deposit address for an HD index, in the form the real service's `derive` answers. */
  depositAddress(index: number): string;
  derivationPath(index: number): string;
  /** Deterministic transaction hash in this network's format. */
  txHash(seed: string): string;
  isTxHash(value: string): boolean;
  /**
   * Address check as the real service makes it. TRON: shape only (the e2e
   * platform wallets are placeholders). Ethereum: `0x` + 40 hex, and a
   * mixed-case value must carry a valid EIP-55 checksum.
   */
  isAddress(value: string): boolean;
  /** Address equality as the network defines it (EVM: case-insensitive). */
  sameAddress(a: string, b: string): boolean;
  /** Allowlisted token contracts, as the service reports them in webhooks. */
  contracts(): Record<StablecoinSymbol, string>;
  /**
   * The subset of {@link contracts} the service is configured to ACCEPT in
   * `monitor/start`. The real Ethereum service rejects an unconfigured token
   * (K2: USDT only); `contracts()` still names USDC so a wrong-token payment
   * lever can send one.
   */
  allowlist(): Partial<Record<StablecoinSymbol, string>>;
  /** A token contract that is NOT allowlisted (spam-token lever, 03 §5.3a). */
  readonly unsupportedContract: string;
  /** The wallet a simulated buyer pays FROM — never a refund target (K9). */
  readonly buyerWallet: string;
  /** `confirmationCount` reported with `payment.confirmed`. */
  readonly paymentConfirmationCount: number;
  /** Block height reported on webhooks and final transfers. */
  readonly blockNumber: number;
  /**
   * TRON accepts a POST body without `network` (runbook calls predating T148);
   * Ethereum refuses it (T150 iv). Both refuse a body naming another network.
   */
  readonly networkFieldRequired: boolean;
  /** The status body of a transfer this network considers final. */
  finalTransferStatus(txHash: string): TransferStatusBody;
}

const TRON_CONTRACTS: Record<StablecoinSymbol, string> = {
  // Mirror of the backend's StablecoinContractOptions TRON defaults (mainnet).
  USDT: 'TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t',
  USDC: 'TEkxiTehnzSmSe2XqrBj4w32RUN966rdz8',
};

// Ethereum mainnet USDT / USDC (11 §F8 K1, T141 karar d). Lower-cased: the
// backend stores and compares EVM addresses lower-case (06 §1.4), and the
// real service's webhook examples carry them that way
// (sidecar-ethereum/contracts/webhook/*.json).
const ETHEREUM_MAINNET_CONTRACTS: Record<StablecoinSymbol, string> = {
  USDT: '0xdac17f958d2ee523a2206206994597c13d831ec7',
  USDC: '0xa0b86991c6218b36c1d19d4a2e9eb0ce3606eb48',
};

const TRON_ADDRESS = /^T[1-9A-HJ-NP-Za-km-z]{33}$/;
const TRON_TX_HASH = /^[0-9a-f]{64}$/;
const EVM_ADDRESS = /^0x[0-9a-fA-F]{40}$/;
const EVM_TX_HASH = /^0x[0-9a-f]{64}$/;

/** Mirror of sidecar-ethereum `checksummedEvmAddress`: mixed case must be EIP-55 valid. */
export function isEvmAddress(value: string): boolean {
  if (!EVM_ADDRESS.test(value)) return false;
  const hex = value.slice(2);
  const mixedCase = hex !== hex.toLowerCase() && hex !== hex.toUpperCase();
  return !mixedCase || toChecksumAddress(value) === value;
}

export const TRON: NetworkProfile = {
  network: 'TRON',
  webhookSecret: () => config.blockchainWebhookSecret,
  depositAddress: (index) => fakeTronAddress(index),
  derivationPath: (index) => `m/44'/195'/0'/0/${index}`,
  txHash: (seed) => fakeTxHash(seed),
  isTxHash: (value) => TRON_TX_HASH.test(value),
  // Base58 alphabet + prefix + length, no checksum: the e2e stack's platform
  // wallets are readable placeholders (`TE2eHotWa11et…`) that would fail
  // base58check. Shape is what keeps a 0x address out of a TRON call, which is
  // the mix-up worth catching here.
  isAddress: (value) => TRON_ADDRESS.test(value),
  sameAddress: (a, b) => a === b,
  contracts: () => TRON_CONTRACTS,
  allowlist: () => TRON_CONTRACTS,
  unsupportedContract: fakeTronAddress(666_001),
  buyerWallet: fakeTronAddress(999_001),
  // 20 = the TRON finality threshold the backend's CONFIRMED CHECK requires.
  paymentConfirmationCount: 20,
  blockNumber: 60_000_000,
  networkFieldRequired: false,
  // Block-count finality: >= 20 confirmations + SUCCESS = confirmed (05 §3.3).
  finalTransferStatus: (txHash) => ({
    txHash,
    blockNumber: 60_000_000,
    contractRet: 'SUCCESS',
    confirmations: config.transferConfirmations,
  }),
};

export const ETHEREUM: NetworkProfile = {
  network: 'ETHEREUM',
  webhookSecret: () => config.ethereumWebhookSecret,
  depositAddress: (index) => fakeEthereumAddress(index),
  // K7 — Ethereum's own seed, standard path from the master node (08 §3.7.2).
  derivationPath: (index) => `m/44'/60'/0'/0/${index}`,
  txHash: (seed) => fakeEthereumTxHash(seed),
  isTxHash: (value) => EVM_TX_HASH.test(value),
  isAddress: isEvmAddress,
  sameAddress: (a, b) => a.toLowerCase() === b.toLowerCase(),
  contracts: () => ({
    USDT: (config.ethereumUsdtContract || ETHEREUM_MAINNET_CONTRACTS.USDT).toLowerCase(),
    USDC: (config.ethereumUsdcContract || ETHEREUM_MAINNET_CONTRACTS.USDC).toLowerCase(),
  }),
  allowlist: () => {
    const all = ETHEREUM.contracts();
    // USDC is accepted only when explicitly configured (K2 — USDT only).
    return config.ethereumUsdcContract ? all : { USDT: all.USDT };
  },
  unsupportedContract: fakeEthereumAddress(666_001).toLowerCase(),
  buyerWallet: fakeEthereumAddress(999_001).toLowerCase(),
  // Informational on a finalized network (the backend stores what is
  // reported); 64 = two epochs, the value the real service's example carries.
  paymentConfirmationCount: 64,
  blockNumber: 21_000_000,
  networkFieldRequired: true,
  // K5 — final means `finalized: true`; a block count alone never qualifies,
  // so the fake reports none. `realizedFeeWei` is a decimal string (T154 —
  // wei passes 2^53).
  finalTransferStatus: (txHash) => ({
    txHash,
    blockNumber: 21_000_000,
    contractRet: 'SUCCESS',
    confirmations: null,
    finalized: true,
    realizedFeeWei: '21000000000000',
  }),
};

export function profileFor(network: Network): NetworkProfile {
  return network === 'ETHEREUM' ? ETHEREUM : TRON;
}

/** The allowlisted symbol whose contract on this network is `contract`, if any. */
export function symbolForContract(
  profile: NetworkProfile,
  contract: string,
): StablecoinSymbol | null {
  const contracts = profile.allowlist();
  for (const symbol of Object.keys(contracts) as StablecoinSymbol[]) {
    const configured = contracts[symbol];
    if (configured && profile.sameAddress(configured, contract)) return symbol;
  }
  return null;
}

/**
 * The key that signs a webhook about a deposit on `depositNetwork`: that
 * network's own (T148 - the key that verifies decides which network the caller
 * IS). `signAs` overrides it for the wrong-network negative levers.
 */
export function signingKeyFor(depositNetwork: Network, signAs?: Network): string {
  return profileFor(signAs ?? depositNetwork).webhookSecret();
}
