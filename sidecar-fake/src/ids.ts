import crypto from 'crypto';
import { keccak256Hex } from './keccak.js';

const BASE58 = '123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz';

/** Minimal big-endian base58 encode of a byte buffer (no checksum). */
function toBase58(bytes: Buffer): string {
  let num = BigInt('0x' + (bytes.toString('hex') || '0'));
  let out = '';
  const radix = 58n;
  while (num > 0n) {
    const rem = Number(num % radix);
    out = BASE58[rem] + out;
    num /= radix;
  }
  // Preserve leading-zero bytes as '1' (base58 convention).
  for (const b of bytes) {
    if (b === 0) out = '1' + out;
    else break;
  }
  return out;
}

function sha256(seed: string): Buffer {
  return crypto.createHash('sha256').update(seed).digest();
}

/**
 * Deterministic, plausible Tron base58 deposit address derived from the HD
 * wallet index. Always 34 chars starting with 'T'. Not a real on-chain
 * address — the backend only persists the string, so format suffices.
 */
export function fakeTronAddress(index: number): string {
  const body = toBase58(sha256(`tron-addr:${index}`)).replace(/[^1-9A-HJ-NP-Za-km-z]/g, '');
  return ('T' + body).slice(0, 34).padEnd(34, '1');
}

/** Deterministic 64-hex Tron transaction hash for the given seed. */
export function fakeTxHash(seed: string): string {
  return sha256(`tx:${seed}`).toString('hex');
}

/**
 * EIP-55 mixed-case form of a 20-byte hex address: each hex letter is
 * upper-cased where the matching nibble of keccak256(lowercase address) is
 * >= 8. The real Ethereum service answers `derive` in this form; the backend
 * stores the address lower-cased (06 §1.4) and shows it EIP-55 again.
 */
export function toChecksumAddress(address: string): string {
  const lower = address.toLowerCase().replace(/^0x/, '');
  if (!/^[0-9a-f]{40}$/.test(lower)) {
    throw new Error(`not a 20-byte hex address: ${address}`);
  }
  const hash = keccak256Hex(Buffer.from(lower, 'ascii'));
  let out = '0x';
  for (let i = 0; i < 40; i++) {
    out += parseInt(hash[i], 16) >= 8 ? lower[i].toUpperCase() : lower[i];
  }
  return out;
}

/**
 * T155 — deterministic, well-formed Ethereum deposit address for an HD index,
 * EIP-55 cased like the real service's `derive` answer. Seeded apart from
 * {@link fakeTronAddress} so the two networks' index 0 never look related.
 * Not a key-derived address — the backend only persists the string.
 */
export function fakeEthereumAddress(index: number): string {
  return toChecksumAddress(sha256(`ethereum-addr:${index}`).subarray(0, 20).toString('hex'));
}

/** T155 — deterministic Ethereum transaction hash: `0x` + 64 lowercase hex. */
export function fakeEthereumTxHash(seed: string): string {
  return '0x' + sha256(`eth-tx:${seed}`).toString('hex');
}

// T137 — `fakeOfferId` was removed with the custody trade surface: the platform
// sends no trade offers (02 §2.1), so there is no offer id left to mint.

/** Deterministic numeric Steam asset id. */
export function fakeAssetId(seed: string): string {
  return BigInt('0x' + sha256(`asset:${seed}`).toString('hex').slice(0, 15)).toString();
}
