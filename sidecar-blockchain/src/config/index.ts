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

/**
 * STAKE_ACCOUNT_PERMISSION_ID as a number. Unset or blank is the documented
 * default, 2 — the first id the chain assigns to an added active permission.
 *
 * The previous `parseInt(env ?? '2')` turned a blank value (a
 * `STAKE_ACCOUNT_PERMISSION_ID=` line outside compose, which applies its own
 * `:-2`) into NaN and read `2abc` as 2. Anything but a plain integer is NaN
 * here, which the delegation flow refuses at construction while a stake
 * account is configured (#325 validation).
 */
/**
 * T170 — a positive whole number from the environment, or the default. An
 * unparsable or non-positive value falls back rather than publishing NaN (a
 * NaN threshold makes the alert's division return nothing — a rule that never
 * fires) or polling in a tight loop.
 */
export function positiveInt(raw: string | undefined, fallback: number): number {
  const value = Number(raw);
  return raw !== undefined && raw.trim() !== '' && Number.isInteger(value) && value > 0
    ? value
    : fallback;
}

export function parseStakePermissionId(raw: string | undefined): number {
  const trimmed = (raw ?? '').trim();
  if (trimmed === '') return 2;
  return /^\d+$/.test(trimmed) ? Number(trimmed) : Number.NaN;
}

/**
 * ENERGY_STAKING_ENABLED as a boolean — T149 (11 §F8, öneriyle kaydedildi — ⚠).
 * The hybrid Energy-stake model (delegation from a stake, 2026-09-16) is
 * superseded by K4/K8: on TRON the buyer pays the sweep as a fee line and the
 * seller the payout, so the platform no longer needs to stake. Its code stays,
 * dormant, behind this switch. Unset or blank is the default, OFF. Only the
 * literal words are accepted: a typo ("ture", "1", "yes") would otherwise flip
 * a money path silently in one direction or the other, so it stops the
 * sidecar at startup instead.
 */
export function parseStakingEnabled(raw: string | undefined): boolean {
  const trimmed = (raw ?? '').trim().toLowerCase();
  if (trimmed === '' || trimmed === 'false') return false;
  if (trimmed === 'true') return true;
  throw new Error(
    `ENERGY_STAKING_ENABLED must be 'true' or 'false' (blank = false); got '${raw}'.`,
  );
}

