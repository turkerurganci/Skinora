import { describe, it, expect, beforeEach } from 'vitest';
import {
  suppressAccept,
  clearSuppressions,
  isAcceptSuppressed,
  listSuppressed,
} from './tradeControl.js';

describe('tradeControl', () => {
  beforeEach(() => clearSuppressions());

  it('defaults to no suppression', () => {
    expect(isAcceptSuppressed('SELLER_TO_BOT')).toBe(false);
    expect(listSuppressed()).toEqual([]);
  });

  it('suppresses only the named direction', () => {
    suppressAccept('SELLER_TO_BOT');
    expect(isAcceptSuppressed('SELLER_TO_BOT')).toBe(true);
    expect(isAcceptSuppressed('BOT_TO_BUYER')).toBe(false);
    expect(listSuppressed()).toEqual(['SELLER_TO_BOT']);
  });

  it('tracks multiple directions independently', () => {
    suppressAccept('SELLER_TO_BOT');
    suppressAccept('BOT_TO_BUYER');
    expect(isAcceptSuppressed('SELLER_TO_BOT')).toBe(true);
    expect(isAcceptSuppressed('BOT_TO_BUYER')).toBe(true);
    expect(isAcceptSuppressed('BOT_TO_SELLER_REFUND')).toBe(false);
    expect(listSuppressed().sort()).toEqual(['BOT_TO_BUYER', 'SELLER_TO_BOT']);
  });

  it('is idempotent on repeated suppression', () => {
    suppressAccept('SELLER_TO_BOT');
    suppressAccept('SELLER_TO_BOT');
    expect(listSuppressed()).toEqual(['SELLER_TO_BOT']);
  });

  it('clears all suppressions', () => {
    suppressAccept('SELLER_TO_BOT');
    suppressAccept('BOT_TO_BUYER');
    clearSuppressions();
    expect(listSuppressed()).toEqual([]);
    expect(isAcceptSuppressed('SELLER_TO_BOT')).toBe(false);
  });
});
