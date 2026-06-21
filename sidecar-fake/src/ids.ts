import crypto from 'crypto';

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

/** Deterministic numeric Steam trade-offer id. */
export function fakeOfferId(seed: string): string {
  return BigInt('0x' + sha256(`offer:${seed}`).toString('hex').slice(0, 15)).toString();
}

/** Deterministic numeric Steam asset id. */
export function fakeAssetId(seed: string): string {
  return BigInt('0x' + sha256(`asset:${seed}`).toString('hex').slice(0, 15)).toString();
}
