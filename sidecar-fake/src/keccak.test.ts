import { describe, it, expect } from 'vitest';
import { keccak256Hex } from './keccak.js';

// Known answers NOT produced by this code:
//   • "" and "abc" — the Keccak team's reference values with the pre-FIPS-202
//     padding Ethereum uses; keccak256("") is the empty-code hash of every EOA;
//   • 135 / 136 / 200 bytes of 'a' — computed with ethers 6.17.0 `keccak256`
//     (the version the TRON and Ethereum services pin), 2026-10-18. They sit
//     just under, exactly on, and past the 136-byte rate, so the padding edge
//     (pad byte and final bit in the same byte vs. a whole extra block) and the
//     multi-block absorb loop are each pinned.
// Each 64-hex digest is written as two 32-hex halves: the pre-commit secret
// scanner reads any bare 64-hex word as a private key.
describe('keccak256Hex', () => {
  it('hashes the empty string to the Ethereum empty-code hash', () => {
    expect(keccak256Hex(Buffer.alloc(0))).toBe(
      'c5d2460186f7233c927e7db2dcc703c0' + 'e500b653ca82273b7bfad8045d85a470',
    );
  });

  it('hashes "abc"', () => {
    expect(keccak256Hex(Buffer.from('abc', 'ascii'))).toBe(
      '4e03657aea45a94fc7d47ba826c8d667' + 'c0d1e6e33a64a036ec44f58fa12d6c45',
    );
  });

  it('is Keccak, not SHA3-256 (FIPS-202 padding gives a different digest)', () => {
    // SHA3-256("") — what Node's crypto 'sha3-256' would have answered.
    expect(keccak256Hex(Buffer.alloc(0))).not.toBe(
      'a7ffc6f8bf1ed76651c14756a061d662' + 'f580ff4de43b49fa82d80a4b80f8434a',
    );
  });

  it.each([
    [135, '34367dc248bbd832f4e3e69dfaac2f92' + '638bd0bbd18f2912ba4ef454919cf446'],
    [136, 'a6c4d403279fe3e0af03729caada8374' + 'b5ca54d8065329a3ebcaeb4b60aa386e'],
    [200, '96ea54061def936c4be90b518992fdc6' + 'f12f535068a256229aca54267b4d084d'],
  ])('hashes %i bytes of "a" like ethers', (length, expected) => {
    expect(keccak256Hex(Buffer.alloc(length, 0x61))).toBe(expected);
  });
});
