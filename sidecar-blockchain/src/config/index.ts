// TronGrid endpoints — 08 §3.1
const TRON_NETWORKS = {
  mainnet: {
    fullNodeUrl: 'https://api.trongrid.io',
    solidityUrl: 'https://api.trongrid.io',
    eventUrl: 'https://api.trongrid.io',
  },
  shasta: {
    fullNodeUrl: 'https://api.shasta.trongrid.io',
    solidityUrl: 'https://api.shasta.trongrid.io',
    eventUrl: 'https://api.shasta.trongrid.io',
  },
  nile: {
    fullNodeUrl: 'https://nile.trongrid.io',
    solidityUrl: 'https://nile.trongrid.io',
    eventUrl: 'https://nile.trongrid.io',
  },
} as const;

type TronNetwork = keyof typeof TRON_NETWORKS;

function getTronNetwork(): TronNetwork {
  const env = (process.env.TRON_NETWORK || 'nile') as string;
  if (env in TRON_NETWORKS) return env as TronNetwork;
  throw new Error(
    `Invalid TRON_NETWORK: ${env}. Must be one of: ${Object.keys(TRON_NETWORKS).join(', ')}`,
  );
}

const network = getTronNetwork();
const networkUrls = TRON_NETWORKS[network];

// USDT/USDC contract addresses — 08 §3.3
const TOKEN_CONTRACTS = {
  mainnet: {
    USDT: 'TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t',
    USDC: 'TEkxiTehnzSmSe2XqrBj4w32RUN966rdz8',
  },
  // Testnet contracts — resolved from faucet at deploy time
  nile: {
    USDT: process.env.TRON_USDT_CONTRACT || '',
    USDC: process.env.TRON_USDC_CONTRACT || '',
  },
  shasta: {
    USDT: process.env.TRON_USDT_CONTRACT || '',
    USDC: process.env.TRON_USDC_CONTRACT || '',
  },
} as const;

export const config = {
  port: parseInt(process.env.PORT || '5200', 10),
  nodeEnv: process.env.NODE_ENV || 'development',

  // Backend communication
  backendUrl: process.env.BACKEND_URL || 'http://skinora-backend:5000',
  internalKey: process.env.INTERNAL_KEY || '',
  webhookSecret: process.env.WEBHOOK_SECRET || '',

  // Tron network
  tronNetwork: network,
  tronFullNodeUrl: process.env.TRON_FULL_NODE_URL || networkUrls.fullNodeUrl,
  tronSolidityUrl: process.env.TRON_SOLIDITY_URL || networkUrls.solidityUrl,
  tronEventUrl: process.env.TRON_EVENT_URL || networkUrls.eventUrl,
  tronApiKey: process.env.TRON_API_KEY || '',
  tronApiKeySecondary: process.env.TRON_API_KEY_SECONDARY || '',

  // Token contracts — 08 §3.3
  usdtContract: TOKEN_CONTRACTS[network].USDT,
  usdcContract: TOKEN_CONTRACTS[network].USDC,
  tokenDecimals: 6,

  // HD Wallet — 08 §3.2, derivation path: m/44'/195'/0'/0/{index}
  hdWalletMnemonic: process.env.HD_WALLET_MNEMONIC || '',

  // Hot wallet — operationally distinct from HD wallet master mnemonic
  // (05 §3.3 + 05 §3.5). Signing material is mounted as a Docker secret in
  // production. Sidecar refuses to broadcast SELLER_PAYOUT transfers when
  // the key is unset, but otherwise starts so the rest of the API surface
  // (monitor, derive) remains usable in development.
  hotWalletAddress: process.env.HOT_WALLET_ADDRESS || '',
  hotWalletPrivateKey: process.env.HOT_WALLET_PRIVATE_KEY || '',

  // Logging
  lokiUrl: process.env.LOKI_URL || 'http://skinora-loki:3100',
  logLevel: process.env.LOG_LEVEL || 'info',

  // Rate limiting — 08 §3.1 (TronGrid plan-based)
  tronGridRequestsPerSecond: parseInt(process.env.TRONGRID_RPS || '10', 10),

  // Monitoring intervals (seconds)
  paymentPollingIntervalMs: parseInt(process.env.PAYMENT_POLLING_INTERVAL_MS || '3000', 10), // 05 §3.3 — 3 second active monitoring
  minConfirmations: parseInt(process.env.MIN_CONFIRMATIONS || '20', 10), // 05 §3.3 — 20 blocks (~60s)
  monitorPageLimit: parseInt(process.env.MONITOR_PAGE_LIMIT || '20', 10), // 08 §3.4

  // Webhook callback endpoints — match BlockchainWebhooksController routes
  webhookEndpoints: {
    paymentDetected: '/api/v1/webhooks/blockchain/payment-detected',
    paymentConfirmed: '/api/v1/webhooks/blockchain/payment-confirmed',
    wrongTokenIncoming: '/api/v1/webhooks/blockchain/wrong-token',
    spamTokenIncoming: '/api/v1/webhooks/blockchain/spam-token',
  },

  // Supported stablecoin allowlist (08 §3.4 wrong-token classification)
  allowlist: {
    USDT: TOKEN_CONTRACTS[network].USDT,
    USDC: TOKEN_CONTRACTS[network].USDC,
  },

  // Graceful shutdown
  shutdownTimeoutMs: 10_000,
} as const;
