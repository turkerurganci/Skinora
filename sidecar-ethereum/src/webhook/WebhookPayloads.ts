import { SERVICE_NETWORK } from '../network.js';

/**
 * Webhook envelopes from the Ethereum service to the backend (T151 —
 * 05 §3.3a "Webhook yükü", 08 §3.7.4). Same events, same paths and same
 * `data` shapes as the TRON service (`sidecar-blockchain/src/webhook/
 * WebhookPayloads.ts`) — the backend has one controller for both networks and
 * tells them apart by `network`, by the secret that signed the request and by
 * the deposit's `PaymentAddress.Network` (T148). The examples in
 * `contracts/webhook/` pin the wire shape on both sides.
 */

export const BlockchainWebhookEvents = {
  PaymentDetected: 'payment.detected',
  PaymentConfirmed: 'payment.confirmed',
  WrongTokenIncoming: 'payment.wrong_token',
  SpamTokenIncoming: 'payment.spam_token',
  LatePaymentDetected: 'payment.late_detected',
  PostCancelMonitorStateChanged: 'monitor.post_cancel_state_changed',
} as const;

export type BlockchainWebhookEvent =
  (typeof BlockchainWebhookEvents)[keyof typeof BlockchainWebhookEvents];

export const WEBHOOK_ENDPOINTS: Readonly<Record<BlockchainWebhookEvent, string>> = {
  'payment.detected': '/api/v1/webhooks/blockchain/payment-detected',
  'payment.confirmed': '/api/v1/webhooks/blockchain/payment-confirmed',
  'payment.wrong_token': '/api/v1/webhooks/blockchain/wrong-token',
  'payment.spam_token': '/api/v1/webhooks/blockchain/spam-token',
  'payment.late_detected': '/api/v1/webhooks/blockchain/late-payment-detected',
  'monitor.post_cancel_state_changed':
    '/api/v1/webhooks/blockchain/post-cancel-monitor-state-changed',
};

export const PostCancelMonitorStates = {
  PostCancel24h: 'POST_CANCEL_24H',
  PostCancel7d: 'POST_CANCEL_7D',
  PostCancel30d: 'POST_CANCEL_30D',
  Stopped: 'STOPPED',
} as const;

export type PostCancelMonitorState =
  (typeof PostCancelMonitorStates)[keyof typeof PostCancelMonitorStates];

export interface BlockchainWebhookEnvelope<TData> {
  event: BlockchainWebhookEvent;
  network: typeof SERVICE_NETWORK;
  timestamp: string;
  data: TData;
}

/** The one place an envelope is built — pinned by `WebhookPayloads.test.ts` against `contracts/webhook`. */
export function buildWebhookEnvelope<TData>(
  event: BlockchainWebhookEvent,
  data: TData,
  now: Date,
): BlockchainWebhookEnvelope<TData> {
  return { event, network: SERVICE_NETWORK, timestamp: now.toISOString(), data };
}

/**
 * First sighting of a payment. On Ethereum it is only ever sent for a
 * transfer in a `finalized` block (K5) and is followed at once by
 * {@link PaymentConfirmedData} for the same `(txHash, eventIndex)`.
 */
export interface PaymentDetectedData {
  paymentAddressId: string;
  transactionId: string;
  txHash: string;
  /** The chain's own log index (08 §3.7.4 "Tekillik") — no guessing as on TRON. */
  eventIndex: number;
  fromAddress: string;
  toAddress: string;
  contractAddress: string;
  tokenSymbol: 'USDT' | 'USDC';
  /** Decimal string with 6 fraction digits — e.g. "20.400000". */
  amount: string;
  blockTimestampMs: number;
  detectedAt: string;
}

/**
 * The transfer's block is `finalized` (K5). `confirmationCount` is the number
 * of blocks the finalized head is past the transfer's block — recorded as is
 * (BlockchainNetworkPolicy.Ethereum, T148); it is not the acceptance rule.
 */
export interface PaymentConfirmedData {
  paymentAddressId: string;
  transactionId: string;
  txHash: string;
  eventIndex: number;
  blockNumber: number;
  confirmationCount: number;
  confirmedAt: string;
}

export interface WrongTokenIncomingData {
  paymentAddressId: string;
  transactionId: string;
  txHash: string;
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

export interface SpamTokenIncomingData {
  paymentAddressId: string;
  transactionId: string;
  txHash: string;
  eventIndex: number;
  fromAddress: string;
  toAddress: string;
  expectedContractAddress: string;
  actualContractAddress: string;
  amount: string;
  blockTimestampMs: number;
  detectedAt: string;
}

export interface LatePaymentDetectedData {
  paymentAddressId: string;
  transactionId: string;
  txHash: string;
  eventIndex: number;
  fromAddress: string;
  toAddress: string;
  contractAddress: string;
  tokenSymbol: 'USDT' | 'USDC';
  amount: string;
  blockTimestampMs: number;
  detectedAt: string;
  monitorState: PostCancelMonitorState;
}

export interface PostCancelMonitorStateChangedData {
  paymentAddressId: string;
  transactionId: string;
  address: string;
  previousState: PostCancelMonitorState;
  newState: PostCancelMonitorState;
  newStateExpiresAt: string | null;
  cancelledAt: string;
  changedAt: string;
}
