import { Interface, getAddress, id, zeroPadValue } from 'ethers';
import { canonicalAddress } from '../address/canonicalAddress.js';

/**
 * The one contract call the signer may make (08 §3.7.6 "İmzacı izin listesi")
 * and the USDT views the service reads before sending (08 §3.7.1, §3.7.6).
 * Function names were checked against mainnet USDT with keyless reads on
 * 2026-10-18 (T152 report, "Dış varsayımlar").
 */

/** `transfer(address,uint256)` selector. */
export const TRANSFER_SELECTOR = '0xa9059cbb';

/** `Transfer(address,address,uint256)` event topic. */
export const TRANSFER_TOPIC = id('Transfer(address,address,uint256)');

export const TOKEN_ABI = new Interface([
  'function transfer(address to, uint256 value)',
  'function deprecated() view returns (bool)',
  'function paused() view returns (bool)',
  'function basisPointsRate() view returns (uint256)',
  'function maximumFee() view returns (uint256)',
  'function isBlackListed(address) view returns (bool)',
]);

/** Calldata of `transfer(to, amount)` — 4 + 32 + 32 bytes. */
export function encodeTransfer(to: string, amountUnits: bigint): string {
  return TOKEN_ABI.encodeFunctionData('transfer', [getAddress(to.toLowerCase()), amountUnits]);
}

/**
 * Strict decode of `transfer(address,uint256)` calldata: exactly 68 bytes, the
 * right selector and a clean (zero-padded) address word. Anything else → null.
 * The signer compares the result to what it was asked to send, so calldata it
 * did not build itself cannot slip through.
 */
export function decodeTransferCall(data: string): { to: string; amountUnits: bigint } | null {
  if (!/^0x[0-9a-fA-F]*$/.test(data)) return null;
  const hex = data.slice(2).toLowerCase();
  if (hex.length !== 8 + 64 + 64) return null;
  if ('0x' + hex.slice(0, 8) !== TRANSFER_SELECTOR) return null;
  const addressWord = hex.slice(8, 72);
  if (!/^0{24}/.test(addressWord)) return null;
  return {
    to: canonicalAddress('0x' + addressWord.slice(24)),
    amountUnits: BigInt('0x' + hex.slice(72)),
  };
}

/** An address as a 32-byte log topic. */
export function addressTopic(address: string): string {
  return zeroPadValue(address.toLowerCase(), 32).toLowerCase();
}
