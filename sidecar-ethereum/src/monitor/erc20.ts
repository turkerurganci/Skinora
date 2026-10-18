import { canonicalAddress } from '../address/canonicalAddress.js';
import type { RpcLog } from '../rpc/RpcClient.js';

/**
 * The ERC-20 wire facts the payment monitor relies on (T151 — 08 §3.7.4).
 * Kept apart from the monitor so each rule is a small, table-tested function.
 */

/** keccak256("Transfer(address,address,uint256)") — topic 0 of every ERC-20 (and ERC-721) Transfer. */
export const TRANSFER_TOPIC = '0xddf252ad1be2c89b69c2b068fc378daa952ba7f163c4a11628f55a4df523b3ef';

/** 4-byte selectors of the read-only calls the service makes (no state change, no signing). */
export const SELECTORS = {
  balanceOf: '0x70a08231',
  decimals: '0x313ce567',
  /** Tether's fee switch (08 §3.7.1): both must stay 0. */
  basisPointsRate: '0xdd644f72',
  maximumFee: '0x35390714',
  /** Tether's kill switches (08 §3.7.1, T142 doğrulaması). */
  deprecated: '0x0e136b19',
  paused: '0x5c975abb',
} as const;

const ADDRESS_HEX = /^0x[0-9a-f]{40}$/;
const WORD_HEX = /^0x[0-9a-f]{64}$/;

/** A 20-byte address as a 32-byte topic / ABI word (left-padded, lowercase). */
export function addressWord(address: string): string {
  const canonical = canonicalAddress(address);
  if (!ADDRESS_HEX.test(canonical)) {
    throw new Error(`Not an EVM address: ${address}`);
  }
  return '0x' + canonical.slice(2).padStart(64, '0');
}

/**
 * The address held in a 32-byte topic, or null when the upper 12 bytes are
 * not zero (not an address — a malformed or non-standard event).
 */
export function addressFromWord(word: string): string | null {
  const w = word.toLowerCase();
  if (!WORD_HEX.test(w) || !/^0x0{24}/.test(w)) return null;
  return '0x' + w.slice(26);
}

/** `balanceOf(holder)` call data. */
export function balanceOfCall(holder: string): string {
  return SELECTORS.balanceOf + addressWord(holder).slice(2);
}

/**
 * A uint256 return value. `0x` (no data) is refused rather than read as 0: a
 * call to an address without code returns empty data, and a zero balance
 * read from the wrong contract must not look like a real one.
 */
export function decodeUint(data: string): bigint {
  if (!/^0x[0-9a-fA-F]{64,}$/.test(data)) {
    throw new Error(`Expected a 32-byte uint return value, got ${data.slice(0, 20)}…`);
  }
  return BigInt('0x' + data.slice(2, 66));
}

/** A bool return value (any non-zero word is true, as Solidity decodes it). */
export function decodeBool(data: string): boolean {
  return decodeUint(data) !== 0n;
}

export interface Erc20Transfer {
  /** Canonical (lowercase) emitting contract. */
  readonly contract: string;
  readonly from: string;
  readonly to: string;
  readonly value: bigint;
  readonly txHash: string;
  readonly logIndex: number;
  readonly blockNumber: bigint;
  readonly blockHash: string;
}

/**
 * Decodes an ERC-20 `Transfer(address indexed from, address indexed to,
 * uint256 value)` log. Returns null for anything else that shares topic 0 —
 * most notably ERC-721 `Transfer`, whose token id is a fourth indexed topic
 * and whose data is empty (the TRON service drops TRC-721 the same way,
 * 08 §3.4 "Kayıt türü ön filtresi").
 */
export function decodeTransferLog(log: RpcLog): Erc20Transfer | null {
  if (log.topics.length !== 3 || log.topics[0]?.toLowerCase() !== TRANSFER_TOPIC) return null;
  if (!/^0x[0-9a-fA-F]{64}$/.test(log.data)) return null;
  const from = addressFromWord(log.topics[1]);
  const to = addressFromWord(log.topics[2]);
  if (from === null || to === null) return null;
  return {
    contract: canonicalAddress(log.address),
    from,
    to,
    value: BigInt(log.data),
    txHash: canonicalAddress(log.transactionHash),
    logIndex: log.logIndex,
    blockNumber: log.blockNumber,
    blockHash: log.blockHash,
  };
}

/**
 * Raw base units → fixed-decimal string, the wire format the backend parses
 * without float rounding (same helper as the TRON service:
 * `formatTokenAmount("20400000", 6) === "20.400000"`).
 */
export function formatTokenAmount(raw: bigint, decimals: number): string {
  if (raw < 0n) throw new Error('Negative token amount');
  if (!Number.isInteger(decimals) || decimals < 0 || decimals > 36) {
    throw new Error(`Invalid decimals: ${decimals}`);
  }
  const digits = raw.toString();
  if (decimals === 0) return digits;
  const padded = digits.padStart(decimals + 1, '0');
  return `${padded.slice(0, padded.length - decimals)}.${padded.slice(padded.length - decimals)}`;
}
