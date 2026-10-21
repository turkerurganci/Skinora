import { describe, expect, it } from 'vitest';
import { InternalKeyError, MIN_INTERNAL_KEY_LENGTH, requireInternalKey } from './internalKey.js';

/** T161 — the internal key is required to start (05 §3.4); same rule as the Ethereum service. */
describe('requireInternalKey', () => {
  function codeOf(raw: string | undefined): string | undefined {
    try {
      requireInternalKey(raw);
      return undefined;
    } catch (err) {
      expect(err).toBeInstanceOf(InternalKeyError);
      return (err as InternalKeyError).code;
    }
  }

  it.each([
    ['unset', undefined],
    ['empty', ''],
    ['blank', '   '],
  ])('refuses a key that is %s (INTERNAL_KEY_MISSING)', (_label, raw) => {
    expect(codeOf(raw)).toBe('INTERNAL_KEY_MISSING');
  });

  it('refuses a key shorter than 32 characters (INTERNAL_KEY_TOO_SHORT)', () => {
    expect(MIN_INTERNAL_KEY_LENGTH).toBe(32);
    expect(codeOf('x'.repeat(31))).toBe('INTERNAL_KEY_TOO_SHORT');
    // Surrounding blanks do not count towards the length.
    expect(codeOf(`  ${'x'.repeat(31)}  `)).toBe('INTERNAL_KEY_TOO_SHORT');
  });

  it('accepts 32 characters and more, trimmed', () => {
    expect(requireInternalKey('x'.repeat(32))).toBe('x'.repeat(32));
    expect(requireInternalKey(` ${'a'.repeat(64)}\n`)).toBe('a'.repeat(64));
  });

  it('never puts the key in the error message', () => {
    const short = 'secret-short-key';
    try {
      requireInternalKey(short);
    } catch (err) {
      expect((err as Error).message).not.toContain(short);
    }
  });
});
