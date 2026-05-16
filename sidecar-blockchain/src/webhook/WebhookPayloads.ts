/**
 * Webhook envelopes sent from the blockchain sidecar to the .NET backend
 * (05 §3.3, 08 §3.4). Each payload carries `event`, `timestamp`, and a
 * `data` block whose shape is event-specific. The outer signature
 * (X-Signature / X-Timestamp / X-Nonce) is added by `WebhookClient` at
 * send time and verified by `WebhookSignatureMiddleware` on the backend
 * (05 §3.4, 09 §11.3).
 */

export const BlockchainWebhookEvents = {
  PaymentDetected: 'payment.detected',
  PaymentConfirmed: 'payment.confirmed',
  WrongTokenIncoming: 'payment.wrong_token',
  SpamTokenIncoming: 'payment.spam_token',
} as const;

export type BlockchainWebhookEvent =
  (typeof BlockchainWebhookEvents)[keyof typeof BlockchainWebhookEvents];

export interface BlockchainWebhookEnvelope<TData> {
  event: BlockchainWebhookEvent;
  timestamp: string;
  data: TData;
}

/**
 * Phase 1 first sighting — the deposit address has received a Transfer from
 * the expected token contract. Backend persists a `BlockchainTransaction`
 * row with `Status=DETECTED` (06 §3.8).
 */
export interface PaymentDetectedData {
  paymentAddressId: string;
  transactionId: string;
  txHash: string;
  fromAddress: string;
  toAddress: string;
  contractAddress: string;
  tokenSymbol: 'USDT' | 'USDC';
  /** Decimal string with 6 fraction digits — e.g. "100.500000". */
  amount: string;
  blockTimestampMs: number;
  detectedAt: string;
}

/**
 * Finality reached — `currentSolidBlock - txBlock >= 20`. Backend flips
 * the row to `Status=CONFIRMED` and persists `BlockNumber` / `ConfirmedAt`.
 * State machine advancement (`PAYMENT_RECEIVED`) and amount validation are
 * forward-deferred to T72 — this event only records the confirmation fact.
 */
export interface PaymentConfirmedData {
  paymentAddressId: string;
  transactionId: string;
  txHash: string;
  blockNumber: number;
  confirmationCount: number;
  confirmedAt: string;
}

/**
 * Phase 2 hit — the deposit address received a supported stablecoin that is
 * different from the one the buyer was billed for. Backend records a
 * `WRONG_TOKEN_INCOMING` row and queues a refund (T72/T73).
 */
export interface WrongTokenIncomingData {
  paymentAddressId: string;
  transactionId: string;
  txHash: string;
  fromAddress: string;
  toAddress: string;
  expectedContractAddress: string;
  actualContractAddress: string;
  actualTokenSymbol: 'USDT' | 'USDC';
  amount: string;
  blockTimestampMs: number;
  detectedAt: string;
}

/**
 * Phase 2 hit — the deposit address received an unsupported token. Backend
 * records a `SPAM_TOKEN_INCOMING` row at terminal `CONFIRMED` (06 §3.8)
 * and does **not** attempt a refund (08 §3.4 spam policy).
 */
export interface SpamTokenIncomingData {
  paymentAddressId: string;
  transactionId: string;
  txHash: string;
  fromAddress: string;
  toAddress: string;
  expectedContractAddress: string;
  actualContractAddress: string;
  amount: string;
  blockTimestampMs: number;
  detectedAt: string;
}

export type AnyBlockchainWebhookPayload =
  | BlockchainWebhookEnvelope<PaymentDetectedData>
  | BlockchainWebhookEnvelope<PaymentConfirmedData>
  | BlockchainWebhookEnvelope<WrongTokenIncomingData>
  | BlockchainWebhookEnvelope<SpamTokenIncomingData>;
