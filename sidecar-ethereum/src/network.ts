/**
 * The network this service watches and signs on (05 §3.3a "Servisler",
 * 11 §F8 K7). A constant, not configuration: an Ethereum service that could be
 * told it is TRON would accept requests meant for another chain's service.
 *
 * Every POST body names its network and a body naming another network — or
 * none — is refused (`NETWORK_MISMATCH`, api/middleware.ts); `/health` reports
 * it so the backend's probe can tell a mis-pointed service address apart.
 */
export const SERVICE_NETWORK = 'ETHEREUM' as const;

/** The Ethereum chains this service can be pointed at (08 §3.7.1). */
export const ETHEREUM_CHAINS = {
  mainnet: { chainId: 1 },
  sepolia: { chainId: 11155111 },
} as const;

export type EthereumChainName = keyof typeof ETHEREUM_CHAINS;

/**
 * USDT and USDC on Ethereum mainnet (08 §3.0, §3.7.4). Sepolia has no official
 * Tether token: T159 deploys a copy of the Tether contract and its address is
 * configured (ETHEREUM_USDT_CONTRACT); the Circle Sepolia USDC is the
 * wrong-token rehearsal token.
 */
export const MAINNET_TOKEN_CONTRACTS = {
  USDT: '0xdAC17F958D2ee523a2206206994597C13D831ec7',
  USDC: '0xA0b86991c6218b36c1d19D4a2e9Eb0cE3606eB48',
} as const;

/** Deposit path of index <c>i</c> (08 §3.7.2): m/44'/60'/0'/0/{i}, from the master node. */
export const ETHEREUM_COIN_TYPE = 60;
