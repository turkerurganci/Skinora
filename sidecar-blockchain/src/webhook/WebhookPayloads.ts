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
  LatePaymentDetected: 'payment.late_detected',
  PostCancelMonitorStateChanged: 'monitor.post_cancel_state_changed',
} as const;

export const PostCancelMonitorStates = {
  PostCancel24h: 'POST_CANCEL_24H',
  PostCancel7d: 'POST_CANCEL_7D',
  PostCancel30d: 'POST_CANCEL_30D',
  Stopped: 'STOPPED',
} as const;

export type PostCancelMonitorState =
  (typeof PostCancelMonitorStates)[keyof typeof PostCancelMonitorStates];

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
  /**
   * On-chain log index of this Transfer event within `txHash` (08 §3.4 — WP10).
   * Together with `txHash` it forms the per-event dedup key on the backend
   * (`BlockchainTransaction (TxHash, EventIndex)` UNIQUE). The common
   * single-transfer transaction reports `0`.
   */
  eventIndex: number;
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
  /** On-chain log index — matches the DETECTED row's `(txHash, eventIndex)` (08 §3.4 — WP10). */
  eventIndex: number;
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
  /** On-chain log index of this Transfer event within `txHash` (08 §3.4 — WP10). */
  eventIndex: number;
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
  /** On-chain log index of this Transfer event within `txHash` (08 §3.4 — WP10). */
  eventIndex: number;
  fromAddress: string;
  toAddress: string;
  expectedContractAddress: string;
  actualContractAddress: string;
  amount: string;
  blockTimestampMs: number;
  detectedAt: string;
}

/**
 * Late payment detected at a cancelled transaction's deposit address (T75 —
 * 02 §4.4, 08 §3.4 gecikmeli ödeme). Carries the same transfer fields as
 * `PaymentDetectedData` plus the post-cancel state in which it was observed.
 * Backend persists a `BUYER_PAYMENT` row, computes net-of-gas refund, then
 * dispatches `LATE_PAYMENT_REFUND` via the existing T73 refund pipeline.
 *
 * The sidecar does NOT wait for 20-block finality before emitting — refund
 * decision is owned by the backend, which may apply its own confirmation
 * policy or proceed optimistically depending on cadence.
 */
export interface LatePaymentDetectedData {
  paymentAddressId: string;
  transactionId: string;
  txHash: string;
  /** On-chain log index of this Transfer event within `txHash` (08 §3.4 — WP10). */
  eventIndex: number;
  fromAddress: string;
  toAddress: string;
  contractAddress: string;
  tokenSymbol: 'USDT' | 'USDC';
  /** Decimal string with 6 fraction digits — e.g. "100.500000". */
  amount: string;
  blockTimestampMs: number;
  detectedAt: string;
  /** Post-cancel state at the moment of detection. */
  monitorState: PostCancelMonitorState;
}

/**
 * Post-cancel monitor advanced to the next state (T75). Emitted on every
 * cadence boundary (24h → 7d, 7d → 30d, 30d → STOPPED). Backend mirrors
 * `PaymentAddress.MonitoringStatus` and `MonitoringExpiresAt`, and on the
 * `STOPPED` terminal raises the admin alert (05 §3.3).
 */
export interface PostCancelMonitorStateChangedData {
  paymentAddressId: string;
  transactionId: string;
  address: string;
  previousState: PostCancelMonitorState;
  newState: PostCancelMonitorState;
  /** ISO timestamp when the new state's window ends (null for STOPPED). */
  newStateExpiresAt: string | null;
  /** Stable cancellation origin — drives the 24h/7d/30d wall-clock. */
  cancelledAt: string;
  changedAt: string;
}

export type AnyBlockchainWebhookPayload =
  | BlockchainWebhookEnvelope<PaymentDetectedData>
  | BlockchainWebhookEnvelope<PaymentConfirmedData>
  | BlockchainWebhookEnvelope<WrongTokenIncomingData>
  | BlockchainWebhookEnvelope<SpamTokenIncomingData>
  | BlockchainWebhookEnvelope<LatePaymentDetectedData>
  | BlockchainWebhookEnvelope<PostCancelMonitorStateChangedData>;