export const config = {
  port: parseInt(process.env.PORT || '5200', 10),
  nodeEnv: process.env.NODE_ENV || 'development',

  // Backend communication
  backendUrl: process.env.BACKEND_URL || 'http://skinora-backend:5000',
  // T171a — the internal key (X-Internal-Key, 05 §3.4) and the webhook HMAC
  // secret are NOT read here: they come from their Docker secret files
  // (config/sharedSecrets.ts). The entry point loads them, refuses to start
  // without them, and hands the webhook secret to WebhookClient.

  // Tron network
  tronNetwork: network,
  tronFullNodeUrl: process.env.TRON_FULL_NODE_URL || networkUrls.fullNodeUrl,
  tronSolidityUrl: process.env.TRON_SOLIDITY_URL || networkUrls.solidityUrl,
  tronEventUrl: process.env.TRON_EVENT_URL || networkUrls.eventUrl,
  // T171b — the TronGrid API keys are NOT read here: they come from their
  // Docker secret files (config/providerCredentials.ts); the entry point hands
  // them to TronGridClient (configureTronApiKeys) and the other clients.

  // Token contracts — 08 §3.3
  usdtContract: TOKEN_CONTRACTS[network].USDT,
  usdcContract: TOKEN_CONTRACTS[network].USDC,
  tokenDecimals: 6,

  // Signing material — the HD seed (08 §3.2, m/44'/195'/0'/0/{index} from the
  // BIP-32 master node, empty BIP-39 passphrase) and the hot wallet key — is
  // NOT part of this object (T163). It is read once at startup from its Docker
  // secret files by config/signingSecrets.ts and handed to the services that
  // sign; nothing that logs or serialises `config` can reach it.
  //
  // Hot wallet — operationally distinct from HD wallet master mnemonic
  // (05 §3.3 + 05 §3.5). Sidecar refuses to broadcast SELLER_PAYOUT transfers
  // when the key is unset, but otherwise starts so the rest of the API surface
  // (monitor, derive) remains usable. In MVP the hot wallet doubles as the
  // sweeper account that issues `delegateresource` to deposit addresses (T74
  // scope decision 2026-05-17). When the key is set, this address must be the
  // one it controls (HOT_WALLET_ADDRESS_MISMATCH, T163).
  hotWalletAddress: process.env.HOT_WALLET_ADDRESS || '',

  // Platform-owned destinations and value ceilings — owner decisions
  // 2026-09-16/17 (05 §3.3). The cold wallet address lives here, not in an
  // admin-editable SystemSetting, because the sidecar is the last component
  // that can refuse to sign; the same reasoning pins the sweep destination to
  // `hotWalletAddress` above. Both limits are decimal USDT strings and both
  // are fail-closed: unset or malformed refuses customer-facing transfers
  // rather than allowing unlimited ones (transfer/TransferGuard.ts).
  coldWalletAddress: process.env.COLD_WALLET_ADDRESS || '',
  maxSingleTransferUsdt: process.env.MAX_SINGLE_TRANSFER_USDT || '',
  maxDailyOutflowUsdt: process.env.MAX_DAILY_OUTFLOW_USDT || '',
  // T172b (proje sahibi kararı 2026-10-23) — refunds leave a deposit, not the
  // hot wallet, so the cap above never saw them: the signer's own 24-hour cap
  // on them, decimal USDT (USDC 1:1). Fail-closed like the two above — unset
  // or malformed and no deposit refund is signed (it waits). Its ledger lives
  // on the service's volume so a restart cannot reset the window
  // (transfer/DepositRefundLimit.ts); unset dir = no ledger = refunds wait.
  depositRefundDailyLimitUsdt: process.env.DEPOSIT_REFUND_DAILY_LIMIT_USDT || '',
  depositRefundLedgerDir: process.env.DEPOSIT_REFUND_LEDGER_DIR || '',

  // Dedicated stake account (05 §3.3, owner decision 2026-09-17). The frozen
  // TRX lives here and its owner key stays offline; the hot wallet's key is
  // listed in an active permission that allows Energy delegation and reclaim
  // and nothing else, so a stolen hot key cannot unstake or move the TRX.
  // Empty = the hot wallet holds its own stake (the pre-split arrangement).
  // A malformed address or permission id stops the sidecar at startup
  // (wallet/EnergyDelegationService.ts, owner decision 2026-09-19).
  // T149 — the hybrid stake is dormant: OFF by default. While off the flow
  // never reads a stake (no getcandelegatedmaxsize), never delegates, and the
  // two STAKE_ACCOUNT_* values below are ignored; every deposit transfer burns.
  energyStakingEnabled: parseStakingEnabled(process.env.ENERGY_STAKING_ENABLED),
  stakeAccountAddress: process.env.STAKE_ACCOUNT_ADDRESS || '',
  stakeAccountPermissionId: parseStakePermissionId(process.env.STAKE_ACCOUNT_PERMISSION_ID),

  // Deposit-sourced transfer resources — 08 §3.3, owner decision 2026-09-16
  // (HYBRID). There is no delegation AMOUNT to configure any more: the flow
  // simulates each transfer, reads the network ratio (TotalEnergyLimit /
  // TotalEnergyWeight — measured mainnet ~9.5, Nile ~73.7 Energy per staked
  // TRX) and delegates the whole transfer's shortfall when the stake can cover
  // all of it, or burns otherwise (wallet/DelegationPlanner.ts). The fixed
  // 200 TRX this replaced bought ~3% of a mainnet sweep and, once the hot
  // wallet staked, would have made sweeps fail rather than fall back.
  //
  // What remains is the TRX sent when the plan itself cannot be computed
  // (probe outage) — sized for the most expensive transfer (130,285 Energy ×
  // 100 SUN = 13.03 TRX) plus its Bandwidth (0.35 TRX), in SUN. Activation is
  // not in it: the sweeper pays that when it sends the deposit its first SUN.
  sweepTrxFallbackSun: parseInt(process.env.SWEEP_TRX_FALLBACK_SUN || '15000000', 10),

  // Logging
  lokiUrl: process.env.LOKI_URL || 'http://skinora-loki:3100',
  logLevel: process.env.LOG_LEVEL || 'info',

  // Rate limiting — 08 §3.1 (TronGrid plan-based)
  tronGridRequestsPerSecond: parseInt(process.env.TRONGRID_RPS || '10', 10),
  // The TronGrid plan's daily request budget. Published as a gauge so the
  // tron-quota-projection alert can compare the measured request rate with it.
  // TronGrid does not return the budget on responses (2026-08-29) and its docs
  // no longer publish one (2026-09-08); 100,000 is the free tier as reported in
  // 2026 — set the real plan's value (DEPLOY_RUNBOOK §B).
  tronGridDailyRequestBudget: parseInt(process.env.TRONGRID_DAILY_REQUEST_BUDGET || '100000', 10),

  // TronGrid read-path resilience — 08 §3.5 / §3.6 (WP10). On a 429 /
  // key-suspension (403) the TronGridClient fails over to the secondary
  // API key immediately (separate rate-limit pool), then applies a short,
  // bounded exponential backoff. The schedule is intentionally kept well
  // under the payment-polling interval so a single stalled request never
  // blocks the whole monitor tick — the loop re-polls naturally every
  // `paymentPollingIntervalMs`. 5xx provider errors reuse the same bounded
  // retry without rotating keys (the key is fine; the provider is degraded).
  tronGridMaxRetries: parseInt(process.env.TRONGRID_MAX_RETRIES || '3', 10),
  tronGridRetryBackoffBaseMs: parseInt(process.env.TRONGRID_RETRY_BACKOFF_BASE_MS || '250', 10),
  tronGridRetryBackoffCapMs: parseInt(process.env.TRONGRID_RETRY_BACKOFF_CAP_MS || '2000', 10),

  // T170 — chain progress (SolidBlockTracker, alert tron-solid-block-stalled).
  // One solid-head read per interval (60 s = 1,440 TronGrid requests a day).
  // Stall threshold 300 s: the solid head normally trails the chain by ~60 s
  // (19 blocks × 3 s); five times that is an outage, not a slow block
  // (öneriyle kaydedildi — ⚠, DEPLOY_RUNBOOK §P.7).
  tronSolidBlockPollIntervalMs: positiveInt(process.env.TRON_SOLID_BLOCK_POLL_INTERVAL_MS, 60000),
  tronSolidBlockStallSeconds: positiveInt(process.env.TRON_SOLID_BLOCK_STALL_SECONDS, 300),

  // Outbound TRC-20 transfer fee cap (08 §3.3, WP10). Previously a hardcoded
  // 100 TRX magic number in TronTransferClient; now configurable so an
  // operator can tune the broadcast fee ceiling without a code change. In
  // SUN (1 TRX = 1_000_000 SUN). Per-request `feeLimitSun` still overrides.
  transferFeeLimitSun: parseInt(process.env.TRANSFER_FEE_LIMIT_SUN || '100000000', 10),

  // Monitoring intervals (seconds)
  paymentPollingIntervalMs: parseInt(process.env.PAYMENT_POLLING_INTERVAL_MS || '3000', 10), // 05 §3.3 — 3 second active monitoring
  // T139-ActiveMonitorQuotaAlarm (owner decision 2026-10-02): once the payment
  // is confirmed the address is only watched for a late second payment or an
  // overpayment until the sweep (08 §3.4), and polling it every 3 s for the
  // ~8-day settlement window cost 57,600 TronGrid requests a day per address.
  paymentHoldingPollingIntervalMs: parseInt(
    process.env.PAYMENT_HOLDING_POLLING_INTERVAL_MS || '900000',
    10,
  ),
  minConfirmations: parseInt(process.env.MIN_CONFIRMATIONS || '20', 10), // 05 §3.3 — 20 blocks (~60s)
  monitorPageLimit: parseInt(process.env.MONITOR_PAGE_LIMIT || '20', 10), // 08 §3.4

  // Post-cancel monitoring cadences — 08 §3.4 / 06 §2.16 (T75). All in ms.
  // Defaults match the spec verbatim (30 s / 5 min / 1 h); admin tuning via
  // SystemSetting is sidecar-restart-bound (forward-deferred to T96).
  postCancelTickIntervalMs: parseInt(process.env.POST_CANCEL_TICK_INTERVAL_MS || '30000', 10),
  postCancelCadence24hMs: parseInt(process.env.POST_CANCEL_CADENCE_24H_MS || '30000', 10),
  postCancelCadence7dMs: parseInt(process.env.POST_CANCEL_CADENCE_7D_MS || '300000', 10),
  postCancelCadence30dMs: parseInt(process.env.POST_CANCEL_CADENCE_30D_MS || '3600000', 10),
  postCancelWindow24hMs: parseInt(
    process.env.POST_CANCEL_WINDOW_24H_MS || String(24 * 60 * 60 * 1000),
    10,
  ),
  postCancelWindow7dMs: parseInt(
    process.env.POST_CANCEL_WINDOW_7D_MS || String(7 * 24 * 60 * 60 * 1000),
    10,
  ),
  postCancelWindow30dMs: parseInt(
    process.env.POST_CANCEL_WINDOW_30D_MS || String(30 * 24 * 60 * 60 * 1000),
    10,
  ),

  // Webhook callback endpoints — match BlockchainWebhooksController routes
  webhookEndpoints: {
    paymentDetected: '/api/v1/webhooks/blockchain/payment-detected',
    paymentConfirmed: '/api/v1/webhooks/blockchain/payment-confirmed',
    wrongTokenIncoming: '/api/v1/webhooks/blockchain/wrong-token',
    spamTokenIncoming: '/api/v1/webhooks/blockchain/spam-token',
    latePaymentDetected: '/api/v1/webhooks/blockchain/late-payment-detected',
    postCancelMonitorStateChanged: '/api/v1/webhooks/blockchain/post-cancel-monitor-state-changed',
  },

  // Supported stablecoin allowlist (08 §3.4 wrong-token classification)
  allowlist: {
    USDT: TOKEN_CONTRACTS[network].USDT,
    USDC: TOKEN_CONTRACTS[network].USDC,
  },

  // Graceful shutdown
  shutdownTimeoutMs: 10_000,
} as const;
