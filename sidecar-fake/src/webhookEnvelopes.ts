import type { DepositPaymentAddress } from './db.js';
import type { Network, NetworkProfile, StablecoinSymbol } from './networks.js';

/**
 * Inbound blockchain webhook envelopes (05 §3.4, 08 §3.4) — the payloads the
 * real services post to `/api/v1/webhooks/blockchain/*`. Pure builders so the
 * contract test can hold them against the services' own example bodies
 * (`sidecar-blockchain/contracts/webhook/`, `sidecar-ethereum/contracts/webhook/`)
 * — the same files the backend's webhook contract tests replay.
 *
 * Every value that differs by network comes from the deposit's profile: the
 * hash format, the payer's wallet, the token contracts, the confirmation
 * count. `network` is the envelope's claim; it defaults to the deposit's own
 * network and is overridable only so a test can send a webhook the backend
 * must refuse (T148 — 409 `WEBHOOK_NETWORK_MISMATCH`).
 */

export interface EnvelopeInput {
  profile: NetworkProfile;
  dep: DepositPaymentAddress;
  txHash: string;
  eventIndex: number;
  amount: string;
  /** The `network` the envelope claims (default: the profile's). */
  network?: Network;
  /** Fixed clock for tests; defaults to now. */
  now?: Date;
}

function header(event: string, input: EnvelopeInput) {
  const now = input.now ?? new Date();
  return {
    event,
    network: input.network ?? input.profile.network,
    timestamp: now.toISOString(),
  };
}

function contractOf(profile: NetworkProfile, symbol: string): string {
  const contracts = profile.contracts();
  return contracts[symbol as StablecoinSymbol] ?? contracts.USDT;
}

export function paymentDetectedEnvelope(input: EnvelopeInput) {
  const now = input.now ?? new Date();
  const { profile, dep } = input;
  return {
    ...header('payment.detected', input),
    data: {
      paymentAddressId: dep.paymentAddressId,
      transactionId: dep.transactionId,
      txHash: input.txHash,
      eventIndex: input.eventIndex,
      fromAddress: profile.buyerWallet,
      toAddress: dep.address,
      contractAddress: contractOf(profile, dep.tokenSymbol),
      tokenSymbol: dep.tokenSymbol,
      amount: input.amount,
      blockTimestampMs: now.getTime(),
      detectedAt: now.toISOString(),
    },
  };
}

export function paymentConfirmedEnvelope(input: Omit<EnvelopeInput, 'amount'>) {
  const now = input.now ?? new Date();
  const { profile, dep } = input;
  return {
    ...header('payment.confirmed', { ...input, amount: '' }),
    data: {
      paymentAddressId: dep.paymentAddressId,
      transactionId: dep.transactionId,
      txHash: input.txHash,
      eventIndex: input.eventIndex,
      blockNumber: profile.blockNumber,
      confirmationCount: profile.paymentConfirmationCount,
      confirmedAt: now.toISOString(),
    },
  };
}

export function wrongTokenEnvelope(input: EnvelopeInput & { actualTokenSymbol: string }) {
  const now = input.now ?? new Date();
  const { profile, dep } = input;
  return {
    ...header('payment.wrong_token', input),
    data: {
      paymentAddressId: dep.paymentAddressId,
      transactionId: dep.transactionId,
      txHash: input.txHash,
      eventIndex: input.eventIndex,
      fromAddress: profile.buyerWallet,
      toAddress: dep.address,
      expectedContractAddress: contractOf(profile, dep.tokenSymbol),
      actualContractAddress: contractOf(profile, input.actualTokenSymbol),
      actualTokenSymbol: input.actualTokenSymbol,
      amount: input.amount,
      blockTimestampMs: now.getTime(),
      detectedAt: now.toISOString(),
    },
  };
}

export function spamTokenEnvelope(input: EnvelopeInput) {
  const now = input.now ?? new Date();
  const { profile, dep } = input;
  return {
    ...header('payment.spam_token', input),
    data: {
      paymentAddressId: dep.paymentAddressId,
      transactionId: dep.transactionId,
      txHash: input.txHash,
      eventIndex: input.eventIndex,
      fromAddress: profile.buyerWallet,
      toAddress: dep.address,
      expectedContractAddress: contractOf(profile, dep.tokenSymbol),
      actualContractAddress: profile.unsupportedContract,
      amount: input.amount,
      blockTimestampMs: now.getTime(),
      detectedAt: now.toISOString(),
    },
  };
}

export function lateDetectedEnvelope(input: EnvelopeInput & { monitorState: string }) {
  const now = input.now ?? new Date();
  const { profile, dep } = input;
  return {
    ...header('payment.late_detected', input),
    data: {
      paymentAddressId: dep.paymentAddressId,
      transactionId: dep.transactionId,
      txHash: input.txHash,
      eventIndex: input.eventIndex,
      fromAddress: profile.buyerWallet,
      toAddress: dep.address,
      contractAddress: contractOf(profile, dep.tokenSymbol),
      tokenSymbol: dep.tokenSymbol,
      amount: input.amount,
      blockTimestampMs: now.getTime(),
      detectedAt: now.toISOString(),
      monitorState: input.monitorState,
    },
  };
}
