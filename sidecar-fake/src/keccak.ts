/**
 * Keccak-256 (the pre-FIPS-202 padding Ethereum uses — NOT `sha3-256`, which
 * Node's crypto offers but which pads with 0x06 instead of 0x01). The fake
 * needs it for one thing only: writing its Ethereum deposit addresses in
 * EIP-55 mixed case, the way the real Ethereum service answers `derive`
 * (sidecar-ethereum `HdWalletService`). No dependency is pulled in for that —
 * the fake stays an Express + mssql + pino image.
 *
 * Lanes are BigInt: slow, but the fake hashes 40-character strings a handful of
 * times per test run, and BigInt keeps the permutation readable.
 */

const MASK_64 = (1n << 64n) - 1n;

const ROUND_CONSTANTS: readonly bigint[] = [
  0x0000000000000001n,
  0x0000000000008082n,
  0x800000000000808an,
  0x8000000080008000n,
  0x000000000000808bn,
  0x0000000080000001n,
  0x8000000080008081n,
  0x8000000000008009n,
  0x000000000000008an,
  0x0000000000000088n,
  0x0000000080008009n,
  0x000000008000000an,
  0x000000008000808bn,
  0x800000000000008bn,
  0x8000000000008089n,
  0x8000000000008003n,
  0x8000000000008002n,
  0x8000000000000080n,
  0x000000000000800an,
  0x800000008000000an,
  0x8000000080008081n,
  0x8000000000008080n,
  0x0000000080000001n,
  0x8000000080008008n,
];

// Rotation offsets r[x][y], indexed as x + 5y.
const ROTATIONS: readonly number[] = [
  0, 1, 62, 28, 27, 36, 44, 6, 55, 20, 3, 10, 43, 25, 39, 41, 45, 15, 21, 8, 18, 2, 61, 56, 14,
];

function rotl(value: bigint, shift: number): bigint {
  if (shift === 0) return value;
  const s = BigInt(shift);
  return ((value << s) | (value >> (64n - s))) & MASK_64;
}

function keccakF(state: bigint[]): void {
  for (let round = 0; round < 24; round++) {
    // θ
    const c: bigint[] = [];
    for (let x = 0; x < 5; x++) {
      c[x] = state[x] ^ state[x + 5] ^ state[x + 10] ^ state[x + 15] ^ state[x + 20];
    }
    for (let x = 0; x < 5; x++) {
      const d = c[(x + 4) % 5] ^ rotl(c[(x + 1) % 5], 1);
      for (let y = 0; y < 25; y += 5) state[x + y] ^= d;
    }
    // ρ and π
    const b: bigint[] = new Array<bigint>(25);
    for (let x = 0; x < 5; x++) {
      for (let y = 0; y < 5; y++) {
        b[y + 5 * ((2 * x + 3 * y) % 5)] = rotl(state[x + 5 * y], ROTATIONS[x + 5 * y]);
      }
    }
    // χ
    for (let x = 0; x < 5; x++) {
      for (let y = 0; y < 25; y += 5) {
        state[x + y] = b[x + y] ^ (~b[((x + 1) % 5) + y] & MASK_64 & b[((x + 2) % 5) + y]);
      }
    }
    // ι
    state[0] ^= ROUND_CONSTANTS[round];
  }
}

/** Keccak-256 digest of `input` as lowercase hex (64 characters). */
export function keccak256Hex(input: Buffer): string {
  const rate = 136; // bytes — 1088-bit rate for a 256-bit output
  const padded = Buffer.alloc(Math.ceil((input.length + 1) / rate) * rate);
  input.copy(padded);
  padded[input.length] ^= 0x01;
  padded[padded.length - 1] ^= 0x80;

  const state: bigint[] = new Array<bigint>(25).fill(0n);
  for (let offset = 0; offset < padded.length; offset += rate) {
    for (let i = 0; i < rate / 8; i++) {
      state[i] ^= padded.readBigUInt64LE(offset + i * 8);
    }
    keccakF(state);
  }

  const out = Buffer.alloc(32);
  for (let i = 0; i < 4; i++) out.writeBigUInt64LE(state[i], i * 8);
  return out.toString('hex');
}
